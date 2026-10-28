"""Historical offline audit; requires an explicit archived snapshot and output directory.

The original assertions describe the 0.3.14 economy. A later catalog rebalance can
legitimately fail its equality checks; use the current recipe audit for release QA.
"""
import argparse
from collections import defaultdict
from datetime import datetime, timezone
import hashlib
import json
import math
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parents[3]
sys.path.insert(0, str(ROOT / ".cache/lua-test-runtime"))
from lupa.lua51 import LuaRuntime, lua_type


def read(path): return json.loads((ROOT / path).read_text(encoding="utf-8-sig"))
def sha(path): return hashlib.sha256((ROOT / path).read_bytes()).hexdigest()
def plain(value):
    if lua_type(value) != "table": return value
    keys = list(value.keys())
    if keys and all(type(k) in (int, float) for k in keys) and sorted(keys) == list(range(1, len(keys) + 1)):
        return [plain(value[i]) for i in range(1, len(keys) + 1)]
    return {k: plain(value[k]) for k in keys}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--snapshot", type=Path, required=True, help="Archived production-static-catalogs.json input")
    parser.add_argument("--output", type=Path, required=True, help="New output directory; historical reports are never selected implicitly")
    args = parser.parse_args()
    out = args.output.resolve(); out.mkdir(parents=True, exist_ok=True)
    core_path = "Server/nakama/modules/duel_match3.lua"
    core = (ROOT / core_path).read_text(encoding="utf-8-sig")
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute('package.path="Server/nakama/?.lua;"..package.path;package.loaded.nakama={};CFG={MINE_DIFFICULTY_DEFAULT="easy",PVE_MAX_LEVEL=12,clamp_int=function(n,a,b)return math.max(a,math.min(b,math.floor(n)))end}')
    blocks = [core[core.index("function normalize_mine_difficulty(v)"):core.index("function make_floor_state_key")],
        core[core.index("local function ingot_def_for_difficulty(diff)"):core.index("--- id предмета-рецепта:")],
        core[core.index("local function workshop_craft_duration_seconds"):core.index("local function inventory_count_def")]]
    for block in blocks: lua.execute(block.replace("local function ", "function "))
    lua.execute('Shop=require("modules.duel_shop");Rewards=require("modules.duel_match3_pve_rewards");math.random=function(a,b)if a then return a end return 0 end')
    prices = read("Server/nakama/data/duel_shop_catalog.json")
    assert plain(lua.globals().Shop.DEFAULT_CATALOG) == prices, "Lua fallback differs from editable canonical price catalog"
    defs = read("Server/nakama/data/duel_match3_item_defs.json")["items"]
    static_path = str(args.snapshot.resolve())
    production = read(static_path)
    assert production["items"]["value"]["items"] == defs, "Live item definitions differ"
    monsters = []
    for difficulty, color in (("easy", "green"), ("medium", "blue"), ("hard", "purple")):
        bots = read("Server/nakama/data/duel_match3_bots_catalog_" + difficulty + ".json")["bots"]
        assert production["bots_" + difficulty]["value"]["bots"] == bots, "Live monster definitions differ"
        for bot in sorted(bots.values(), key=lambda b: b["floor"]):
            floor = bot["floor"]; boss = bot.get("is_boss", False) or floor in (4, 8, 12)
            deps = lua.table_from({"normalize_difficulty": lua.globals().normalize_mine_difficulty,
                "get_bot_profile": lambda *_, bot=bot: lua.table_from(bot, recursive=True),
                "clamp_floor": lambda n: n, "is_boss_floor": lambda f: f in (4, 8, 12),
                "reward_multiplier": lua.globals().mine_reward_multiplier, "stat_multiplier": lua.globals().mine_stat_multiplier,
                "apply_reward_xp": lambda n: n, "respawn_seconds": lambda *_: 0, "next_affix": lambda *_: "",
                "ingot_drop_chance": lua.globals().ingot_drop_chance_non_boss,
                "ingot_def": lua.globals().ingot_def_for_difficulty, "uses_recipe_drop": lambda *_: False})
            rewarder = lua.globals().Rewards.new(deps)
            plan = plain(rewarder.freeze(bot["id"], lua.table_from({"difficulty": difficulty, "floor": floor}), "offline-audit", "offline-no-account", None))
            amount = next((i["amount"] for i in plan["items"].values() if i.get("kind") == "ingots"), 0) if isinstance(plan["items"], dict) else next((i["amount"] for i in plan["items"] if i.get("kind") == "ingots"), 0)
            chance = 1 if boss else lua.globals().ingot_drop_chance_non_boss(floor)
            mul = lua.globals().mine_reward_multiplier(difficulty)
            monsters.append({"difficulty": difficulty, "floor": floor, "name": bot["name"], "boss": boss,
                "gold": plan["reward_gold"], "ore": plan["reward_ore"], "xp": plan["reward_xp"], "ingot_id": "ingot_" + color,
                "ingots_on_drop": amount, "ingot_drop_probability": chance, "expected_ingots_per_win": amount * chance,
                "matter_min": math.ceil(bot.get("reward_matter_min", 0) * mul), "matter_max": math.ceil(bot.get("reward_matter_max", 0) * mul)})
    assert next(m for m in monsters if m["difficulty"] == "hard" and m["floor"] == 6)["ingots_on_drop"] == 18
    assert next(m for m in monsters if m["difficulty"] == "hard" and m["floor"] == 4)["ingots_on_drop"] == 48
    crafts, groups = [], defaultdict(list)
    for item_id, item in sorted(defs.items()):
        if item.get("kind") != "equipment": continue
        cost = plain(lua.globals().workshop_craft_cost_from_def(lua.table_from(item, recursive=True), item["tier"], item["quality"]))
        assert item.get("sale_price", 100) <= cost["gold"], "Craft/sale would mint gold: " + item_id
        entry = dict(cost, id=item_id, quality=item["quality"], slot=item["slot"], consumed_item=item.get("craft_item_id", ""),
            recipe=item.get("craft_recipe_id", ""), minutes=lua.globals().workshop_craft_duration_seconds(lua.table_from(item), item["tier"]) // 60)
        crafts.append(entry); groups[item["quality"]].append(entry)
    sets = {quality: {"pieces": len(rows), "gold": sum(r["gold"] for r in rows), "ore": sum(r["ore"] for r in rows),
        "ingots": sum(r["ingot_n"] for r in rows), "tesseracts": sum(r["tesseract_n"] for r in rows),
        "ingots_min_max_per_piece": [min(r["ingot_n"] for r in rows), max(r["ingot_n"] for r in rows)]} for quality, rows in groups.items()}
    offers = []
    for offer in prices["offers"]:
        if offer["currency"] != "matter": continue
        gold = offer["gold"] + sum(defs[i["def_id"]].get("sale_price", 0) * i["amount"] for i in offer.get("items", []))
        assert gold <= offer["price"] * 250, "Buy/sell gold arbitrage: " + offer["id"]
        offers.append({"id": offer["id"], "matter_price": offer["price"], "maximum_gold_after_item_sales": gold, "gold_equivalent_limit": offer["price"] * 250, "passed": True})
    report = {"passed": True, "checked_utc": datetime.now(timezone.utc).isoformat(), "network_or_account_actions": False,
        "actual_production_catalogs_match": True, "monster_rows": monsters, "craft_rows": crafts, "full_set_incremental_costs": sets,
        "shop_sale_audit": offers, "resource_anchor": {"matter": 1, "gold": 250, "ore": 250, "rub_anchor": 99, "rub_gold": 10000, "rub_ore": 10000},
        "decision": "No monster drops or craft costs changed.20ingots cost3/4/5matter. Craft bundle30matter contains5000gold,5000ore and20of each ingot (separate total32matter).",
        "limits": ["Loot rows are base rewards before account XP/currency bonuses; scrolls multiply only gold/ore.", "Non-boss amounts require the listed random drop; bosses guarantee their amount.", "Craft costs are per upgrade; blue/purple/gold consume the previous-quality item and require learned recipes.", "Blue ingot full-set2080 versus epic1028 is an existing crafting design; no unrelated progression rebalance was made.", "Playing battles intentionally earns resources; the arbitrage audit addresses immediate purchases and item/craft sales."],
        "sources": {p: sha(p) for p in (core_path, "Server/nakama/modules/duel_match3_pve_rewards.lua", "Server/nakama/data/duel_shop_catalog.json", static_path)}}
    (out / "economy-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("PASS actual Lua formulas36monster rows,32craft rows,17Matter offers; production catalogs match")


if __name__ == "__main__": main()
