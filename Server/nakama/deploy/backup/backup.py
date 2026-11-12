#!/usr/bin/env python3
"""Publish encrypted Escape database backups and serve the restricted SSH export."""

import contextlib
import fcntl
import grp
import hashlib
import json
import os
import re
import shutil
import stat
import subprocess
import sys
import tempfile
from datetime import datetime, timezone
from pathlib import Path


PROJECT = Path('/opt/escape')
STORE = Path('/var/lib/escape-backup')
RECIPIENT = Path('/etc/escape-backup/recipient.pub')
READER_GROUP = 'escape-backup'
SNAPSHOTS = 'snapshots'
RETAIN = 2
STAMP_FORMAT = '%Y%m%dT%H%M%SZ'
SNAPSHOT_NAME = r'escape-([0-9]{8}T[0-9]{6}Z)\.dump\.age'
SHA256 = re.compile(r'[0-9a-f]{64}')
POSTGRES = ('docker', 'compose', '-p', 'escape', 'exec', '-T', 'postgres')


@contextlib.contextmanager
def open_directory(name, parent=None):
    fd = os.open(name, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=parent)
    try:
        info = os.fstat(fd)
        if info.st_uid != 0 or info.st_mode & 0o027:
            raise ValueError('Backup directory must be private and writable only by root')
        yield fd
    finally:
        os.close(fd)


@contextlib.contextmanager
def open_file(name, parent):
    fd = os.open(name, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=parent)
    try:
        info = os.fstat(fd)
        if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o037 or info.st_nlink != 1:
            raise ValueError('Backup file must be a private regular file owned by root')
        with os.fdopen(fd, 'rb', closefd=False) as stream:
            yield stream, info.st_size
    finally:
        os.close(fd)


def creation_time(name):
    stamp = re.fullmatch(SNAPSHOT_NAME, name)
    if stamp is None:
        raise ValueError('Invalid snapshot name')
    return datetime.strptime(stamp[1], STAMP_FORMAT).replace(tzinfo=timezone.utc).isoformat()


def checksum(stream):
    digest = hashlib.sha256()
    for data in iter(lambda: stream.read(1024 * 1024), b''):
        digest.update(data)
    return digest.hexdigest()


def snapshot_names(directory):
    return sorted(
        (name for name in os.listdir(directory) if re.fullmatch(SNAPSHOT_NAME, name)),
        reverse=True,
    )


def snapshot_manifest(snapshot, name):
    with open_file('manifest.json', snapshot) as (stream, size):
        if size > 4096:
            raise ValueError('Invalid snapshot manifest size')
        row = json.load(stream)
    if (
        not isinstance(row, dict)
        or row.get('name') != name
        or row.get('createdUtc') != creation_time(name)
        or type(row.get('bytes')) is not int
        or row['bytes'] <= 0
        or not isinstance(row.get('sha256'), str)
        or not SHA256.fullmatch(row['sha256'])
    ):
        raise ValueError('Invalid snapshot manifest')
    return {key: row[key] for key in ('name', 'createdUtc', 'bytes', 'sha256')}


@contextlib.contextmanager
def open_snapshot(directory, name):
    with open_directory(name, directory) as snapshot:
        row = snapshot_manifest(snapshot, name)
        with open_file('archive.age', snapshot) as (archive, size):
            if row['bytes'] != size:
                raise ValueError('Encrypted archive size differs from the manifest')
            yield row, archive


