using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Leaderboard
{
    public sealed class LeaderboardRowView : MonoBehaviour
    {
        [SerializeField] private LeaderboardRowStyle rowStyle = LeaderboardRowStyle.Standard;
        [SerializeField] private Image rowBackground;
        [SerializeField] private Image frameGlow;
        [SerializeField] private GameObject yourRankGroup;
        [SerializeField] private TMP_Text yourRankLabel;
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private GameObject deltaGroup;
        [SerializeField] private Image deltaIcon;
        [SerializeField] private TMP_Text deltaText;
        [SerializeField] private GameObject newBadge;
        [SerializeField] private Image avatarImage;
        [SerializeField] private Image avatarFrame;
        [SerializeField] private TMP_Text nicknameText;
        [SerializeField] private GameObject secondaryStatGroup;
        [SerializeField] private TMP_Text secondaryStatText;
        [SerializeField] private Image trophyIcon;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private Color goldAccent = new Color(1f, 0.84f, 0f, 1f);
        [SerializeField] private Color silverAccent = new Color(0.78f, 0.8f, 0.86f, 1f);
        [SerializeField] private Color bronzeAccent = new Color(0.8f, 0.5f, 0.2f, 1f);
        [SerializeField] private Color stickyAccent = new Color(0f, 0.92f, 1f, 1f);
        [SerializeField] private Color deltaUpColor = new Color(0.3f, 0.8f, 0.32f, 1f);
        [SerializeField] private Color deltaDownColor = new Color(0.96f, 0.26f, 0.21f, 1f);

        public LeaderboardRowStyle RowStyle => rowStyle;

        public void Bind(LeaderboardEntry entry)
        {
            if (entry == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            ApplyStyleVisuals();

            if (yourRankGroup != null)
                yourRankGroup.SetActive(rowStyle == LeaderboardRowStyle.Sticky);
            if (yourRankLabel != null && rowStyle == LeaderboardRowStyle.Sticky)
                yourRankLabel.text = "YOUR RANK";

            if (rankText != null)
                rankText.text = entry.Rank.ToString(CultureInfo.InvariantCulture);

            if (nicknameText != null)
            {
                nicknameText.text = entry.Nickname;
                nicknameText.color = rowStyle == LeaderboardRowStyle.Sticky
                    ? stickyAccent
                    : Color.white;
            }

            if (scoreText != null)
            {
                scoreText.text = FormatScore(entry.Score);
                scoreText.color = rowStyle switch
                {
                    LeaderboardRowStyle.Gold => goldAccent,
                    LeaderboardRowStyle.Silver => silverAccent,
                    LeaderboardRowStyle.Bronze => bronzeAccent,
                    LeaderboardRowStyle.Sticky => stickyAccent,
                    _ => Color.white,
                };
            }

            if (secondaryStatGroup != null)
                secondaryStatGroup.SetActive(entry.SecondaryScore > 0);
            if (secondaryStatText != null)
                secondaryStatText.text = entry.SecondaryScore.ToString("N0", CultureInfo.InvariantCulture);

            BindDelta(entry);
            ApplyAvatarPlaceholder(entry);
        }

        private void BindDelta(LeaderboardEntry entry)
        {
            var showDelta = rowStyle == LeaderboardRowStyle.Standard;
            if (deltaGroup != null)
                deltaGroup.SetActive(showDelta);
            if (!showDelta)
                return;

            if (newBadge != null)
                newBadge.SetActive(entry.Delta == LeaderboardRankDelta.New);

            if (deltaIcon != null)
                deltaIcon.gameObject.SetActive(false);

            if (deltaText != null)
            {
                var show = entry.Delta is LeaderboardRankDelta.Up or LeaderboardRankDelta.Down;
                deltaText.gameObject.SetActive(show);
                if (!show)
                    return;

                deltaText.color = entry.Delta == LeaderboardRankDelta.Up ? deltaUpColor : deltaDownColor;
                var arrow = entry.Delta == LeaderboardRankDelta.Up ? "▲" : "▼";
                deltaText.text = entry.DeltaAmount > 0
                    ? arrow + entry.DeltaAmount.ToString(CultureInfo.InvariantCulture)
                    : arrow;
            }
        }

        private void ApplyAvatarPlaceholder(LeaderboardEntry entry)
        {
            if (avatarImage == null)
                return;

            var hash = string.IsNullOrEmpty(entry.UserId) ? entry.Nickname.GetHashCode() : entry.UserId.GetHashCode();
            var hue = Mathf.Abs(hash % 360) / 360f;
            avatarImage.color = Color.HSVToRGB(hue, 0.35f, 0.75f);
        }

        private void ApplyStyleVisuals()
        {
            if (rowBackground != null)
            {
                rowBackground.color = rowStyle switch
                {
                    LeaderboardRowStyle.Gold => new Color(0.22f, 0.17f, 0.05f, 0.95f),
                    LeaderboardRowStyle.Silver => new Color(0.14f, 0.15f, 0.18f, 0.95f),
                    LeaderboardRowStyle.Bronze => new Color(0.18f, 0.11f, 0.06f, 0.95f),
                    LeaderboardRowStyle.Sticky => new Color(0.04f, 0.14f, 0.2f, 0.98f),
                    _ => new Color(0.08f, 0.09f, 0.11f, 0.88f),
                };
            }

            if (frameGlow != null)
            {
                frameGlow.enabled = rowStyle != LeaderboardRowStyle.Standard;
                frameGlow.color = rowStyle switch
                {
                    LeaderboardRowStyle.Gold => goldAccent,
                    LeaderboardRowStyle.Silver => silverAccent,
                    LeaderboardRowStyle.Bronze => bronzeAccent,
                    LeaderboardRowStyle.Sticky => stickyAccent,
                    _ => Color.clear,
                };
            }

            if (avatarFrame != null)
            {
                avatarFrame.color = rowStyle switch
                {
                    LeaderboardRowStyle.Gold => goldAccent,
                    LeaderboardRowStyle.Silver => silverAccent,
                    LeaderboardRowStyle.Bronze => bronzeAccent,
                    LeaderboardRowStyle.Sticky => stickyAccent,
                    _ => new Color(0.35f, 0.37f, 0.4f, 1f),
                };
            }

            if (trophyIcon != null)
            {
                trophyIcon.color = rowStyle switch
                {
                    LeaderboardRowStyle.Gold => goldAccent,
                    LeaderboardRowStyle.Silver => silverAccent,
                    LeaderboardRowStyle.Bronze => bronzeAccent,
                    LeaderboardRowStyle.Sticky => stickyAccent,
                    _ => silverAccent,
                };
            }

            if (rankText != null)
            {
                rankText.fontSize = rowStyle switch
                {
                    LeaderboardRowStyle.Gold => 54f,
                    LeaderboardRowStyle.Silver => 46f,
                    LeaderboardRowStyle.Bronze => 42f,
                    LeaderboardRowStyle.Sticky => 48f,
                    _ => 28f,
                };
                rankText.color = rowStyle switch
                {
                    LeaderboardRowStyle.Gold => goldAccent,
                    LeaderboardRowStyle.Silver => silverAccent,
                    LeaderboardRowStyle.Bronze => bronzeAccent,
                    LeaderboardRowStyle.Sticky => stickyAccent,
                    _ => new Color(0.72f, 0.74f, 0.78f, 1f),
                };
            }
        }

        public static string FormatScore(long score)
        {
            if (score >= 1_000_000_000)
                return (score / 1_000_000_000f).ToString("0.#", CultureInfo.InvariantCulture) + "B";
            if (score >= 1_000_000)
                return (score / 1_000_000f).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (score >= 10_000)
                return (score / 1_000f).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return score.ToString("N0", CultureInfo.InvariantCulture);
        }
    }
}
