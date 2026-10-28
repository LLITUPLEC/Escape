"""Deploy the exact tested Escape runtime with backup, verification and rollback.

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
import sys
import time
from urllib.parse import urlencode
from urllib.error import HTTPError, URLError
from urllib.request import Request, urlopen


ROOT = Path('/opt/escape')


def tree(path):
    result = {}
    for file in sorted(path.rglob('*')):
        if file.is_symlink():
            raise RuntimeError('Runtime symlinks are refused')
        if file.is_file():
            result[file.relative_to(path).as_posix()] = hashlib.sha256(file.read_bytes()).hexdigest()
    return result


def verify_runtime_pins(before, tested, expected_runtime=None, expected_tested=None):
    digest = hashlib.sha256(json.dumps(before, sort_keys=True).encode()).hexdigest()
    if expected_runtime and digest != expected_runtime:
        raise RuntimeError('Production runtime drifted since the pinned preflight; no deployment')
    if expected_tested and tested['sha256'] != expected_tested:
        raise RuntimeError('Tested manifest differs from the pinned candidate; no deployment')
    return digest


def wait_for_api_readiness(timeout_seconds=75, poll_seconds=2, request_timeout_seconds=5):
    """A healthy Nakama container can precede Nginx Docker-DNS cache recovery."""
    if timeout_seconds <= 0 or poll_seconds <= 0 or request_timeout_seconds <= 0:
        raise ValueError('Readiness timeouts must be positive')
    targets = {'gateway': 'http://127.0.0.1:27350/healthcheck',
               'public': 'https://prokrust-play.ru/healthcheck'}
    deadline = time.monotonic() + timeout_seconds
    statuses = dict.fromkeys(targets, 'not_checked')
    attempts = 0
    while time.monotonic() < deadline:
        attempts += 1
        statuses = dict.fromkeys(targets, 'not_checked')
        for label, url in targets.items():
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                break
            try:
                request = Request(url, headers={'Cache-Control': 'no-cache', 'User-Agent': 'Escape-DeployReadiness'})
                with urlopen(request, timeout=min(request_timeout_seconds, remaining)) as response:
                    statuses[label] = response.status if response.geturl() == url else 'redirected'
            except HTTPError as error:
                statuses[label] = error.code
            except (URLError, TimeoutError, OSError):
                statuses[label] = 'unavailable'
        if all(status == 200 for status in statuses.values()):
            print('PASS API readiness: gateway=200 public=200; attempts=' + str(attempts), flush=True)
            return {'attempts': attempts, 'statuses': statuses}
        remaining = deadline - time.monotonic()
        if remaining > 0:
            time.sleep(min(poll_seconds, remaining))
    raise RuntimeError('Escape API readiness timed out: gateway=' + str(statuses['gateway'])
                       + ' public=' + str(statuses['public']))


def run_post_restart_checks():
    # Waiting never substitutes for, retries, or ignores a failed security check.
    wait_for_api_readiness()
    subprocess.run(['python3', 'security_smoke.py', '--url', 'https://prokrust-play.ru'], check=True)
    subprocess.run(['python3', 'dashboard_smoke.py'], check=True)


def restore_catalog_if_unchanged(console, migration, backup_path):
    previous = json.loads(backup_path.read_text())
    expected, _ = migration.merge_catalog(previous['value'])
    current = console.read(migration.COLLECTION, migration.KEY)
    if current and current['value'] == previous['value']:
        return True
    if not current or current['value'] != expected:
        return False
    console.write(migration.COLLECTION, migration.KEY, previous['value'], current['version'])
    restored = console.read(migration.COLLECTION, migration.KEY)
    return bool(restored and restored['value'] == previous['value'])


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


def check_idle(console, system_user_id, allow_test_games=False):
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
    if allow_test_games:
        print('Explicit test-game interruption authorized: Escape Nakama may restart while test games are active', flush=True)
    if not allow_test_games and (matches['matches'] or active or queued):
        raise RuntimeError('Escape is busy; no update made')
    if not allow_test_games and registry is None and sessions:
        raise RuntimeError('Legacy queue migration waits for connected clients to leave')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apply', action='store_true')
    parser.add_argument('--expected-runtime-sha256', help='Pinned production manifest from the read-only preflight; required for apply')
    parser.add_argument('--expected-tested-sha256', help='Pinned complete isolated-tested candidate manifest')
    parser.add_argument('--catalog-migration', type=Path, help='Optional reviewed field-only CAS migration helper under /opt/escape')
    parser.add_argument('--catalog-migration-sha256', help='Pinned SHA256 of the optional migration helper')
    parser.add_argument('--catalog-expected-changes', type=int, help='Exact field-target count from the reviewed old-price preflight')
    parser.add_argument('--catalog-probe-suite', type=Path, help='Pinned production-safe suite proving the active quote guard before catalog CAS')
    parser.add_argument('--catalog-probe-suite-sha256')
    parser.add_argument('--catalog-probe-scenario', default='quote-preflight')
    parser.add_argument('--allow-test-games', action='store_true',
                        help='Explicit owner authorization to interrupt active Escape test games; all hash, backup and health checks remain required')
    args = parser.parse_args()
    if args.apply and not args.expected_runtime_sha256:
        parser.error('--apply requires --expected-runtime-sha256 from a fresh preflight')
    if bool(args.catalog_migration) != bool(args.catalog_migration_sha256):
        parser.error('Catalog helper and its SHA256 must be supplied together')
    if bool(args.catalog_probe_suite) != bool(args.catalog_probe_suite_sha256):
        parser.error('Quote probe suite and its SHA256 must be supplied together')
    if args.apply and args.catalog_migration and not args.catalog_probe_suite:
        parser.error('Catalog apply requires an active-runtime quote guard probe before CAS')
    if args.apply and args.catalog_migration and (args.catalog_expected_changes is None or args.catalog_expected_changes < 1):
        parser.error('Catalog apply requires the positive expected-change count from the reviewed old baseline')
    if Path.cwd().resolve() != ROOT or ROOT.is_symlink():
        raise SystemExit('Run from /opt/escape only')
    os.umask(0o077)
    runtime, overlay = ROOT / 'runtime', ROOT / 'multiclient-runtime-overlay'
    tested = json.loads((ROOT / 'multiclient-runtime-manifest.json').read_text())
    tested_files = tested['files']
    if not tested_files or hashlib.sha256(json.dumps(tested_files, sort_keys=True).encode()).hexdigest() != tested['sha256']:
        raise RuntimeError('Invalid tested runtime manifest')
    before = tree(runtime)
    before_digest = verify_runtime_pins(before, tested, args.expected_runtime_sha256, args.expected_tested_sha256)
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
    migration = None
    if args.catalog_probe_suite:
        probe = args.catalog_probe_suite.resolve(strict=True)
        if (not probe.is_relative_to(ROOT) or args.catalog_probe_suite.is_symlink()
                or hashlib.sha256(probe.read_bytes()).hexdigest() != args.catalog_probe_suite_sha256):
            raise RuntimeError('Quote probe suite is outside scope or differs from its pin')
    if args.catalog_migration:
        helper = args.catalog_migration.resolve(strict=True)
        if (not helper.is_relative_to(ROOT) or args.catalog_migration.is_symlink()
                or hashlib.sha256(helper.read_bytes()).hexdigest() != args.catalog_migration_sha256):
            raise RuntimeError('Catalog helper is outside scope or differs from its pin')
        sys.path.insert(0, str(helper.parent))
        migration_spec = importlib.util.spec_from_file_location('reviewed_catalog_migration', helper)
        migration = importlib.util.module_from_spec(migration_spec)
        migration_spec.loader.exec_module(migration)

    try:
        console.login(config['console']['username'], config['console']['password'])
        check_idle(console, admin.SYSTEM_USER_ID, args.allow_test_games)
        if migration:
            plan = migration.merge_console(console)
            if args.catalog_expected_changes is not None and len(plan['changed_ids']) != args.catalog_expected_changes:
                raise RuntimeError('Catalog target count drifted from the reviewed old-price baseline')
            print('PASS catalog CAS preflight: ' + json.dumps(plan), flush=True)
        if not args.apply:
            print('PASS preflight only; production files unchanged', flush=True)
            return
        stamp = datetime.now(timezone.utc).strftime('%Y%m%dT%H%M%SZ')
        backup = ROOT / 'backups' / ('runtime-release-' + stamp)
        backup.mkdir(mode=0o700)
        shutil.copytree(runtime, backup / 'runtime')
        # Keep the complete pre-change private deployment context with this DB
        # and runtime snapshot. These files are preserved, never reinstalled.
        for name in ('.env', 'compose.yml', 'nginx.conf.template', 'Caddyfile', 'secrets', 'admin', 'tools'):
            source = ROOT / name
            if source.is_symlink() or (source.is_dir() and any(p.is_symlink() for p in source.rglob('*'))):
                raise RuntimeError('Protected deployment context contains a symlink')
            if source.is_dir():
                shutil.copytree(source, backup / name)
            elif source.is_file():
                shutil.copy2(source, backup / name)
            else:
                raise RuntimeError('Protected deployment context is missing: ' + name)
        dump = backup / 'escape.dump'
        with dump.open('xb') as output:
            subprocess.run(['docker', 'compose', '-p', 'escape', 'exec', '-T', 'postgres',
                            'pg_dump', '-U', 'escape', '-d', 'escape', '-Fc'], stdout=output, check=True)
        with dump.open('rb') as source:
            subprocess.run(['docker', 'compose', '-p', 'escape', 'exec', '-T', 'postgres',
                            'pg_restore', '--list'], stdin=source, stdout=subprocess.DEVNULL, check=True)
        backup_files = tree(backup)
        (backup / 'manifest.json').write_text(json.dumps({'schema': 1, 'createdUtc': datetime.now(timezone.utc).isoformat(),
            'runtimeSha256': before_digest, 'pgRestoreListVerified': True, 'files': backup_files}, indent=2))
        if tree(backup / 'runtime') != before:
            raise RuntimeError('Backup runtime differs from the pre-change manifest')
        if tree(runtime) != before:
            raise RuntimeError('Production runtime changed during preparation')
        check_idle(console, admin.SYSTEM_USER_ID, args.allow_test_games)  # Last check immediately before the explicitly authorized restart.
        catalog_before_runtime = None
        if migration:
            catalog_before_runtime = console.read(migration.COLLECTION, migration.KEY)
            _, initial_changes = migration.merge_catalog(catalog_before_runtime['value'])
            if len(initial_changes) != args.catalog_expected_changes:
                raise RuntimeError('Catalog baseline changed before guarded runtime activation')
            with (backup / 'catalog-before-runtime.json').open('x') as output:
                json.dump(catalog_before_runtime, output, ensure_ascii=False, indent=2)
        installed_names = []
        try:
            if tree(runtime) != before:
                raise RuntimeError('Runtime drifted after final preflight; refusing replacement')
            for name, source in updates.items():
                pending = runtime / ('.' + name + '.incoming')
                shutil.copyfile(source, pending)
                pending.chmod(0o644)
                os.replace(pending, runtime / name)
                installed_names.append(name)
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
            if migration:
                wait_for_api_readiness()
                # Activate the safety guard while prices are still old. Only a
                # proven new guard may precede the higher-price CAS; never let
                # the old runtime observe new prices even for a restart window.
                subprocess.run(['python3', str(probe), '--scenario', args.catalog_probe_scenario,
                    '--url', 'https://prokrust-play.ru', '--console-url', 'http://127.0.0.1:27351',
                    '--config', str(ROOT / 'secrets/server.yml'), '--report', str(backup / 'quote-guard-active.json')], check=True)
                probe_report = json.loads((backup / 'quote-guard-active.json').read_text())
                if (not probe_report.get('checks') or probe_report.get('errors')
                        or any(not c.get('passed') for c in probe_report['checks'])):
                    raise RuntimeError('Active quote guard proof failed; old catalog remains untouched')
                print('PASS active quote guard verified at old catalog prices', flush=True)
                applied = migration.merge_console(console, backup / 'catalog-before-migration.json', apply=True)
                (backup / 'catalog-migration-result.json').write_text(json.dumps(applied, indent=2))
                backup_files = tree(backup)
                backup_files.pop('manifest.json', None)
                (backup / 'manifest.json').write_text(json.dumps({'schema': 1, 'createdUtc': datetime.now(timezone.utc).isoformat(),
                    'runtimeSha256': before_digest, 'pgRestoreListVerified': True, 'files': backup_files}, indent=2))
                print('PASS narrow catalog CAS after guarded runtime startup: ' + json.dumps(applied), flush=True)
                # Existing Lua item-def caches live for30seconds. Let all old
                # cache entries expire; cached lower prices remain safe meanwhile.
                expires = time.monotonic() + 31
                while time.monotonic() < expires:
                    time.sleep(max(0, min(2, expires - time.monotonic())))
                print('PASS catalog cache TTL30seconds elapsed after CAS', flush=True)
            run_post_restart_checks()
            (ROOT / 'deployed-runtime-manifest.json').write_text(json.dumps(tested, indent=2))
            print('PASS deployed tested runtime; Escape Nakama healthy; backup=' + str(backup), flush=True)
        except BaseException:
            catalog_safe_for_old_runtime = True
            if migration and (backup / 'catalog-before-migration.json').is_file():
                try:
                    catalog_safe_for_old_runtime = restore_catalog_if_unchanged(console, migration, backup / 'catalog-before-migration.json')
                    if catalog_safe_for_old_runtime:
                        print('Rolled back only the verified catalog fields using current CAS', flush=True)
                    else:
                        print('Catalog changed concurrently; preserved it for manual review against the private backup', flush=True)
                except Exception:
                    catalog_safe_for_old_runtime = False
                    print('Catalog rollback deferred; private DB and exact catalog backups retained', flush=True)
            if migration and catalog_safe_for_old_runtime:
                try:
                    current_catalog = console.read(migration.COLLECTION, migration.KEY)
                    catalog_safe_for_old_runtime = bool(current_catalog and catalog_before_runtime
                        and current_catalog['value'] == catalog_before_runtime['value'])
                except Exception:
                    catalog_safe_for_old_runtime = False
            if not catalog_safe_for_old_runtime:
                # Restoring a legacy runtime against unproven higher prices
                # would reintroduce silent overcharging. Keep the active guard.
                print('Guarded runtime retained: old catalog restoration unproven; private backups retained', flush=True)
                raise
            for name in installed_names:
                old, target = backup / 'runtime' / name, runtime / name
                if target.exists() and hashlib.sha256(target.read_bytes()).hexdigest() != tested_files[name]:
                    print('Runtime file changed concurrently; preserved for manual review: ' + name, flush=True)
                    continue
                if old.exists():
                    shutil.copyfile(old, target)
                    target.chmod(0o644)
                elif target.is_file() and target.parent == runtime:
                    target.unlink()
            if installed_names:
                subprocess.run(['docker', 'compose', '-p', 'escape', 'restart', 'nakama'], check=True)
            print('Rolled back runtime; database backup retained; no database restore attempted', flush=True)
            raise
    finally:
        console.close()


if __name__ == '__main__':
    main()
