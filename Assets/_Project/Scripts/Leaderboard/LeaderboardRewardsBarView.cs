using TMPro;
using UnityEngine;

namespace Project.Leaderboard
{
    public sealed class LeaderboardRewardsBarView : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private LeaderboardRewardTierView[] tiers;

        public void Bind(LeaderboardRewardTierDto[] rewards)
        {
            if (titleText != null)
                titleText.text = "TOP 3 REWARDS";

            if (tiers == null)
                return;

            for (var i = 0; i < tiers.Length; i++)
            {
                LeaderboardRewardTierDto dto = null;
                if (rewards != null)
                {
                    foreach (var r in rewards)
                    {
                        if (r != null && r.place == i + 1)
                        {
                            dto = r;
                            break;
                        }
                    }
                }

                if (tiers[i] != null)
                    tiers[i].Bind(i + 1, dto);
            }
        }
    }
}
