"""Owner-authorized test interruptions never bypass validation of server responses."""
import importlib.util
import hashlib
import json
import tempfile
from copy import deepcopy
from pathlib import Path
import unittest

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
