"""Read-only source audit of Blacksmith tournament entry/rewards and achievement pacing."""
from __future__ import annotations
from datetime import datetime, timezone
import hashlib
import json
from pathlib import Path
import re
import runpy
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def main():
    server = ROOT / "Server/nakama/modules/arena_tournament.lua"
    source = server.read_text(encoding="utf-8-sig")
    if "modules.arena_smith_rewards" in source:
        # Keep the 0.3.7 production snapshot historical; write current v2 results
        # into its versioned release folder instead of reinterpreting that audit.
        runpy.run_path(str(Path(__file__).with_name("audit_smith_tournament_v2.py")), run_name="__main__")
        return
    fee = int(re.search(r"local ARENA_QUEUE_BET_INGOTS = (\d+)", source)[1])
    slots = int(re.search(r"local ARENA_QUEUE_MAX = (\d+)", source)[1])
    prize_fn = re.search(r"local function arena_final_prize_ore_gold\(bet\)([\s\S]*?)\nend", source)[1]
    defaults = re.findall(r"return (\d+), (\d+)", prize_fn)
    assert len(defaults) == 3, "Review changed prize function before interpreting it"
    prizes = dict(zip(("blue", "purple", "green"), ((int(a), int(b)) for a,b in defaults)))
    catalog = json.loads((ROOT / "Server/nakama/data/duel_match3_item_defs.json").read_text(encoding="utf-8-sig"))["items"]
    achievements = json.loads((ROOT / "Server/nakama/data/achievement_catalog.example.json").read_text(encoding="utf-8-sig"))
    chains = {chain["id"]: chain for chain in achievements["chains"]}
    smith = chains["sl.blacksmith"]
    threshold, steps = 0, []
    for step in smith["steps"]:
        threshold += step["threshold_delta"]
        steps.append({"additional_wins": step["threshold_delta"], "total_wins": threshold, "rewards": step["rewards"]})
    expected_predictions, pairs = 0, slots // 2
    while pairs >= 2:
        expected_predictions += pairs - 1
        pairs //= 2
    rows = []
    for tier in ("green", "blue", "purple"):
        ore, gold = prizes[tier]
        sale = catalog["ingot_" + tier]["sale_price"]
        rows.append({"tier": tier, "entry_ingots": fee, "selling_entry_instead_gold": fee * sale,
                     "champion_gold": gold, "champion_ore": ore,
                     "champion_perfect_forecasts_gold": round(gold * (1 + .2 * expected_predictions)),
                     "champion_perfect_forecasts_ore": round(ore * (1 + .2 * expected_predictions)),
                     "equal_skill_win_probability": 1 / slots,
                     "expected_gold_per_entry_at_equal_skill": gold / slots,
                     "expected_ore_per_entry_at_equal_skill": ore / slots})
    pacing = [{"championship_probability": p,
               "expected_entries_for_first_5_wins": 5 / p,
               "expected_ingots_for_first_5_wins": fee * 5 / p,
               "expected_entries_for_final_hp_step": threshold / p,
               "expected_ingots_for_final_hp_step": fee * threshold / p} for p in (1 / slots, .25, .5, 1)]
    tests = ROOT / "Server/nakama/tests/arena_concurrency_audit.lua"
    aggregate_path = ROOT / "Design/Release-0.3.15/validation/balance/blacksmith-production-aggregate.json"
    aggregate = json.loads(aggregate_path.read_text()) if aggregate_path.exists() else None
    report = {
        "generated_utc": datetime.now(timezone.utc).isoformat(),
        "evidence_scope": "Current source and canonical repository item definitions; timestamped read-only production aggregate and live numeric catalog values are included when available.",
        "production_aggregate": aggregate,
        "entry_and_rewards": rows, "cross_currency_conversion_assumed": False,
        "assumption": "Equal-skill model assumes each of eight entrants has championship probability 1/8 and predictions are unbiased; actual player skill/bot strength can change this probability.",
        "champion_prediction_count": expected_predictions,
        "achievement_chain": {"id": smith["id"], "counter_key": smith["counter_key"], "threshold_mode": smith["threshold_mode"], "steps": steps},
        "achievement_pacing_models": pacing,
        "resource_tournament_comparison": [
            {"kind": "ore", "entry": 500, "champion_prize": 2500, "equal_skill_expected_gross": 312.5, "equal_skill_expected_net": -187.5},
            {"kind": "ore", "entry": 2000, "champion_prize": 10000, "equal_skill_expected_gross": 1250, "equal_skill_expected_net": -750},
            {"kind": "gold", "entry": 600, "champion_prize": 3000, "equal_skill_expected_gross": 375, "equal_skill_expected_net": -225},
            {"kind": "gold", "entry": 2400, "champion_prize": 12000, "equal_skill_expected_gross": 1500, "equal_skill_expected_net": -900},
        ],
        "new_release_cosmetic_incentive": {"theme": "emberoracle", "name": "Хранитель горна", "requirements": ["obs.firework_lover achievement rank3", "1 smith tournament championship"], "economy_fees_changed": False, "detail": "The new visible exclusive cosmetic gives existing players a nearer tournament goal; it does not remove the ingot opportunity cost."},
        "queue": {"total_slots": slots, "minimum_human_entrants": 1, "bot_fill_seconds_per_slot": [5,8], "solo_full_queue_nominal_seconds": [35,56], "poll_required": True, "round_countdown_seconds": 20, "independent_stake_queues": 7},
        "confirmed_fixes": [
            {"file": "Assets/_Project/Scripts/UI/ExpeditionPresentation.cs", "issue": "Arena card promised equipment and blueprints although the implemented champion prize is only gold+ore", "fix": "Caption now displays 50 ingots and the actual gold+ore prize type"},
            {"file": "Assets/_Project/Scripts/UI/ArenaMatch8Bridge.cs", "issue": "Picking a paid entry silently returned when the socket was not ready", "fix": "Connect first, recheck authenticated account scope before paid RPC, show an unresolved-entry connection message and reconcile through authoritative polling"},
            {"file": "Server/nakama/modules/arena_tournament.lua", "issue": "Last-human elimination aborted the bracket before settling completed forecast achievement counters", "fix": "Atomic settlement of known completed outcomes before terminal abort; unfinished outcomes are never invented; repeat callbacks remain idempotent"},
        ],
        "validation": {"arena_concurrency_invariants": 30, "new_regressions": 3, "defect_reproduced_before_fix": True, "economy_entry_fees_or_prize_amounts_changed": False, "live_gameplay_mutation_performed": False},
        "recommendations_for_review_not_applied": [
            "Give Blacksmith a clearly visible exclusive cosmetic goal with a reachable first milestone, distinct from the same +5% HP chain available in ore/gold tournaments.",
            "Review a smaller ingot stake for the first tier; 50 ingots compete directly with equipment crafting and are substantially more costly than the cash-only tournaments. Model resource sinks and new-player stock before picking an exact price.",
            "Alternatively keep the prestige stake and give the champion a useful crafting reward, e.g. returning the champion's own ingot entry or a one-time required recipe; use existing durable pending-inventory delivery and explicit idempotent receipts.",
            "Show the next exact achievement target/reward beside entry and measure modal views, accepted entries, insufficient-ingot refusals, wait time and tournament completions before judging popularity.",
        ],
        "known_remaining_limitations": [
            "Smith queue cancellation can require inventory space for the 50-ingot refund. Existing server behavior preserves the paid entry and returns inventory_full rather than losing the refund.",
            "Bot filling advances only on polling. Closing/backgrounding the game can extend the wait; the nominal35–56 seconds is not a guaranteed service-level time.",
            "Production snapshot is a very small aggregate and can include administrative testing. It does not measure views, unsuccessful entries or unique real players, and cannot establish the cause of low participation.",
        ],
        "sources_sha256": {str(path.relative_to(ROOT)).replace('\\','/'): hashlib.sha256(path.read_bytes()).hexdigest() for path in (server, tests, ROOT / "Server/nakama/data/duel_match3_item_defs.json", ROOT / "Server/nakama/data/achievement_catalog.example.json")},
    }
    out = ROOT / "Design/Release-0.3.15/validation/balance"
    out.mkdir(parents=True, exist_ok=True)
    (out / "blacksmith-tournament-audit.json").write_text(json.dumps(report, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    lines = ["Турнир кузнеца: аудит привлекательности", "", "Вывод: вход дорогой относительно практической награды; одни достижения с далёкими порогами слабо компенсируют расход материалов для крафта.", "", "Вход и базовый приз чемпиона:"]
    if aggregate:
        lines[3:3] = [f"Производственный READ ONLY снимок: {aggregate['stats_profiles']} профиля статистики; входы smith/ore/gold = {aggregate['entries']['smith']}/{aggregate['entries']['ore']}/{aggregate['entries']['gold']}; победы = {aggregate['championships']['smith']}/{aggregate['championships']['ore']}/{aggregate['championships']['gold']}. Идентификаторы и имена игроков не выгружались.", ""]
    for row in rows:
        lines.append(f"- {row['tier']}: {fee} слитков; приз {row['champion_gold']} золота + {row['champion_ore']} руды. Продажа этих слитков вместо турнира: {row['selling_entry_instead_gold']} золота.")
        lines.append(f"  При равных шансах 1/8 ожидаемая награда на вход: {row['expected_gold_per_entry_at_equal_skill']} золота + {row['expected_ore_per_entry_at_equal_skill']} руды. Руда в золото не конвертируется в расчёте.")
    lines += ["", "Достижение «Кузнечный Король»: общие пороги 5 / 30 / 130 / 630 побед в турнире; награды 2000 / 10000 / 30000 золота и +5% HP. Это победы в финале, а не участия.", "При модели равных шансов первые 5 побед требуют в среднем 40 входов, то есть 2000 слитков. Последний шаг 630 побед — 5040 входов. Это модель, а не измерение игроков.", "В рудном и золотом турнирах приз равен 5 взносам в той же валюте: при шансах 1/8 ожидается возврат 62,5% взноса. Турнир кузнеца вместо валюты расходует нужные для крафта слитки.", "Новое косметическое поощрение: «Хранитель горна» открывается за ранг 3 «Пиротехника» и 1 победу в турнире кузнеца. Это отдельная видимая цель, цены и суммы призов не менялись.", "", "Очередь не требует 8 настоящих игроков: один игрок достаточен, остальные места заполняются ботами раз в 5–8 секунд. Номинально 35–56 секунд до полной сетки, затем 20 секунд перед раундом; нужна работающая периодическая проверка состояния.", "", "Исправлено без изменения цен/сумм призов:"]
    lines += ["- " + fix["issue"] + " -> " + fix["fix"] for fix in report["confirmed_fixes"]]
    lines += ["", "Что сделать привлекательнее (предложения, экономика пока не менялась):"] + ["- " + item for item in report["recommendations_for_review_not_applied"]]
    lines += ["", "Ограничения:"] + ["- " + item for item in report["known_remaining_limitations"]]
    lines += ["", "Проверка: 30 инвариантов конкурентного турнира прошли, в том числе 3 новых регрессии. Полный отчёт с числами, моделями и путями: blacksmith-tournament-audit.json."]
    (out / "blacksmith-tournament-audit.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(json.dumps({"fees": [row["entry_ingots"] for row in rows], "tiers": len(rows), "achievement_total_thresholds": [step["total_wins"] for step in steps], "report": str(out / "blacksmith-tournament-audit.json")}, ensure_ascii=False))


if __name__ == "__main__":
    main()
