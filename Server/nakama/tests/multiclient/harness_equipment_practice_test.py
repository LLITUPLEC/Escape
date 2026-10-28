"""Offline contract checks for the new network scenarios. No HTTP/SSH/Unity."""
import importlib.util
from pathlib import Path
import unittest

HERE = Path(__file__).resolve().parent
def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec); spec.loader.exec_module(module)
    return module
Harness = load('equipment_practice_harness', HERE / 'run.py')
Runner = load('equipment_practice_runner', HERE / 'isolated_runner.py')


class FakeSocket:
    def __init__(self): self.pending = []; self.board = {}; self.mid = ''
    def packet(self, code, value):
        import base64, json
        return {'match_data': {'match_id': self.mid, 'op_code': str(code),
                              'data': base64.b64encode(json.dumps(value).encode()).decode()}}
    def command(self, kind, value):
        self.mid = value['match_id']
        self.board = dict(globalBossPractice=True, bHp=50000, bMaxHp=50000, aHp=1000, aMaxHp=1000,
            globalBossMyDamage=0, bossActionSequence=0, roundNumber=1, bossOwnTurn=0,
            activeUserId='temporary-owner', turnPaused=True, board=[1,1,2,1,2,3]*6)
        self.pending.append(self.packet(10, self.board)); return {'match': {}}
    def match_data(self, mid, code, payload=None):
        self.mid = mid
        if code == 17:
            self.board['turnPaused'] = False; self.pending.append(self.packet(10, self.board))
        elif code == 13:
            self.board.update(actionActorUserId='temporary-owner', bossActionSequence=1, globalBossMyDamage=100, bHp=49900)
            self.pending.append(self.packet(10, self.board))
        elif code == 12:
            self.pending.append(self.packet(11, dict(globalBossPractice=True,bossAttemptFinished=True,
                bossGlobalMaxHp=50000,bossMyDamage=100,bossGlobalHp=49900,bossQualified=False,bossAttemptRefunded=False)))
    def event(self, mid, code):
        import base64, json
        packet = self.await_message(lambda m: m['match_data']['match_id'] == mid and m['match_data']['op_code'] == str(code))
        return json.loads(base64.b64decode(packet['match_data']['data']))
    def await_message(self, predicate, timeout=0):
        for index, packet in enumerate(self.pending):
            if predicate(packet): return self.pending.pop(index)
        raise AssertionError('Fake socket protocol expectation was not fulfilled')


