"""Offline safety/selector checks for the real reconnect network scenario."""
import base64
import importlib.util
import json
from pathlib import Path
from types import SimpleNamespace
import unittest

DIRECTORY = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('reconnect_suite', DIRECTORY / 'run.py')
run = importlib.util.module_from_spec(spec)
spec.loader.exec_module(run)
spec = importlib.util.spec_from_file_location('reconnect_isolated', DIRECTORY / 'isolated_runner.py')
isolated = importlib.util.module_from_spec(spec)
spec.loader.exec_module(isolated)


class ReconnectHarnessTests(unittest.TestCase):
    def test_new_scenario_refuses_production_before_account_creation_or_storage(self):
        suite = object.__new__(run.Suite)
        suite.args = SimpleNamespace(url='https://prokrust-play.ru', console_url='http://localhost:27351')
        suite.player = lambda: self.fail('Remote account operation reached from isolated-only scenario')
        with self.assertRaisesRegex(RuntimeError, 'requires isolated'):
            suite.reconnect_0317()

    def test_accepted_ability_requires_actual_actor_type_and_match_not_any_board(self):
        client = object.__new__(run.SocketClient)
        def packet(mid, actor, action):
            value = {'actionActorUserId': actor, 'actionType': action, 'aMana': 10, 'aPetardCd': 3}
            return {'match_data': {'match_id': mid, 'op_code': '10', 'data': base64.b64encode(json.dumps(value).encode()).decode()}}
        messages = [packet('other', 'owner', 4), packet('own', 'bot', 4), packet('own', 'owner', 1), packet('own', 'owner', 4)]
        def wait(predicate, timeout):
            accepted = [m for m in messages if predicate(m)]
            self.assertEqual(len(accepted), 1)
            self.assertEqual(accepted[0], messages[-1])
            return accepted[0]
        client.await_message = wait
        self.assertEqual(client.accepted_action('own', 'owner', 4)['aPetardCd'], 3)

    def test_played_surrender_fixture_requires_server_ready_proof_for_both_connections(self):
        suite = object.__new__(run.Suite)
        replies = iter([{'ok': True, 'active': True, 'battle_started': False}, {'ok': True, 'active': True, 'battle_started': True}])
        suite.rpc = lambda *_: next(replies)
        class Socket:
            def __init__(self): self.operations = []
            def match_data(self, mid, opcode): self.operations.append((mid, opcode))
            def event(self, mid, opcode): return {'turnPaused': False, 'board': [1]}
        a, b = Socket(), Socket()
        result = suite.begin_played_match('own', [({'uid': 'a'}, a), ({'uid': 'b'}, b)])
        self.assertEqual(a.operations, [('own', 17)])
        self.assertEqual(b.operations, [('own', 17)])
        self.assertEqual(set(result), {'a', 'b'})

    def test_played_fixture_does_not_accept_an_uncommitted_ready_ack(self):
        suite = object.__new__(run.Suite)
        suite.rpc = lambda *_: {'ok': True, 'active': True, 'battle_started': False}
        socket = SimpleNamespace(match_data=lambda *_: None, event=lambda *_: {'board': [1]})
        with self.assertRaisesRegex(RuntimeError, 'Ready failed'):
            suite.begin_played_match('own', [({'uid': 'a'}, socket)])

    def test_disposable_runner_passes_the_canonical_catalog_and_exact_loopback_ports(self):
        args = isolated.suite_arguments(Path('/opt/escape'), Path('/opt/escape/tests/owned'), 'escape-multiclient-owned', 'reconnect-0317')
        self.assertEqual(args[args.index('--url') + 1], 'http://127.0.0.1:28350')
        self.assertEqual(args[args.index('--console-url') + 1], 'http://127.0.0.1:28351')
        self.assertEqual(args[args.index('--item-catalog') + 1], str(Path('/opt/escape/tests/owned/item-catalog.json')))

    def test_only_exact_idempotent_forfeit_retries_one_lost_ack_with_frozen_account(self):
        suite = object.__new__(run.Suite)
        original = {'uid': 'owner', 'epoch': 7, 'token': 'offline-test-token'}
        calls = []
        def rpc(player, name, payload):
            calls.append((dict(player), name, dict(payload)))
            if len(calls) == 1:
                original['epoch'] = 8
                raise run.RemoteDisconnected('injected acknowledgement loss')
            return {'ok': True, 'terminal': True}
        suite.rpc = rpc
        self.assertTrue(suite.forfeit_with_lost_ack_retry(original, 'immutable-match')['terminal'])
        self.assertEqual(len(calls), 2)
        self.assertEqual(calls[0], calls[1])
        self.assertEqual(calls[1][0]['epoch'], 7)
        self.assertEqual(calls[1][1:], ('duel_match3_forfeit', {'match_id': 'immutable-match'}))

    def test_forfeit_retry_does_not_hide_any_other_error(self):
        suite = object.__new__(run.Suite)
        calls = []
        def rpc(*_):
            calls.append(True)
            raise RuntimeError('real server error')
        suite.rpc = rpc
        with self.assertRaisesRegex(RuntimeError, 'real server error'):
            suite.forfeit_with_lost_ack_retry({'epoch': 7}, 'own')
        self.assertEqual(len(calls), 1)

    def test_repeated_disconnection_is_still_a_failure_after_one_retry(self):
        suite = object.__new__(run.Suite)
        calls = []
        def rpc(*_):
            calls.append(True)
            raise run.RemoteDisconnected('still unavailable')
        suite.rpc = rpc
        with self.assertRaises(run.RemoteDisconnected):
            suite.forfeit_with_lost_ack_retry({'epoch': 7}, 'own')
        self.assertEqual(len(calls), 2)


if __name__ == '__main__':
    unittest.main()
