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

    def idle():
        matches = console.call('GET', '/v2/console/match?limit=1')
        status = console.call('GET', '/v2/console/status')
        sessions = sum(int(node.get('session_count', 0)) for node in status.get('nodes', []))
        active = 0
        cursor = ''
        while True:
            query = {'collection': 'arena_tournament', 'user_id': admin.SYSTEM_USER_ID}
            if cursor:
                query['cursor'] = cursor
            page = console.call('GET', '/v2/console/storage?' + urlencode(query))
            for row in page.get('objects', []):
                value = json.loads(row['value']) if isinstance(row['value'], str) else row['value']
                active += value.get('phase') != 'done'
            cursor = page.get('next_cursor', '')
            if not cursor:
                break
        registry = console.read('arena_queue', 'state')
        queued = 0
        if registry:
            for queue in registry['value'].get('queues', {}).values():
                queued += sum(not player.get('bot', False) for player in queue.get('entries', []))
        print('Idle check: matches=' + str(len(matches.get('matches', []))) + ', tournaments=' + str(active)
              + ', queued=' + str(queued) + ', connected_sessions=' + str(sessions), flush=True)
        if matches.get('matches') or active or queued:
            raise RuntimeError('Escape is busy; no update made')
        # The previous queue was VM-local. Initial migration needs disconnected
        # clients as well, to avoid dropping an in-memory paid queue entry.
        if not registry and sessions:
            raise RuntimeError('Legacy queue migration waits for connected clients to leave')

    try:
        console.login(config['console']['username'], config['console']['password'])
        idle()
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
        idle()  # Last check immediately before the explicitly authorized restart.
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