class ContractTests(unittest.TestCase):
    def test_quote_preflight_keeps_missing_wallet_absent_without_seeding_it(self):
        suite = Harness.Suite.__new__(Harness.Suite); suite.checks = []
        player = {'uid': 'temporary-owner', 'epoch': 7}
        suite.player = lambda: player
        original = lambda method, path, payload=None: self.assertEqual(method, 'GET')
        suite.console = original
        suite.storage_snapshot = lambda uid: {('duel_match3_character', 'sheet'): {'version': 'same', 'value': '{}'}}
        suite.check = lambda passed, text: suite.checks.append(bool(passed))
        calls = []
        def rpc(player, name, payload=None):
            if name == 'duel_character_get': return {'ok': True}
            calls.append(payload)
            return {'ok': False, 'err': 'invalid_craft_quote'}
        suite.rpc = rpc
        suite.quote_preflight()
        self.assertEqual(len(calls), 4)
        self.assertEqual(suite.checks, [True] * 5)
        self.assertIs(suite.console, original)

    def test_quote_preflight_detects_an_unexpected_new_wallet_row(self):
        suite = Harness.Suite.__new__(Harness.Suite); suite.checks = []
        suite.player = lambda: {'uid': 'temporary-owner', 'epoch': 7}
        suite.console = lambda *args: None
        snapshots = [{('duel_match3_character','sheet'): {'version':'same'}},
            {('duel_match3_character','sheet'): {'version':'same'}, ('duel_match3_progress','profile'): {'version':'new'}}]
        suite.storage_snapshot = lambda uid: snapshots.pop(0)
        suite.check = lambda passed,text: suite.checks.append(bool(passed))
        suite.rpc = lambda p,name,payload=None: {'ok':True} if name == 'duel_character_get' else {'ok':False,'err':'invalid_craft_quote'}
        suite.quote_preflight()
        self.assertEqual(suite.checks, [True,True,True,True,False])

    def test_disposable_runner_supplies_catalog_to_both_new_isolated_scenarios(self):
        for name in ('equipment-sets', 'global-boss-practice'):
            args = Runner.suite_arguments(Path('/opt/escape'), Path('/isolated'), 'one', name)
            self.assertIn('--item-catalog', args)
            self.assertEqual(args[args.index('--scenario')+1], name)

    def test_storage_snapshot_paginates_and_keeps_exact_versions(self):
        suite = Harness.Suite.__new__(Harness.Suite); calls=[]
        def console(method, path, payload=None):
            calls.append(path)
            return 200, {'objects':[dict(collection='duel_api_rate_limits',key='v1',user_id='u',version='one',value='{}')],
                         'next_cursor':'next' if len(calls)==1 else ''}
        suite.console = console
        result = suite.storage_snapshot('u')
        self.assertEqual(len(calls),2); self.assertIn('cursor=next', calls[1])
        self.assertEqual(result[('duel_api_rate_limits','v1')]['version'],'one')

    def test_storage_snapshot_refuses_unfiltered_foreign_rows(self):
        suite = Harness.Suite.__new__(Harness.Suite)
        suite.console = lambda *args: (200, {'objects':[dict(collection='c',key='k',user_id='other')]})
        with self.assertRaises(RuntimeError): suite.storage_snapshot('u')

    def test_practice_smoke_console_is_read_only_and_restored_for_cleanup(self):
        suite = Harness.Suite.__new__(Harness.Suite); original=lambda *args: None
        suite.console=original; suite.player=lambda: {}; suite.rpc=lambda *args: {}; suite.connect=lambda *args: object()
        suite._practice_trace=lambda *args,**kwargs: suite.console('PUT','/storage/blocked')
        with self.assertRaisesRegex(RuntimeError,'refused a Console write'): suite.practice_smoke()
        self.assertIs(suite.console, original)

    def test_real_trace_contract_for_two_fresh_matches_and_zero_writes(self):
        suite=Harness.Suite.__new__(Harness.Suite); suite.checks=[]
        suite.check=lambda passed,text:suite.checks.append((bool(passed),text))
        suite.storage=lambda *args: {'max_hp':50000} if args[1]=='config' else {}
        suite.storage_snapshot=lambda uid:{('duel_api_rate_limits','v1'):{'version':'unchanged','value':'{}'}}
        sequence=[]
        def rpc(player,name,payload=None):
            sequence.append(name)
            if payload and 'damage' in payload:return {'ok':False,'err':'bad_payload'}
            if payload and payload.get('session_epoch')==8:return {'ok':False,'err':'stale'}
            return {'ok':True,'practice':True,'spent_token':False,'match_id':'private-'+str(len(sequence))}
        suite.rpc=rpc
        original=Harness.valid_swap; Harness.valid_swap=lambda board:{'actionType':1}
        try:suite._practice_trace({'uid':'temporary-owner','epoch':7},FakeSocket(),2,True)
        finally:Harness.valid_swap=original
        self.assertEqual(len(suite.checks),15)
        self.assertTrue(all(passed for passed,_ in suite.checks),suite.checks)
        self.assertEqual(sequence,['duel_global_boss_practice']*4)

    def test_generic_persistent_rpc_budget_rewrite_is_detected(self):
        suite=Harness.Suite.__new__(Harness.Suite); suite.checks=[]
        suite.check=lambda passed,text:suite.checks.append((bool(passed),text))
        suite.storage=lambda *args: {'max_hp':50000} if args[1]=='config' else {}
        calls=[]
        def snapshot(uid):
            calls.append(uid)
            version='before' if len(calls)<=2 else 'after'
            return {('duel_api_rate_limits','v1'):{'version':version,'value':'{}'}}
        suite.storage_snapshot=snapshot
        suite.rpc=lambda p,name,payload=None: {'ok':False,'err':'bad_payload'} if payload and 'damage'in payload else (
            {'ok':False} if payload else {'ok':True,'practice':True,'spent_token':False,'match_id':'private'})
        original=Harness.valid_swap; Harness.valid_swap=lambda board:{'actionType':1}
        try:suite._practice_trace({'uid':'temporary-owner','epoch':7},FakeSocket(),1,False)
        finally:Harness.valid_swap=original
        self.assertTrue(any(not passed and 'zero durable writes'in text for passed,text in suite.checks))


if __name__=='__main__':unittest.main()
