"""Read-only audit of all shipping recipe costs and their Unity mirrors. No network."""
import argparse
import json
import math
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output', type=Path, default=ROOT / '.cache/qa/equipment-20261028/recipe-audit.json')
args = parser.parse_args()
data = ROOT / 'Server/nakama/data'
items = json.loads((data / 'duel_match3_item_defs.json').read_text(encoding='utf-8-sig'))['items']
policy = json.loads((data / 'equipment_ingot_policy.json').read_text())
qualities = ['normal', 'rare', 'epic', 'legendary']
colors = ['green', 'blue', 'purple', 'gold']
slot_order = ['Helmet', 'Shoulders', 'Chest', 'Gloves', 'Legs', 'Feet', 'WeaponLeft', 'WeaponRight']
rows = []
mirrors = {'craftOre': 'craft_ore', 'craftGold': 'craft_gold', 'craftIngotDef': 'craft_ingot_def',
           'craftIngotN': 'craft_ingot_n', 'craftTesseractN': 'craft_tesseract_n',
           'craftItemId': 'craft_item_id', 'craftRecipeId': 'craft_recipe_id', 'craftMinutes': 'craft_minutes'}
for slot, budget in policy['slots'].items():
    previous = None
    for qi, quality in enumerate(qualities):
        matches = [(key, item) for key, item in items.items() if item.get('kind') == 'equipment' and
                   item.get('tier') == 1 and item.get('slot') == slot and item.get('quality') == quality]
        assert len(matches) == 1, (slot, quality, 'equipment count')
        key, item = matches[0]
        recipe_id = f'recipe_drop_t1_{colors[qi]}_{slot}'
        recipe = items[recipe_id]
        assert item['craft_recipe_id'] == recipe_id and recipe['kind'] == 'recipe'
        assert recipe['quality'] == quality and recipe['tier'] == 1 and recipe['recipe_target_slot'] == slot
        assert item.get('craft_item_id', '') == (previous[0] if previous else ''), (key, 'prerequisite')
        assert item['craft_minutes'] == [60, 120, 180, 300][qi]
        assert item['craft_ingot_n'] == budget.get(quality, 0), (key, 'policy')
        assert item.get('craft_ingot_def', '') == (f'ingot_{colors[qi]}' if qi < 3 else '')
        assert item['craft_tesseract_n'] == (1 if qi == 3 else 0)
        if previous:
            assert item['craft_ore'] > previous[1]['craft_ore'] and item['craft_gold'] > previous[1]['craft_gold']
        if quality in ('rare', 'epic'):
            assert item['craft_ingot_n'] > previous[1]['craft_ingot_n']
            assert item['craft_ingot_n'] / policy['floor10_rewards'][quality] > previous[1]['craft_ingot_n'] / policy['floor10_rewards'][qualities[qi - 1]]
        if quality == 'epic':
            expected = math.ceil(budget['rare'] * (28 / 20) * policy['epic_effort_multiplier'] / 10 - 1e-9) * 10
            assert budget['epic'] == expected, (key, 'reward-adjusted effort')
        text = (ROOT / f'Assets/_Project/Data/Character/Items/{key}.asset').read_text(encoding='utf-8-sig')
        for unity, server in mirrors.items():
            value = re.search(r'^  ' + unity + r': *(.*)$', text, re.M).group(1).strip().strip('"')
            assert value == str(item.get(server, '')), (key, unity, 'client/server mismatch')
        assert int(re.search(r'^  slot: (\d+)$', text, re.M).group(1)) == slot_order.index(slot)
        assert int(re.search(r'^  quality: (\d+)$', text, re.M).group(1)) == qi
        rows.append({'id': key, 'slot': slot, 'quality': quality, 'recipe': recipe_id,
                     'ingots': item['craft_ingot_n'], 'floor10_wins_equivalent': round(item['craft_ingot_n'] / policy['floor10_rewards'][quality], 3) if qi < 3 else None,
                     'ore': item['craft_ore'], 'gold': item['craft_gold'], 'tesseracts': item['craft_tesseract_n'],
                     'prerequisite': item.get('craft_item_id', ''), 'minutes': item['craft_minutes']})
        previous = key, item
assert len(rows) == 32
for quality, diff, multiplier in [('normal', 'easy', 1), ('rare', 'medium', 1.5), ('epic', 'hard', 2)]:
    bots = json.loads((data / f'duel_match3_bots_catalog_{diff}.json').read_text(encoding='utf-8-sig'))['bots']
    floor10 = [bot for bot in bots.values() if bot['floor'] == 10]
    assert len(floor10) == 1
    actual_reward = math.floor(floor10[0]['reward_ingots'] * multiplier + .5)
    assert actual_reward == policy['floor10_rewards'][quality], (quality, 'floor10 reward drift')
report = {'passed': True, 'network_actions': False, 'account_writes': False, 'recipes': len(rows),
          'fields_mirrored_per_recipe': len(mirrors), 'floor10_rewards': policy['floor10_rewards'], 'rows': rows}
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(f'PASS all32 recipes: slot, quality, prerequisite chain, resource kinds, time,8 mirrored cost fields, monotonic counts/effort, floor10 rewards. {args.output}')
