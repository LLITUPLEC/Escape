using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Leaderboard
{
    public sealed class LeaderboardRewardTierView : MonoBehaviour
    {
        [SerializeField] private Image medalImage;
        [SerializeField] private TMP_Text[] rewardLines;
        [SerializeField] private Color goldColor = new Color(1f, 0.84f, 0f, 1f);
        [SerializeField] private Color silverColor = new Color(0.78f, 0.8f, 0.86f, 1f);
        [SerializeField] private Color bronzeColor = new Color(0.8f, 0.5f, 0.2f, 1f);

        public void Bind(int place, LeaderboardRewardTierDto dto)
        {
            if (medalImage != null)
            {
                medalImage.color = place switch
                {
                    1 => goldColor,
                    2 => silverColor,
                    _ => bronzeColor,
                };
            }

            if (rewardLines == null)
                return;

            for (var i = 0; i < rewardLines.Length; i++)
            {
                var line = rewardLines[i];
                if (line == null)
                    continue;

                if (dto?.items != null && i < dto.items.Length && dto.items[i] != null)
                {
                    var item = dto.items[i];
                    line.text = $"{item.amount} {FormatIconId(item.icon_id)}";
                    line.gameObject.SetActive(true);
                }
                else
                {
                    line.gameObject.SetActive(false);
                }
            }
        }

        private static string FormatIconId(string iconId)
        {
            if (string.IsNullOrWhiteSpace(iconId))
                return string.Empty;
            return iconId switch
            {
                "gold" => "Au",
                "ore" => "Ore",
                "matter" => "Mt",
                "energy" => "En",
                "diamond" => "Dm",
                _ => iconId,
            };
        }
    }
}
