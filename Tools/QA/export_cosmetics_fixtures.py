"""Export actual server cosmetics replies through the local security fixture (no network)."""
import json
import os
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[2]
os.chdir(ROOT)
sys.path.insert(0, str(ROOT / ".cache/lua-test-runtime"))
from lupa.lua54 import LuaRuntime, lua_type


def convert(value):
    if lua_type(value) != "table":
        return value
    keys = list(value.keys())
    if keys and all(isinstance(key, int) for key in keys) and sorted(keys) == list(range(1, len(keys) + 1)):
        return [convert(value[index]) for index in sorted(keys)]
    return {str(key): convert(value[key]) for key in keys}


runtime = LuaRuntime(unpack_returned_tuples=True)
runtime.globals().print = lambda *args: None
runtime.execute((ROOT / "Server/nakama/tests/cosmetics_security_test.lua").read_text(encoding="utf-8-sig"))
source = runtime.globals()
manifest = json.loads((ROOT / "Server/nakama/data/migrations/cosmetics-rewards-0.3.18.json").read_text(encoding="utf-8"))
catalog = convert(runtime.eval("require('modules.duel_cosmetics_rewards').catalog"))
assert manifest["rewards"] == {key: value for key, value in catalog.items() if key != "dawnwarden"}, "Migration manifest disagrees with the canonical reward catalog"
fixture = {name: convert(source[key]) for name, key in {
    "male": "COSMETIC_UI_FIXTURE_MALE", "female": "COSMETIC_UI_FIXTURE_FEMALE",
    "male_selected": "COSMETIC_UI_SELECTED_MALE", "female_selected": "COSMETIC_UI_SELECTED_FEMALE",
    "male_all": "COSMETIC_UI_ALL_MALE", "female_all": "COSMETIC_UI_ALL_FEMALE",
}.items()}
destination = ROOT / "Assets/_Project/Editor/Fixtures/appearance-server-0318.json"
destination.write_text(json.dumps(fixture, ensure_ascii=False, indent=2, sort_keys=True) + "\n", encoding="utf-8")
print("Actual cosmetics schema-2 fixture exported: " + str(destination))