def publish(directory, staging, name, gid):
    snapshot = staging / 'snapshot'
    snapshot.mkdir(mode=0o700)
    archive = snapshot / 'archive.age'
    with tempfile.TemporaryFile(dir=staging) as dump:
        subprocess.run((*POSTGRES, 'pg_dump', '-U', 'escape', '-d', 'escape', '-Fc', '-w'),
                       cwd=PROJECT, stdout=dump, check=True)
        dump.seek(0)
        subprocess.run((*POSTGRES, 'pg_restore', '--list'), cwd=PROJECT,
                       stdin=dump, stdout=subprocess.DEVNULL, check=True)
        dump.seek(0)
        subprocess.run(('age', '--encrypt', '--recipients-file', str(RECIPIENT), '--output', str(archive)),
                       stdin=dump, check=True)

    with archive.open('rb') as encrypted:
        row = {
            'name': name,
            'createdUtc': creation_time(name),
            'bytes': os.fstat(encrypted.fileno()).st_size,
            'sha256': checksum(encrypted),
        }
        if row['bytes'] == 0:
            raise ValueError('Empty encrypted archive')
        os.fchown(encrypted.fileno(), 0, gid)
        os.fchmod(encrypted.fileno(), 0o640)
        os.fsync(encrypted.fileno())
    manifest = snapshot / 'manifest.json'
    with manifest.open('x', encoding='utf-8') as stream:
        json.dump(row, stream)
        stream.write('\n')
        stream.flush()
        os.fchown(stream.fileno(), 0, gid)
        os.fchmod(stream.fileno(), 0o640)
        os.fsync(stream.fileno())
    with open_directory(snapshot) as prepared:
        os.fchown(prepared, 0, gid)
        os.fchmod(prepared, 0o750)
        os.fsync(prepared)
    os.rename(snapshot, name, dst_dir_fd=directory)
    os.fsync(directory)
    return row


def prune(directory, staging):
    names = snapshot_names(directory)
    for name in names[:RETAIN]:
        with open_snapshot(directory, name) as (row, archive):
            if checksum(archive) != row['sha256']:
                raise ValueError(f'Encrypted archive checksum mismatch: {name}')
    for name in names[RETAIN:]:
        with open_snapshot(directory, name):
            os.rename(name, staging / name, src_dir_fd=directory)
    os.fsync(directory)


def create_backup():
    if os.geteuid() != 0:
        raise ValueError('Only root can create a database backup')
    gid = grp.getgrnam(READER_GROUP).gr_gid
    os.umask(0o077)
    with open_directory(STORE) as store, open_directory(SNAPSHOTS, store) as directory:
        lock = os.open('.lock', os.O_WRONLY | os.O_CREAT | os.O_NOFOLLOW, 0o600, dir_fd=store)
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
            name = datetime.now(timezone.utc).strftime(f'escape-{STAMP_FORMAT}.dump.age')
            if name in snapshot_names(directory):
                raise ValueError('A backup already exists for this timestamp')
            with tempfile.TemporaryDirectory(prefix='.stage-', dir=STORE) as stage:
                staging = Path(stage)
                row = publish(directory, staging, name, gid)
                prune(directory, staging)
        finally:
            os.close(lock)
    print(json.dumps(row))


def export_backup(command):
    requested = re.fullmatch(f'get ({SNAPSHOT_NAME})', command)
    if command != 'list' and requested is None:
        raise ValueError('Allowed commands are list and get <snapshot-name>')
    with open_directory(STORE) as store, open_directory(SNAPSHOTS, store) as directory:
        if requested is not None:
            with open_snapshot(directory, requested[1]) as (_, archive):
                shutil.copyfileobj(archive, sys.stdout.buffer, length=1024 * 1024)
            return
        rows = []
        for name in snapshot_names(directory)[:RETAIN]:
            try:
                with open_snapshot(directory, name) as (row, _):
                    rows.append(row)
            except FileNotFoundError:
                # A concurrent backup may retire an archive after listdir.
                continue
        print(json.dumps({'schema': 1, 'backups': rows}))


def main():
    try:
        if sys.argv[1:] == ['create']:
            create_backup()
        elif sys.argv[1:] == ['export']:
            export_backup(os.environ.get('SSH_ORIGINAL_COMMAND', ''))
        else:
            raise ValueError('Usage: backup.py {create|export}')
    except (OSError, ValueError, KeyError, subprocess.CalledProcessError) as error:
        print(f'Escape backup: {error}', file=sys.stderr)
        return 1
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
