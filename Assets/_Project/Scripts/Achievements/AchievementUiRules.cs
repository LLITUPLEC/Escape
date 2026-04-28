using UnityEngine;

namespace Project.Achievements
{
    public static class AchievementUiRules
    {
        /// <summary>
        /// Шаг с индексом i недоступен для отображения прогресса, пока не выполнен порог предыдущего шага.
        /// </summary>
        public static bool IsSlotLocked(AchievementChainDefinition chain, int stepIndex, int statTotal)
        {
            if (chain == null || stepIndex <= 0)
                return false;
            if (stepIndex - 1 >= chain.Thresholds.Length)
                return true;
            return statTotal < chain.Thresholds[stepIndex - 1];
        }

        public static float SliderRatio(AchievementChainDefinition chain, int stepIndex, int statTotal)
        {
            if (chain == null || stepIndex < 0 || stepIndex >= chain.Thresholds.Length)
                return 0f;
            var t = Mathf.Max(1, chain.Thresholds[stepIndex]);
            var numerator = Mathf.Clamp(statTotal, 0, t);
            return numerator / (float)t;
        }
    }
}
