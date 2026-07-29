using UnityEngine;

namespace Project.Character
{
    /// <summary>
    /// Пороги XP по уровням — зеркало <c>CFG.LEVEL_XP</c> / <c>PVE_MAX_LEVEL</c> на сервере.
    /// </summary>
    public static class PlayerLevelXpTable
    {
        /// <summary>Thresholds[i] — суммарный XP, нужный чтобы достичь уровня i+1 (уровень 1 = 0 XP).</summary>
        public static readonly int[] Thresholds =
        {
            0, 100, 320, 804, 1869, 4212, 9367, 20708, 45658, 100548, 221306, 486974,
        };

        public const int MaxLevel = 12;

        public static int LevelFromXp(int xp)
        {
            xp = Mathf.Max(0, xp);
            var level = 1;
            for (var i = 1; i < Thresholds.Length; i++)
            {
                if (xp >= Thresholds[i])
                    level = i + 1;
                else
                    break;
            }

            return Mathf.Min(level, MaxLevel);
        }

        /// <summary>
        /// fill01 — доля заполнения текущего уровня; xpRemaining — XP до следующего (0 на макс. уровне).
        /// </summary>
        public static void GetBarState(int xp, out int level, out float fill01, out int xpRemaining)
        {
            xp = Mathf.Max(0, xp);
            level = LevelFromXp(xp);

            if (level >= MaxLevel || level >= Thresholds.Length)
            {
                fill01 = 1f;
                xpRemaining = 0;
                return;
            }

            var cur = Thresholds[level - 1];
            var next = Thresholds[level];
            var span = Mathf.Max(1, next - cur);
            fill01 = Mathf.Clamp01((xp - cur) / (float)span);
            xpRemaining = Mathf.Max(0, next - xp);
        }
    }
}
