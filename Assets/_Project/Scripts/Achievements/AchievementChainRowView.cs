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
        [SerializeField] private bool overrideArrowColorByState;

        public void Bind(AchievementChainDefinition chain, Action<string, int> onStepClick, Sprite chainIcon = null)
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
                var cumulativeNeed = AchievementUiRules.CumulativeRequired(chain, i);
                var thresholdMet = stat >= cumulativeNeed;
                var prevCumulative = i > 0 ? AchievementUiRules.CumulativeRequired(chain, i - 1) : 0;
                var stepNeed = Mathf.Max(1, target);

                var numerator = locked ? 0 : Mathf.Clamp(stat - prevCumulative, 0, stepNeed);

                var rewardClaimed = AchievementProgressStorage.IsStepClaimed(chain.ChainId, i);
                var canClaimReward = AchievementRewardClaim.CanClaimStep(chain, i);

                slot.Apply(chain.ChainId, i, tierColor,
                    chain.Descriptions[i],
                    chain.RewardTexts[i],
                    numerator,
                    stepNeed,
                    locked,
                    thresholdMet,
                    rewardClaimed,
                    canClaimReward,
                    onStepClick,
                    chainIcon);

                if (arrows != null && i < n - 1 && i < arrows.Length && arrows[i] != null)
                {
                    arrows[i].gameObject.SetActive(true);
                    if (overrideArrowColorByState)
                        arrows[i].color = locked ? new Color(0.35f, 0.35f, 0.37f, 1f) : new Color(1f, 0.82f, 0.16f, 1f);
                }
            }
        }
    }
}
