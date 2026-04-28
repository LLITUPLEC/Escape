using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>Одна строка цепочки: несколько слотов и стрелок между ними.</summary>
    public sealed class AchievementChainRowView : MonoBehaviour
    {
        [SerializeField] private AchievementChainSlotView[] slots;
        [SerializeField] private Graphic[] arrows;

        public void Bind(AchievementChainDefinition chain, Action<string, int> onStepClick)
        {
            var stat = AchievementProgressStorage.GetStat(chain.StatKey);
            var n = chain.Thresholds.Length;

            if (slots == null || slots.Length == 0)
                slots = GetComponentsInChildren<AchievementChainSlotView>(true);

            for (var i = 0; i < slots.Length; i++)
            {
                var slot = slots[i];
                if (slot == null)
                    continue;
                if (i >= n)
                {
                    slot.gameObject.SetActive(false);
                    if (arrows != null && i - 1 >= 0 && i - 1 < arrows.Length && arrows[i - 1] != null)
                        arrows[i - 1].gameObject.SetActive(false);
                    continue;
                }

                slot.gameObject.SetActive(true);
                var tierColor = chain.TierAccentColors != null && i < chain.TierAccentColors.Length
                    ? chain.TierAccentColors[i]
                    : Color.white;
                var target = chain.Thresholds[i];
                var locked = AchievementUiRules.IsSlotLocked(chain, i, stat);
                var completed = stat >= target;

                var numerator = locked ? 0 : Mathf.Clamp(stat, 0, target);

                slot.Apply(chain.ChainId, i, tierColor,
                    chain.Descriptions[i],
                    chain.RewardTexts[i],
                    numerator,
                    target,
                    locked,
                    completed,
                    onStepClick);

                if (arrows != null && i < n - 1 && i < arrows.Length && arrows[i] != null)
                {
                    arrows[i].gameObject.SetActive(true);
                    arrows[i].color = locked ? new Color(0.35f, 0.35f, 0.37f, 1f) : new Color(1f, 0.82f, 0.16f, 1f);
                }
            }
        }
    }
}
