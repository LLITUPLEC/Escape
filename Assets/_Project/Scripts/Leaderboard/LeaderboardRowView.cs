using System.Globalization;
using Project.UI;
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
        public int BoundRank { get; private set; }
        private bool _presentationBuilt;
        private long _secondaryScore;
        private Image _rankMedal;
        private static Sprite[] _rankFrames;

        public void Bind(LeaderboardEntry entry)
        {
            if (entry == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            BoundRank = entry.Rank;
            _secondaryScore = entry.SecondaryScore;
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
            ApplyExpeditionPresentation();
        }

        /// <summary>Actual bound values in ornamental rows; no podium, portrait or synthesized player data.</summary>
        public void ApplyExpeditionPresentation()
        {
            var root = transform as RectTransform;
            if (root == null) return;
            if (!_presentationBuilt)
            {
                foreach (var layout in GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
                foreach (var fitter in GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
                // Keep serialized value writers, but give them a single predictable horizontal coordinate space.
                Reparent(rankText, root); Reparent(nicknameText, root); Reparent(scoreText, root);
                Reparent(secondaryStatText, root); Reparent(yourRankLabel, root);
                if (trophyIcon != null) trophyIcon.transform.SetParent(root, false);
                _rankMedal = ExpeditionUi.Ensure<Image>(ExpeditionUi.Rect(root, "ExpeditionRankMedal", 0, 0, 1, 1).gameObject);
                _rankMedal.sprite = ExpeditionTheme.Icon("trophy");
                _rankMedal.preserveAspect = true; _rankMedal.raycastTarget = false;
                _presentationBuilt = true;
            }
            var sticky = rowStyle == LeaderboardRowStyle.Sticky;
            var top = rowStyle == LeaderboardRowStyle.Gold || rowStyle == LeaderboardRowStyle.Silver || rowStyle == LeaderboardRowStyle.Bronze;
            Color accent = rowStyle switch
            {
                LeaderboardRowStyle.Gold => new Color32(238, 197, 107, 255),
                LeaderboardRowStyle.Silver => new Color32(208, 221, 218, 255),
                LeaderboardRowStyle.Bronze => new Color32(221, 157, 111, 255),
                _ => ExpeditionTheme.Ivory
            };
            var height = ExpeditionUi.Ensure<LayoutElement>(gameObject);
            height.preferredHeight = height.minHeight = top ? 230 : sticky ? 142 : 158;
            height.flexibleHeight = 0;
            var rootImage = GetComponent<Image>();
            if (rootImage != null && rootImage != rowBackground) { rootImage.enabled = false; rootImage.raycastTarget = false; }
            if (rowBackground == null) rowBackground = ExpeditionUi.Ensure<Image>(gameObject);
            var frame = top ? RankFrame(rowStyle) : null;
            rowBackground.enabled = true; rowBackground.overrideSprite = null;
            rowBackground.sprite = frame != null ? frame : ExpeditionTheme.Plate;
            rowBackground.type = frame != null ? Image.Type.Simple : Image.Type.Sliced;
            rowBackground.preserveAspect = frame != null;
            rowBackground.pixelsPerUnitMultiplier = 4;
            rowBackground.color = Color.white; rowBackground.raycastTarget = false;
            if (rowBackground.transform != transform)
            {
                ExpeditionUi.SetRect(rowBackground.rectTransform, 0, 0, 1, 1);
                rowBackground.transform.SetAsFirstSibling();
            }
            if (frameGlow != null) frameGlow.enabled = false;
            if (avatarImage != null) avatarImage.gameObject.SetActive(false);
            if (avatarFrame != null) avatarFrame.gameObject.SetActive(false);
            if (yourRankGroup != null) yourRankGroup.SetActive(false);
            if (secondaryStatGroup != null) secondaryStatGroup.SetActive(false);
            var x = top ? .292f : .205f;
            var nameRight = !top && !sticky && deltaGroup != null && deltaGroup.activeSelf ? .78f : .925f;
            PlaceLabel(rankText, top ? .127f : .035f, top ? .285f : .13f, top ? .226f : .154f, top ? .565f : .48f,
                top ? 48 : 36, accent, TextAlignmentOptions.Center);
            PlaceLabel(nicknameText, x, top ? .435f : .49f, nameRight, top ? .685f : .86f,
                top ? 37 : 35, ExpeditionTheme.Ivory, TextAlignmentOptions.MidlineLeft);
            PlaceLabel(scoreText, x + .049f, top ? .18f : .14f, .605f, top ? .43f : .47f,
                34, top ? accent : ExpeditionTheme.Amber, TextAlignmentOptions.MidlineLeft);
            PlaceLabel(secondaryStatText, .635f, top ? .18f : .14f, .925f, top ? .43f : .47f,
                27, ExpeditionTheme.Muted, TextAlignmentOptions.MidlineRight);
            if (secondaryStatText != null) secondaryStatText.gameObject.SetActive(_secondaryScore > 0);
            if (trophyIcon != null)
            {
                trophyIcon.sprite = ExpeditionTheme.Icon("trophy"); trophyIcon.overrideSprite = null;
                trophyIcon.type = Image.Type.Simple; trophyIcon.preserveAspect = true;
                trophyIcon.raycastTarget = false; trophyIcon.color = top ? accent : ExpeditionTheme.Amber;
                trophyIcon.enabled = trophyIcon.sprite != null;
                ExpeditionUi.SetRect(trophyIcon.rectTransform, x, top ? .19f : .17f, x + .04f, top ? .42f : .45f);
            }
            _rankMedal.enabled = !top && _rankMedal.sprite != null;
            _rankMedal.color = new Color32(166, 184, 183, 255);
            ExpeditionUi.SetRect(_rankMedal.rectTransform, .054f, .49f, .137f, .86f);
            if (yourRankLabel != null)
            {
                yourRankLabel.gameObject.SetActive(sticky);
                yourRankLabel.text = "ВАШЕ МЕСТО";
                PlaceLabel(yourRankLabel, .20f, .81f, .925f, .98f, 18, ExpeditionTheme.Muted, TextAlignmentOptions.MidlineRight);
            }
            if (deltaGroup != null)
            {
                ExpeditionUi.SetRect(deltaGroup.transform as RectTransform, .81f, .69f, .925f, .94f);
                if (deltaText != null)
                    PlaceLabel(deltaText, 0, 0, 1, 1, 21, deltaText.color, TextAlignmentOptions.MidlineRight);
                if (newBadge != null)
                {
                    ExpeditionUi.SetRect(newBadge.transform as RectTransform, 0, 0, 1, 1);
                    var background = newBadge.GetComponent<Image>();
                    if (background != null) { background.enabled = false; background.raycastTarget = false; }
                    var label = newBadge.GetComponentInChildren<TMP_Text>(true);
                    if (label != null) { label.text = "НОВЫЙ"; PlaceLabel(label, 0, 0, 1, 1, 18, ExpeditionTheme.Muted, TextAlignmentOptions.MidlineRight); }
                }
            }
        }

        private static void Reparent(TMP_Text label, RectTransform parent)
        {
            if (label != null) label.transform.SetParent(parent, false);
        }

        private static void PlaceLabel(TMP_Text label, float x0, float y0, float x1, float y1, float size, Color color, TextAlignmentOptions alignment)
        {
            if (label == null) return;
            ExpeditionUi.SetRect(label.rectTransform, x0, y0, x1, y1);
            label.rectTransform.localScale = Vector3.one;
            label.rectTransform.localRotation = Quaternion.identity;
            label.font = ExpeditionTheme.Font; label.fontStyle = FontStyles.Normal;
            label.fontSize = label.fontSizeMax = size; label.fontSizeMin = size * .86f;
            label.enableAutoSizing = true; label.color = color; label.alignment = alignment;
            label.textWrappingMode = TextWrappingModes.NoWrap; label.overflowMode = TextOverflowModes.Ellipsis;
            label.raycastTarget = false;
        }

        private static Sprite RankFrame(LeaderboardRowStyle style)
        {
            if (_rankFrames == null || _rankFrames.Length == 0)
                _rankFrames = Resources.LoadAll<Sprite>("Expedition/UI/leaderboard-rank-frames");
            var name = style == LeaderboardRowStyle.Gold ? "leaderboard-gold" : style == LeaderboardRowStyle.Silver ? "leaderboard-silver" : "leaderboard-bronze";
            foreach (var sprite in _rankFrames) if (sprite != null && sprite.name == name) return sprite;
            return null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetFrameCache() { _rankFrames = null; }

        private void BindDelta(LeaderboardEntry entry)
        {
            var showDelta = rowStyle == LeaderboardRowStyle.Standard && entry.Delta != LeaderboardRankDelta.None;
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
