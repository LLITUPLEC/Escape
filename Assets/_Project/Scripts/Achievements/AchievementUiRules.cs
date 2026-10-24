using UnityEngine;

namespace Project.Achievements
{
    public static class AchievementUiRules
    {
        /// <summary>The additional work for this step. Eligibility still uses CumulativeRequired.</summary>
        public static int StepTarget(AchievementChainDefinition chain, int stepIndex)
        {
            return chain?.Thresholds != null && stepIndex >= 0 && stepIndex < chain.Thresholds.Length
                ? Mathf.Max(0, chain.Thresholds[stepIndex]) : 0;
        }

        public static int StepProgress(AchievementChainDefinition chain, int stepIndex, int statTotal)
        {
            var previous = stepIndex > 0 ? CumulativeRequired(chain, stepIndex - 1) : 0;
            return Mathf.Clamp(statTotal - previous, 0, StepTarget(chain, stepIndex));
        }

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
            var t = Mathf.Max(1, StepTarget(chain, stepIndex));
            var numerator = StepProgress(chain, stepIndex, statTotal);
            return numerator / (float)t;
        }
    }
}
