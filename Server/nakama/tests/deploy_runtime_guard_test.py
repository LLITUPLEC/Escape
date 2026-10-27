"""Owner-authorized test interruptions never bypass validation of server responses."""
import importlib.util
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
