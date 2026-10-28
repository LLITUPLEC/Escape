"""Owner-authorized test interruptions never bypass validation of server responses."""
import importlib.util
import hashlib
import json
import tempfile
from copy import deepcopy
from pathlib import Path
import unittest
from unittest.mock import patch
from urllib.error import HTTPError, URLError
import subprocess

SPEC = importlib.util.spec_from_file_location('escape_deploy', Path(__file__).parents[1] / 'deploy/deploy_runtime.py')
DEPLOY = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(DEPLOY)
OWNER = '00000000-0000-0000-0000-000000000000'


class Console:
    malformed = False

    def call(self, method, path):
        if path.startswith('/v2/console/match'):
            return {'matches': [{'match_id': 'test-only'}]}
        if path == '/v2/console/status':
            return {'nodes': [{'session_count': 2}]}
        return {'objects': [{'collection': 'arena_tournament', 'user_id': OWNER,
                             'key': 'test', 'value': {'phase': 'playing'}}],
                'next_cursor': None if self.malformed else ''}

    def read(self, collection, key):
        return {'collection': collection, 'key': key, 'user_id': OWNER,
                'value': {'queues': {'smith': {'entries': [{'bot': False}]}}}}


class GuardTest(unittest.TestCase):
    def test_transient_gateway_502_waits_for_both_paths_before_full_smokes(self):
        class Clock:
            now = 0
            def monotonic(self): return self.now
            def sleep(self, seconds): self.now += seconds
        clock = Clock(); requests = []; calls = {}
        class Response:
            status = 200
            def __init__(self, url): self.url = url
            def __enter__(self): return self
            def __exit__(self, *_): return False
            def geturl(self): return self.url
        def open_request(request, timeout):
            url = request.full_url; calls[url] = calls.get(url, 0) + 1
            requests.append((url, timeout))
            if calls[url] == 1: raise HTTPError(url, 502, 'temporary DNS cache miss', {}, None)
            return Response(url)
        with patch.object(DEPLOY.time, 'monotonic', clock.monotonic), patch.object(DEPLOY.time, 'sleep', clock.sleep), \
                patch.object(DEPLOY, 'urlopen', open_request), patch.object(DEPLOY.subprocess, 'run') as smoke:
            DEPLOY.run_post_restart_checks()
        self.assertEqual(clock.now, 2)
        self.assertEqual(list(calls.values()), [2, 2])
        self.assertTrue(all(0 < timeout <= 5 for _, timeout in requests))
        self.assertEqual(smoke.call_args_list[0].args[0], ['python3','security_smoke.py','--url','https://prokrust-play.ru'])
        self.assertEqual(smoke.call_args_list[1].args[0], ['python3','dashboard_smoke.py'])
        self.assertTrue(all(call.kwargs['check'] for call in smoke.call_args_list))

    def test_persistent_failure_of_either_route_never_runs_smoke(self):
        for failed_route in ('gateway', 'public'):
            with self.subTest(failed_route=failed_route):
                clock = [0]
                def sleep(seconds): clock[0] += seconds
                class Response:
                    status = 200
                    def __init__(self, url): self.url = url
                    def __enter__(self): return self
                    def __exit__(self, *_): return False
                    def geturl(self): return self.url
                def open_request(request, timeout):
                    failed = ('127.0.0.1' in request.full_url) == (failed_route == 'gateway')
                    if failed: raise HTTPError(request.full_url, 502, 'gateway unavailable', {}, None)
                    return Response(request.full_url)
                original = DEPLOY.wait_for_api_readiness
                with patch.object(DEPLOY.time, 'monotonic', lambda: clock[0]), patch.object(DEPLOY.time, 'sleep', sleep), \
                        patch.object(DEPLOY, 'urlopen', open_request), patch.object(DEPLOY.subprocess, 'run') as smoke, \
                        patch.object(DEPLOY, 'wait_for_api_readiness', lambda: original(timeout_seconds=6)):
                    with self.assertRaisesRegex(RuntimeError, 'readiness timed out'):
                        DEPLOY.run_post_restart_checks()
                    smoke.assert_not_called()
                self.assertEqual(clock[0], 6)

    def test_readiness_transport_failure_is_bounded_and_does_not_expose_error_contents(self):
        clock = [0]; request_timeouts = []
        def open_request(request, timeout):
            request_timeouts.append(timeout); clock[0] += timeout
            raise URLError('sensitive fixture value must not appear')
        with patch.object(DEPLOY.time, 'monotonic', lambda: clock[0]), \
                patch.object(DEPLOY.time, 'sleep', lambda seconds: clock.__setitem__(0, clock[0] + seconds)), \
                patch.object(DEPLOY, 'urlopen', open_request):
            with self.assertRaisesRegex(RuntimeError, 'gateway=unavailable public=not_checked') as raised:
                DEPLOY.wait_for_api_readiness(timeout_seconds=3)
        self.assertEqual(clock[0], 3)
        self.assertEqual(request_timeouts, [3])
        self.assertNotIn('sensitive fixture', str(raised.exception))

    def test_a_failed_security_smoke_is_not_retried_or_ignored_after_readiness(self):
        with patch.object(DEPLOY, 'wait_for_api_readiness') as readiness, \
                patch.object(DEPLOY.subprocess, 'run', side_effect=subprocess.CalledProcessError(1, ['security_smoke.py'])) as smoke:
            with self.assertRaises(subprocess.CalledProcessError): DEPLOY.run_post_restart_checks()
        readiness.assert_called_once_with(); smoke.assert_called_once()
    def test_catalog_rollback_uses_current_cas_only_for_the_exact_own_patch(self):
        previous = {'items': {'epic': {'craft_ingot_n': 180}}, 'admin': 'preserved'}
        updated = deepcopy(previous); updated['items']['epic']['craft_ingot_n'] = 560
        class Migration:
            COLLECTION, KEY = 'defs', 'catalog'
            @staticmethod
            def merge_catalog(value):
                result = deepcopy(value); result['items']['epic']['craft_ingot_n'] = 560
                return result, ['epic']
        class Store:
            def __init__(self, value): self.value, self.writes = deepcopy(value), []
            def read(self, *_): return {'value': deepcopy(self.value), 'version': 'fresh-version'}
            def write(self, collection, key, value, version):
                self.writes.append((collection, key, version)); self.value = deepcopy(value)
        scratch = Path(__file__).resolve().parents[3] / '.cache/qa/deploy-runtime-tests'
        scratch.mkdir(parents=True, exist_ok=True)
        with tempfile.TemporaryDirectory(dir=scratch) as folder:
            backup = Path(folder) / 'before.json'; backup.write_text(json.dumps({'value': previous}))
            store = Store(updated)
            self.assertTrue(DEPLOY.restore_catalog_if_unchanged(store, Migration, backup))
            self.assertEqual(store.value, previous)
            self.assertEqual(store.writes, [('defs', 'catalog', 'fresh-version')])
            unchanged = Store(previous)
            self.assertTrue(DEPLOY.restore_catalog_if_unchanged(unchanged, Migration, backup))
            self.assertEqual(unchanged.writes, [])
            concurrent = deepcopy(updated); concurrent['admin'] = 'operator edit'
            store = Store(concurrent)
            self.assertFalse(DEPLOY.restore_catalog_if_unchanged(store, Migration, backup))
            self.assertEqual(store.value, concurrent); self.assertEqual(store.writes, [])

    def test_runtime_and_tested_pins_accept_only_the_preflight_pair(self):
        before = {'duel.lua': 'old', 'zz_escape_seed.lua': 'preserved'}
        digest = hashlib.sha256(json.dumps(before, sort_keys=True).encode()).hexdigest()
        self.assertEqual(DEPLOY.verify_runtime_pins(before, {'sha256': 'tested'}, digest, 'tested'), digest)
        for changed in ({'duel.lua': 'admin-edit', 'zz_escape_seed.lua': 'preserved'},
                        {'duel.lua': 'old', 'zz_escape_seed.lua': 'changed'}):
            with self.assertRaisesRegex(RuntimeError, 'drifted'):
                DEPLOY.verify_runtime_pins(changed, {'sha256': 'tested'}, digest, 'tested')
        with self.assertRaisesRegex(RuntimeError, 'pinned candidate'):
            DEPLOY.verify_runtime_pins(before, {'sha256': 'different'}, digest, 'tested')

    def test_default_still_refuses_active_games(self):
        with self.assertRaisesRegex(RuntimeError, 'busy'):
            DEPLOY.check_idle(Console(), OWNER)

    def test_explicit_flag_allows_only_the_busy_state(self):
        DEPLOY.check_idle(Console(), OWNER, allow_test_games=True)

    def test_flag_preserves_malformed_response_rejection(self):
        console = Console()
        console.malformed = True
        with self.assertRaisesRegex(RuntimeError, 'cursor'):
            DEPLOY.check_idle(console, OWNER, allow_test_games=True)


if __name__ == '__main__':
    unittest.main()
