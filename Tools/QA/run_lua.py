import argparse
import os
from pathlib import Path
import sys
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / '.cache/lua-test-runtime'))
os.chdir(ROOT)
from lupa.lua51 import LuaRuntime
from lupa.lua54 import LuaRuntime as LuaRuntime54

p = argparse.ArgumentParser()
p.add_argument('files', nargs='*')
p.add_argument('--parse', action='store_true')
a = p.parse_args()
if a.parse:
    lua = LuaRuntime(unpack_returned_tuples=True)
    check = lua.eval('function(p) local f,e=loadfile(p); assert(f,e); return true end')
    sources = sorted(Path('Server/nakama/modules').glob('*.lua'))
    for file in sources:
        check(file.as_posix())
    print('Lua51 syntax: ' + str(len(sources)) + ' modules passed')
for filename in a.files:
    source = Path(filename).read_text(encoding='utf-8-sig')
    runtime = LuaRuntime54 if 'with Lua 5.4' in source[:250] or Path(filename).name in ('arena_concurrency_audit.lua', 'cosmetics_security_test.lua') else LuaRuntime
    lua = runtime(unpack_returned_tuples=True)
    lua.execute(source, name='@' + filename)
    print('PASS ' + filename, flush=True)
