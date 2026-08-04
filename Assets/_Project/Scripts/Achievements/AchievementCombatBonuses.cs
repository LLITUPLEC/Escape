using Project.Match3;
using UnityEngine;
using Project.Character;

namespace Project.Achievements
{
    /// <summary>
    /// Пассивные бонусы из claimed шагов достижений.
    /// Авторитетный расчёт — на сервере (duel_match3_combat_bonuses.lua).
    /// Клиентский метод оставлен для офлайн-превью; в бою и на экране персонажа статы приходят с сервера.
    /// Проценты умножают сумму «уровень + экипировка»; плоские — прибавляются после. Крит — только плоско.
    /// </summary>
    public static class AchievementCombatBonuses
    {
        public static void ApplyToCharacterStats(StatsMap s)
        {
            if (s == null) return;
            ApplyBonusesToStatsMap(s);
        }

        private static void ApplyBonusesToStatsMap(StatsMap s)
        {
            AchievementProgressStorage.EnsureLoaded();

            var flatHp = 0;
            var flatDmg = 0;
            var flatArmor = 0;
            var flatCrit = 0f;
            var hpPct = 0f;
            var dmgPct = 0f;
            var armorPct = 0f;
            var healPct = 0f;

            Accumulate("obs.cross", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("obs.square", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("obs.petard", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("obs.fury", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("obs.shield", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.blacksmith", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.ore_tournament", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.gold_tournament", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.duel", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.cross_finish", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.square_finish", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("obs.firework_lover", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("obs.line5", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("obs.line6", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.bets_placed", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("sl.bets_won", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("dnn.triple_extra", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("dnn.perfect_bets", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("dnn.double_line", 1, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            Accumulate("dnn.win_1hp", 1, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);

            s.hp = ScaleAndAdd(Mathf.Max(1, s.hp), hpPct, flatHp);
            s.damage = ScaleAndAdd(Mathf.Max(0, s.damage), dmgPct, flatDmg);
            s.armor = ScaleAndAdd(Mathf.Max(0, s.armor), armorPct, flatArmor);
            s.healing = ScaleAndAdd(Mathf.Max(0, s.healing), healPct, 0);
            s.crit_chance = Mathf.Clamp01(s.crit_chance + flatCrit);
        }

        private static int ScaleAndAdd(int value, float pct, int flat)
        {
            return Mathf.RoundToInt(value * (1f + pct)) + flat;
        }

        private static void Accumulate(
            string chainId,
            int stepCount,
            ref int flatHp,
            ref int flatDmg,
            ref int flatArmor,
            ref float flatCrit,
            ref float hpPct,
            ref float dmgPct,
            ref float armorPct,
            ref float healPct)
        {
            for (var i = 0; i < stepCount; i++)
            {
                if (!AchievementProgressStorage.IsStepClaimed(chainId, i))
                    continue;
                AddStep(chainId, i, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref hpPct, ref dmgPct, ref armorPct, ref healPct);
            }
        }

        private static void AddStep(
            string chainId,
            int step,
            ref int flatHp,
            ref int flatDmg,
            ref int flatArmor,
            ref float flatCrit,
            ref float hpPct,
            ref float dmgPct,
            ref float armorPct,
            ref float healPct)
        {
            switch (chainId)
            {
                case "obs.cross":
                case "obs.square":
                    flatHp += step switch { 0 => 10, 1 => 20, 2 => 50, _ => 70 };
                    break;
                case "obs.petard":
                    flatDmg += step switch { 0 => 5, 1 => 10, 2 => 15, _ => 20 };
                    break;
                case "obs.fury":
                    flatCrit += 0.005f;
                    break;
                case "obs.shield":
                    flatArmor += step switch { 0 => 5, 1 => 10, 2 => 15, _ => 20 };
                    break;
                case "sl.blacksmith":
                case "sl.ore_tournament":
                case "sl.gold_tournament":
                    if (step == 3) hpPct += 0.05f;
                    break;
                case "sl.duel":
                    if (step == 3) dmgPct += 0.05f;
                    break;
                case "sl.cross_finish":
                case "sl.square_finish":
                    flatDmg += step switch { 0 => 10, 1 => 25, 2 => 100, _ => 250 };
                    break;
                case "obs.firework_lover":
                    if (step == 3) flatCrit += 0.01f;
                    break;
                case "obs.line5":
                    hpPct += step switch { 0 => 0.01f, 1 => 0.02f, 2 => 0.03f, _ => 0.04f };
                    break;
                case "obs.line6":
                    dmgPct += step switch { 0 => 0.01f, 1 => 0.02f, 2 => 0.03f, _ => 0.04f };
                    break;
                case "sl.bets_placed":
                    if (step == 3) armorPct += 0.05f;
                    break;
                case "sl.bets_won":
                    switch (step)
                    {
                        case 0: hpPct += 0.07f; break;
                        case 1: armorPct += 0.07f; break;
                        case 2: healPct += 0.07f; break;
                        default: dmgPct += 0.07f; break;
                    }
                    break;
                case "dnn.triple_extra":
                    switch (step)
                    {
                        case 0: flatHp += 300; break;
                        case 1: flatArmor += 300; break;
                        case 2: armorPct += 0.05f; break;
                        default: hpPct += 0.10f; break;
                    }
                    break;
                case "dnn.perfect_bets":
                    dmgPct += step switch { 0 => 0.02f, 1 => 0.04f, 2 => 0.06f, _ => 0.08f };
                    break;
                case "dnn.double_line":
                    dmgPct += 0.01f;
                    break;
                case "dnn.win_1hp":
                    armorPct += 0.05f;
                    break;
            }
        }
    }
}
