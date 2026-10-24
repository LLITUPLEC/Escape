"""Deploy the exact tested Escape runtime only during an idle window.

Run server-locally in /opt/escape after isolated regression tests. Without
--apply this only validates hashes and reads status. Never manages other projects.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import time
from urllib.parse import urlencode


ROOT = Path('/opt/escape')


def tree(path):
    result = {}
    for file in sorted(path.rglob('*')):
        if file.is_symlink():
            raise RuntimeError('Runtime symlinks are refused')
        if file.is_file():
            result[file.relative_to(path).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    return result


def storage_value(console, row, collection, system_user_id, expected_key=None):
    """Console LIST returns metadata only; verify identity before reading detail."""
    def verify(item):
        if not isinstance(item, dict) or item.get('collection') != collection:
            raise RuntimeError('Unexpected Console storage collection/row shape')
        key = item.get('key')
        owners = [item[name] for name in ('user_id', 'userId') if name in item]
        if not isinstance(key, str) or not key or (expected_key is not None and key != expected_key):
            raise RuntimeError('Missing or mismatched Console storage key')
        if not owners or any(owner != system_user_id for owner in owners):
            raise RuntimeError('Console storage object is not explicitly system-owned')
        return key

    key = verify(row)
    if row.get('value') is None:
        detail = console.read(collection, key)
        if detail is None:
            raise RuntimeError('Listed storage object disappeared; idle state is unproven')
        expected_key = key
        verify(detail)
        row = detail
    value = row.get('value')
    if isinstance(value, str):
        try:
            value = json.loads(value)
        except ValueError:
            raise RuntimeError('Invalid Console storage JSON; idle state is unproven') from None
    if not isinstance(value, dict):
        raise RuntimeError('Console storage value must be an object')
    return value


def check_idle(console, system_user_id):
    matches = console.call('GET', '/v2/console/match?limit=1')
    status = console.call('GET', '/v2/console/status')
    if not isinstance(matches, dict) or not isinstance(matches.get('matches'), list):
        raise RuntimeError('Unrecognized Console match list; idle state is unproven')
    if not isinstance(status, dict) or not isinstance(status.get('nodes'), list) or not status['nodes']:
        raise RuntimeError('Missing Console node status; idle state is unproven')
    sessions = 0
    for node in status['nodes']:
        count = node.get('session_count') if isinstance(node, dict) else None
        if type(count) is not int or count < 0:
            raise RuntimeError('Missing/invalid connected session count; idle state is unproven')
        sessions += count
    active, cursor, seen_cursors, seen_keys = 0, '', set(), set()
    while True:
        query = {'collection': 'arena_tournament', 'user_id': system_user_id, 'limit': 100}
        if cursor:
            query['cursor'] = cursor
        page = console.call('GET', '/v2/console/storage?' + urlencode(query))
        if not isinstance(page, dict) or not isinstance(page.get('objects'), list):
            raise RuntimeError('Unrecognized Console storage page; idle state is unproven')
        if 'next_cursor' not in page and 'nextCursor' not in page:
            raise RuntimeError('Storage pagination cursor missing; idle state is unproven')
        next_cursor = page.get('next_cursor', page.get('nextCursor'))
        if not isinstance(next_cursor, str) or ('next_cursor' in page and 'nextCursor' in page and page['next_cursor'] != page['nextCursor']):
            raise RuntimeError('Invalid Console storage pagination cursor')
        if 'total_count' in page:
            total = page['total_count']
            if type(total) is not int or total < 0:
                raise RuntimeError('Invalid Console storage total count')
            # Console total_count is unfiltered (32 versus one system-owned
            # tournament in the live API). Only next_cursor terminates this scan.
        for row in page['objects']:
            value = storage_value(console, row, 'arena_tournament', system_user_id)
            if row['key'] in seen_keys:
                raise RuntimeError('Duplicate tournament page entry; idle state is unproven')
            seen_keys.add(row['key'])
            phase = value.get('phase')
            if not isinstance(phase, str) or not phase:
                raise RuntimeError('Missing tournament phase; idle state is unproven')
            active += phase != 'done'
        if not next_cursor:
            break
        if next_cursor in seen_cursors:
            raise RuntimeError('Repeated storage cursor; idle state is unproven')
        seen_cursors.add(next_cursor)
        if len(seen_cursors) >= 10000:
            raise RuntimeError('Storage pagination limit reached; idle state is unproven')
        cursor = next_cursor
    registry = console.read('arena_queue', 'state')
    queued = 0
    if registry is not None:
        value = storage_value(console, registry, 'arena_queue', system_user_id, 'state')
        if not isinstance(value.get('queues'), dict):
            raise RuntimeError('Queue registry shape is unrecognized; idle state is unproven')
        for queue in value['queues'].values():
            entries = queue.get('entries') if isinstance(queue, dict) else None
            # Nakama serializes an empty Lua array/table as {}. Only the empty
            # object is equivalent to []; a nonempty object remains malformed.
            if type(entries) is dict and not entries:
                entries = []
            if not isinstance(entries, list):
                raise RuntimeError('Queue entries shape is unrecognized; idle state is unproven')
            for player in entries:
                if not isinstance(player, dict) or type(player.get('bot', False)) is not bool:
                    raise RuntimeError('Queue participant shape is unrecognized; idle state is unproven')
                queued += not player.get('bot', False)
    print('Idle check: matches=' + str(len(matches['matches'])) + ', tournaments=' + str(active)
          + ', queued=' + str(queued) + ', connected_sessions=' + str(sessions), flush=True)
    if matches['matches'] or active or queued:
        raise RuntimeError('Escape is busy; no update made')
    if registry is None and sessions:
        raise RuntimeError('Legacy queue migration waits for connected clients to leave')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    args = parser.parse_args()
    if Path.cwd().resolve() != ROOT or ROOT.is_symlink():
        raise SystemExit('Run from /opt/escape only')
    os.umask(0o077)
    runtime, overlay = ROOT / 'runtime', ROOT / 'multiclient-runtime-overlay'
    tested = json.loads((ROOT / 'multiclient-runtime-manifest.json').read_text())
    tested_files = tested['files']
    if not tested_files or hashlib.sha256(json.dumps(tested_files, sort_keys=True).encode()).hexdigest() != tested['sha256']:
        raise RuntimeError('Invalid tested runtime manifest')
    before = tree(runtime)
    candidate = dict(before)
    updates = {}
    for file in overlay.iterdir():
        if not file.is_file() or file.is_symlink() or file.suffix != '.lua':
            raise RuntimeError('Unexpected test overlay entry')
        updates[file.name] = file
        candidate[file.name] = hashlib.sha256(file.read_bytes()).hexdigest()
    if candidate != tested_files:
        raise RuntimeError('Candidate differs from the tested runtime; no deployment')
    print('PASS candidate equals tested runtime ' + tested['sha256'], flush=True)

    spec = importlib.util.spec_from_file_location('storage_admin', ROOT / 'tools/storage_admin.py')
    admin = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(admin)
    config = json.loads((ROOT / 'secrets/server.yml').read_text())
    console = admin.Console('http://127.0.0.1:27351')

    try:
        console.login(config['console']['username'], config['console']['password'])
        check_idle(console, admin.SYSTEM_USER_ID)
        if not args.apply:
            print('PASS preflight only; production files unchanged', flush=True)
            return
        stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
        backup = ROOT / 'backups' / ('runtime-release-' + stamp)
        backup.mkdir(mode=0o700)
        shutil.copytree(runtime, backup / 'runtime')
        dump = backup / 'escape.dump'
        with dump.open('xb') as output:
            subprocess.run(['docker', 'compose', '-p', 'escape', 'exec', '-T', 'postgres',
                            'pg_dump', '-U', 'escape', '-d', 'escape', '-Fc'], stdout=output, check=True)
        with dump.open('rb') as source:
            subprocess.run(['docker', 'compose', '-p', 'escape', 'exec', '-T', 'postgres',
                            'pg_restore', '--list'], stdin=source, stdout=subprocess.DEVNULL, check=True)
        if tree(runtime) != before:
            raise RuntimeError('Production runtime changed during preparation')
        check_idle(console, admin.SYSTEM_USER_ID)  # Last check immediately before the explicitly authorized restart.
        try:
            for name, source in updates.items():
                pending = runtime / ('.' + name + '.incoming')
                shutil.copyfile(source, pending)
                pending.chmod(0o644)
                os.replace(pending, runtime / name)
            if tree(runtime) != tested_files:
                raise RuntimeError('Installed runtime hash mismatch')
            subprocess.run(['docker', 'compose', '-p', 'escape', 'restart', 'nakama'], check=True)
            deadline = time.monotonic() + 75
            while time.monotonic() < deadline:
                health = subprocess.run(['docker', 'inspect', '--format', '{{.State.Health.Status}}',
                                         'escape-nakama-1'], capture_output=True, text=True, check=True)
                if health.stdout.strip() == 'healthy':
                    break
                time.sleep(2)
            else:
                raise RuntimeError('Nakama healthcheck timed out')
            subprocess.run(['python3', 'security_smoke.py', '--url', 'https://prokrust-play.ru'], check=True)
            subprocess.run(['python3', 'dashboard_smoke.py'], check=True)
            (ROOT / 'deployed-runtime-manifest.json').write_text(json.dumps(tested, indent=2))
            print('PASS deployed tested runtime; Escape Nakama healthy; backup=' + str(backup), flush=True)
        except BaseException:
            for name in updates:
                old, target = backup / 'runtime' / name, runtime / name
                if old.exists():
                    shutil.copyfile(old, target)
                    target.chmod(0o644)
                elif target.is_file() and target.parent == runtime:
                    target.unlink()
            subprocess.run(['docker', 'compose', '-p', 'escape', 'restart', 'nakama'], check=True)
            print('Rolled back runtime; database was not changed by deployment', flush=True)
            raise
    finally:
        console.close()


if __name__ == '__main__':
    main()
