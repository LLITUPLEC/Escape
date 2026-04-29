using Project.Match3;
using UnityEngine;
using Project.Character;

namespace Project.Achievements
{
    /// <summary>
    /// Пассивные бонусы из полученных (claimed) шагов достижений для боевых статов матча.
    /// Проценты «от экипировки» здесь применяются как доля от уже синхронизированных с сервера базовых статов
    /// (аппроксимация без разбора вклада только предметов).
    /// </summary>
    public static class AchievementCombatBonuses
    {
        public static void ApplyToCharacterStats(StatsMap s)
        {
            if (s == null) return;
            AchievementProgressStorage.EnsureLoaded();

            var flatHp = 0;
            var flatDmg = 0;
            var flatArmor = 0;
            var flatCrit = 0f;
            var equipHpPct = 0f;
            var equipDmgPct = 0f;
            var equipArmorPct = 0f;

            Accumulate("obs.cross", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.square", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.petard", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.fury", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.shield", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("sl.blacksmith", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("sl.duel", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("sl.petard_finish", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("dnn.double_line", 1, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("dnn.win_1hp", 1, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);

            var baseHp = Mathf.Max(1, s.hp);
            var baseDmg = Mathf.Max(0, s.damage);
            var baseArmor = Mathf.Max(0, s.armor);

            s.hp = baseHp + flatHp + Mathf.RoundToInt(baseHp * equipHpPct);
            s.damage = baseDmg + flatDmg + Mathf.RoundToInt(baseDmg * equipDmgPct);
            s.armor = baseArmor + flatArmor + Mathf.RoundToInt(baseArmor * equipArmorPct);
            s.crit_chance = Mathf.Clamp01(s.crit_chance + flatCrit);
        }

        public static void ApplyToMyStats(PlayerStats s)
        {
            if (s == null) return;
            AchievementProgressStorage.EnsureLoaded();

            var flatHp = 0;
            var flatDmg = 0;
            var flatArmor = 0;
            var flatCrit = 0f;
            var equipHpPct = 0f;
            var equipDmgPct = 0f;
            var equipArmorPct = 0f;

            Accumulate("obs.cross", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.square", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.petard", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.fury", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("obs.shield", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("sl.blacksmith", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("sl.duel", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("sl.petard_finish", 4, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("dnn.double_line", 1, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            Accumulate("dnn.win_1hp", 1, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);

            var srvDmg = Mathf.Max(0, s.baseDamage);
            var srvArmor = Mathf.Max(0, s.baseArmor);
            var srvMaxHp = s.maxHp > 0 ? s.maxHp : 150;

            s.baseDamage = srvDmg + flatDmg + Mathf.RoundToInt(srvDmg * equipDmgPct);
            s.baseArmor = srvArmor + flatArmor + Mathf.RoundToInt(srvArmor * equipArmorPct);
            s.baseCrit = Mathf.Clamp01(s.baseCrit + flatCrit);

            var hpFlatBonus = flatHp + Mathf.RoundToInt(srvMaxHp * equipHpPct);
            var newMax = Mathf.Max(1, srvMaxHp + hpFlatBonus);
            var deltaMax = newMax - srvMaxHp;
            s.maxHp = newMax;
            s.hp = Mathf.Min(newMax, Mathf.Max(0, s.hp + Mathf.Max(0, deltaMax)));
        }

        private static void Accumulate(
            string chainId,
            int stepCount,
            ref int flatHp,
            ref int flatDmg,
            ref int flatArmor,
            ref float flatCrit,
            ref float equipHpPct,
            ref float equipDmgPct,
            ref float equipArmorPct)
        {
            for (var i = 0; i < stepCount; i++)
            {
                if (!AchievementProgressStorage.IsStepClaimed(chainId, i))
                    continue;
                AddStep(chainId, i, ref flatHp, ref flatDmg, ref flatArmor, ref flatCrit, ref equipHpPct, ref equipDmgPct, ref equipArmorPct);
            }
        }

        private static void AddStep(
            string chainId,
            int step,
            ref int flatHp,
            ref int flatDmg,
            ref int flatArmor,
            ref float flatCrit,
            ref float equipHpPct,
            ref float equipDmgPct,
            ref float equipArmorPct)
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
                    if (step == 3) equipHpPct += 0.05f;
                    break;
                case "sl.duel":
                    if (step == 3) equipDmgPct += 0.05f;
                    break;
                case "sl.petard_finish":
                    if (step == 3) flatCrit += 0.01f;
                    break;
                case "dnn.double_line":
                    equipDmgPct += 0.01f;
                    break;
                case "dnn.win_1hp":
                    equipArmorPct += 0.05f;
                    break;
            }
        }
    }
}
