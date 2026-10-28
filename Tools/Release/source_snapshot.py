"""Create once after final QA, then compare every client source/asset used by a release.

This command never builds, imports, publishes, contacts a server, or copies file
contents into evidence. --inventory is read-only and does not freeze a snapshot.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]
DEFAULT_VERSION = '0.3.16'
SELECTION = ('Every regular file under Assets (including runtime/editor C#, assembly definitions, '
             'plugins, all Resources artwork/data, sprites and metadata, scenes/prefabs, font sources '
             'and font assets); every ProjectSettings file; Packages manifest/lock; three final '
             'public source PNGs. File contents are represented by sizes and SHA-256 hashes only.')


def sha(path):
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(chunk)
    return digest.hexdigest()


def manifest_sha(rows):
    return hashlib.sha256(json.dumps(rows, sort_keys=True, separators=(',', ':')).encode()).hexdigest()


def safe_regular(path):
    if not path.is_file() or not path.resolve(strict=True).is_relative_to(ROOT):
        raise ValueError('Source is missing or outside the workspace: ' + str(path))
    for ancestor in (path, *path.parents):
        if ancestor == ROOT:
            break
        if ancestor.is_symlink() or ancestor.is_junction():
            raise ValueError('Source symlinks/junctions cannot be frozen: ' + str(ancestor))
    return path


def selected(version, require_screens=True):
    files = set()
    for name in ('Assets', 'ProjectSettings'):
        directory = ROOT / name
        if not directory.is_dir():
            raise FileNotFoundError(directory)
        for path in directory.rglob('*'):
            if path.is_symlink() or path.is_junction():
                raise ValueError('Source selection contains a link: ' + str(path))
            if path.is_file():
                files.add(safe_regular(path))
    for name in ('manifest.json', 'packages-lock.json'):
        files.add(safe_regular(ROOT / 'Packages' / name))
    screens = [ROOT / 'Design' / ('Release-' + version) / 'screens' / (name + '.png')
               for name in ('hub', 'character', 'mine')]
    missing = [str(path.relative_to(ROOT)).replace('\\', '/') for path in screens if not path.is_file()]
    if missing and require_screens:
        raise FileNotFoundError('Final native screenshots are not ready: ' + ', '.join(missing))
    for path in screens:
        if path.is_file():
            safe_regular(path)
            with path.open('rb') as stream:
                header = stream.read(24)
            if len(header) != 24 or header[:8] != b'\x89PNG\r\n\x1a\n' or header[12:16] != b'IHDR':
                raise ValueError('Source screenshot is not a PNG: ' + str(path))
            files.add(path)
    return sorted(files), missing


def entries(version):
    files, _ = selected(version)
    return [{'path': path.relative_to(ROOT).as_posix(), 'bytes': path.stat().st_size,
             'sha256': sha(path)} for path in files]


def read_snapshot(path, version):
    data = json.loads(path.read_text(encoding='utf-8'))
    assert data['schema'] == 2 and data['releaseVersion'] == version
    assert data['manifestSha256'] == manifest_sha(data['files']), 'Snapshot manifest was altered'
    assert len(data['files']) == len({row['path'] for row in data['files']}), 'Duplicate snapshot paths'
    return data


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--version', default=DEFAULT_VERSION)
    actions = parser.add_mutually_exclusive_group()
    actions.add_argument('--create', action='store_true', help='Create a new exclusive snapshot after final sources/art/screens/QA')
    actions.add_argument('--inventory', action='store_true', help='List selection counts and pending screenshots without freezing anything')
    actions.add_argument('--record-sdk-restore', action='store_true', help='Record only the exact expected whole-file SDK36-to-0 cleanup before build')
    args = parser.parse_args()
    if not re.fullmatch(r'\d+\.\d+\.\d+', args.version):
        parser.error('--version must be a three-part numeric release version')
    validation = ROOT / 'Design' / ('Release-' + args.version) / 'validation'
    snapshot = validation / 'source-snapshot.json'
    if args.inventory:
        files, missing = selected(args.version, require_screens=False)
        print(json.dumps({'version': args.version, 'readOnly': True, 'snapshotCreated': False,
                          'fileCount': len(files), 'totalBytes': sum(path.stat().st_size for path in files),
                          'selection': SELECTION, 'pendingScreens': missing}))
        return
    if args.create:
        rows = entries(args.version)
        validation.mkdir(parents=True, exist_ok=True)
        data = {'schema': 2, 'releaseVersion': args.version, 'createdUtc': datetime.now(timezone.utc).isoformat(),
                'selection': SELECTION, 'files': rows, 'manifestSha256': manifest_sha(rows)}
        with snapshot.open('x', encoding='utf-8') as stream:
            json.dump(data, stream, ensure_ascii=False, indent=2)
        print(json.dumps({'created': True, 'version': args.version, 'files': len(rows),
                          'manifestSha256': data['manifestSha256']}))
        return
    before = read_snapshot(snapshot, args.version)
    if args.record_sdk_restore:
        entry = next(row for row in before['files'] if row['path'] == 'ProjectSettings/ProjectSettings.asset')
        content = (ROOT / entry['path']).read_bytes()
        assert sha(ROOT / entry['path']) == entry['sha256'], 'Settings changed since snapshot'
        pattern = rb'(?m)^  AndroidTargetSdkVersion: 36(\r?\n)'
        restored, count = re.subn(pattern, lambda match: b'  AndroidTargetSdkVersion: 0' + match.group(1), content)
        assert count == 1, 'Expected one temporary SDK36 setting, preserving its exact newline'
        entry['expectedAfterBuild'] = {'sha256': hashlib.sha256(restored).hexdigest(), 'bytes': len(restored),
                                       'change': 'Build finally restores temporary AndroidTargetSdkVersion36 to original0; exact whole-file identity only.'}
        before['manifestSha256'] = manifest_sha(before['files'])
        snapshot.write_text(json.dumps(before, ensure_ascii=False, indent=2), encoding='utf-8')
        print(json.dumps({'recordedSdkCleanup': True, 'manifestSha256': before['manifestSha256']}))
        return
    after = entries(args.version)
    expected = {row['path']: row for row in before['files']}
    actual = {row['path']: row for row in after}
    missing = sorted(set(expected) - set(actual))
    added = sorted(set(actual) - set(expected))
    def same(path):
        old, new = expected[path], actual[path]
        return old['sha256'] == new['sha256'] and old['bytes'] == new['bytes']
    def cleanup(path):
        old, new = expected[path].get('expectedAfterBuild'), actual[path]
        return old and old['sha256'] == new['sha256'] and old['bytes'] == new['bytes']
    changed = sorted(path for path in set(expected) & set(actual) if not same(path) and not cleanup(path))
    restored = sorted(path for path in set(expected) & set(actual) if not same(path) and cleanup(path))
    result = {'passed': not (missing or added or changed), 'version': args.version,
              'checkedUtc': datetime.now(timezone.utc).isoformat(), 'fileCount': len(after),
              'snapshotManifestSha256': before['manifestSha256'], 'missing': missing, 'added': added,
              'changed': changed, 'exactExpectedBuildCleanup': restored}
    (validation / 'source-snapshot-check.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(result))
    raise SystemExit(0 if result['passed'] else 1)


if __name__ == '__main__':
    main()
