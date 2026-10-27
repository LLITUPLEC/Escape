"""Run actual Nakama Lua unit suites locally; no server or account changes."""
import argparse
import hashlib
import json
import os
import sys
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
os.chdir(ROOT)
sys.path.insert(0, str(ROOT / '.cache/lua-test-runtime'))
from lupa.lua51 import LuaRuntime
from lupa.lua54 import LuaRuntime as Lua54Runtime

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, default=ROOT / '.cache/qa/backend-local')
args = parser.parse_args()
OUT = args.output
OUT.mkdir(parents=True, exist_ok=True)
MODULES = ROOT / 'Server/nakama/modules'
def hashes():
    return {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sorted(MODULES.glob('*.lua'))}

before = hashes()
results = []
for test in sorted((ROOT / 'Server/nakama/tests').glob('*.lua')):
    if test.name in ('storage_fault_fixture.lua','global_boss_engine_fixture.lua'):
        continue
    lines = []
    # These tests intentionally yield through pcall while interleaving storage.
    # Lua 5.1 disallows that; their documented runner is Lua 5.4.
    constructor = Lua54Runtime if test.name in ('arena_concurrency_audit.lua', 'cosmetics_security_test.lua') else LuaRuntime
    lua = constructor(unpack_returned_tuples=True)
    lua.globals().print = lambda *args: lines.append('\t'.join(str(x) for x in args))
    error = None
    try:
        lua.execute(test.read_text(encoding='utf-8-sig'))
    except Exception as exc:
        error = str(exc)
        lines.append('ERROR ' + error)
    (OUT / (test.name + '.log')).write_text('\n'.join(lines) + '\n', encoding='utf-8')
    named = sum(line.startswith('PASS ') for line in lines)
    results.append({'test': test.name, 'lua_version': '5.4' if constructor is Lua54Runtime else '5.1',
        'passed': error is None, 'named_cases': named, 'error': error})
    print(test.name + ': ' + ('PASS' if error is None else 'FAIL') + ', cases=' + str(named), flush=True)

after = hashes()
report = {'utc': datetime.now(timezone.utc).isoformat(), 'runtime': 'Lupa Lua 5.1 plus documented Lua 5.4 concurrency interleaving',
    'scope': 'Actual Lua modules against local simulated Nakama storage/auth/network boundaries; no production actions.',
    'passed': all(x['passed'] for x in results), 'named_cases': sum(x['named_cases'] for x in results),
    'results': results, 'source_sha256': before, 'sources_changed_during_run': before != after}
(OUT / 'local-security-summary.json').write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print('TOTAL=' + str(report['named_cases']) + ', passed=' + str(report['passed']) + ', changed=' + str(before != after))
raise SystemExit(0 if report['passed'] and before == after else 1)

