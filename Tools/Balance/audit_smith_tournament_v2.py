"""Reproducible versioned Blacksmith reward budget; does not call game services."""
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[2]

def main():
    path = ROOT / "Server/nakama/modules/arena_smith_rewards.lua"
    source = path.read_text(encoding="utf-8-sig")
    scoped_recipes = "MineRecipes.eligible" in source
    version = "0.3.9" if scoped_recipes else "0.3.8"
    tiers = re.findall(r'(green|blue|purple) = \{ entry = (\d+), champion = (\d+), consolation = (\d+), fallback_ingots = (\d+) \}', source)
    assert len(tiers) == 3
    catalog = json.loads((ROOT / "Server/nakama/data/duel_match3_item_defs.json").read_text(encoding="utf-8-sig"))["items"]
    rows = []
    for color,e,c,l,f in tiers:
        entry,champion,loss,fallback = map(int,(e,c,l,f))
        gold = {"green":300,"blue":600,"purple":1200}[color]
        sale = catalog["ingot_"+color]["sale_price"]
        row = dict(tier=color,entry_ingots=entry,previous_entry_ingots=50,entry_sale_value_gold=entry*sale,
                   champion_gold=gold,champion_ore=gold,champion_ingots=champion,loss_ingots=loss,
                   champion_complete_collection_ingots=champion+fallback,
                   recipe_rule=("Only actual drops from won AND unlocked floors at the matching difficulty; " if scoped_recipes else "") +
                       "one unlearned, unowned T1 recipe of the same rarity; exclude inventory and pending loot",
                   full_collection_fallback_extra_ingots=fallback)
        row["eight_human_ingot_budget"] = dict(paid=8*entry,returned_with_recipe=champion+7*loss,
            returned_with_complete_collection=champion+fallback+7*loss)
        assert row["eight_human_ingot_budget"]["returned_with_complete_collection"] < 8*entry
        row["sensitivity_not_observed_win_rates"] = [dict(championship_probability=p,
            expected_gold=gold*p,expected_ore=gold*p,
            expected_ingot_return_with_recipe=p*champion+(1-p)*loss,
            expected_ingot_return_complete_collection=p*(champion+fallback)+(1-p)*loss,
            expected_championships_for_first_achievement=5,
            expected_entries_for_first_achievement=5/p) for p in (.125,.25,.5,1)]
        rows.append(row)
    report = dict(generated_utc=datetime.now(timezone.utc).isoformat(),version=version,source_sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
        tiers=rows,achievement_total_championship_thresholds=[5,30,130,630],
        security=["Entry cost/version saved per paid participant; legacy missing version means exact old fifty-ingot debit",
            "Separate v1/v2 queues; legacy v1 keeps old rewards; existing craft receipts and pending items are never recalculated", "Cancellation atomically refunds paid cost with pending delivery at full capacity",
            "No participation award before a completed fight; loss compensation is only 20% of the entry",
            "Bots never receive rewards; one durable receipt per human per tournament",
            "Result, sheet, pending loot and currency/progress commit atomically with CAS; retry re-reads ownership",
            "Predictions affect gold/ore only; item rewards are fixed", "No tesseract fallback: canonical sale value is 30000 gold each"],
        limitations=["Expected values assume the stated championship chance; bots and player skill change the actual chance",
            "Ore is not converted to gold in this audit", "Repeat legitimate championships intentionally earn rewards; cancellation/replay cannot mint rewards",
            ("APK 0.3.8 has old fallback/recipe text until updated; authoritative cost remains 10/15/20" if scoped_recipes else
             "APK 0.3.7 still has static old fee text until updated; server charges and refunds authoritative cost")])
    if scoped_recipes:
        shared=ROOT/"Server/nakama/modules/duel_mine_recipe_drops.lua"
        report["shared_actual_pve_drop_table_sha256"]=hashlib.sha256(shared.read_bytes()).hexdigest()
        report["recipe_policy_version"]=3
    out=ROOT/".cache/qa/balance/smith"
    out.mkdir(parents=True,exist_ok=True)
    (out/"smith-balance.json").write_text(json.dumps(report,ensure_ascii=False,indent=2)+"\n",encoding="utf-8")
    lines=["Кузнечный турнир "+version, "", "Цвет | Взнос | Чемпион: слитки + новый рецепт | Если доступного нового рецепта нет | Проигрыш"]
    for r in rows:
        lines.append(f"{r['tier']} | {r['entry_ingots']} | {r['champion_ingots']} + 1 рецепт | {r['champion_complete_collection_ingots']} слитков | {r['loss_ingots']}")
    lines += ["", "Золото и руда чемпиону сохранены: 300/600/1200 каждого ресурса. Прогнозы ±20% влияют только на них.",
        "Рецепт той же редкости выбирается среди ещё не изученных и не полученных (включая ожидающие место). Если подходящего нет: дополнительные " +
        "/".join(str(r['full_collection_fallback_extra_ingots']) for r in rows) + " слитков, без тессерактов.",
        "Компенсация 20% взноса начисляется один раз после завершённого боя. До старта — возврат точной оплаченной цены, без награды.",
        "Все восемь участников вместе получают меньше слитков, чем заплатили: даже при полной коллекции остаётся расход ресурса.",
        "При модели шансов 1/8 средний возврат слитков с рецептом — 42,5% взноса; модель не измеряет фактическую частоту побед против ботов.",
        "Старые турниры/очереди по 50 сохраняют старую цену и награды. Новые v2 очереди отделены. Полный инвентарь сохраняет награду в durable pending.",
        "Уровни достижения по-прежнему требуют суммарно 5/30/130/630 чемпионств. Первый порог при модели 1/8 — 40 входов: 400/600/800 слитков до компенсаций вместо прежних 2000."]
    (out/"smith-balance.txt").write_text("\n".join(lines)+"\n",encoding="utf-8")
    if scoped_recipes:
        with (out/"smith-balance.txt").open("a",encoding="utf-8") as f:
            f.write("Рецепты ограничены фактическими дропами побеждённых монстров открытых этажей на соответствующей сложности; простой доступ к этажу не достаточен. Этажи 4/8/10/12 рецептов не дают. Если единственный допустимый рецепт уже получен, неизвестный рецепт следующего непобеждённого этажа не подставляется.\n")
    print(json.dumps({"tiers":len(rows),"all_brackets_ingot_sink":True,"output":str(out)},ensure_ascii=False))

if __name__ == "__main__":
    main()
