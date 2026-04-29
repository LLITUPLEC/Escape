using UnityEngine;

namespace Project.Achievements
{
    public static class AchievementUiRules
    {
        public static int CumulativeRequired(AchievementChainDefinition chain, int stepIndex)
        {
            if (chain == null || chain.Thresholds == null || stepIndex < 0)
                return 0;
            var max = Mathf.Min(stepIndex, chain.Thresholds.Length - 1);
            var sum = 0;
            for (var i = 0; i <= max; i++)
                sum += Mathf.Max(0, chain.Thresholds[i]);
            return sum;
        }

        /// <summary>
        /// Шаг с индексом i недоступен для отображения прогресса, пока не выполнен порог предыдущего шага.
        /// </summary>
        public static bool IsSlotLocked(AchievementChainDefinition chain, int stepIndex, int statTotal)
        {
            if (chain == null || stepIndex <= 0)
                return false;
            if (chain.Thresholds == null || stepIndex - 1 >= chain.Thresholds.Length)
                return true;
            return statTotal < CumulativeRequired(chain, stepIndex - 1);
        }

        public static float SliderRatio(AchievementChainDefinition chain, int stepIndex, int statTotal)
        {
            if (chain == null || stepIndex < 0 || stepIndex >= chain.Thresholds.Length)
                return 0f;
            var prev = stepIndex > 0 ? CumulativeRequired(chain, stepIndex - 1) : 0;
            var t = Mathf.Max(1, chain.Thresholds[stepIndex]);
            var numerator = Mathf.Clamp(statTotal - prev, 0, t);
            return numerator / (float)t;
        }
    }
}
