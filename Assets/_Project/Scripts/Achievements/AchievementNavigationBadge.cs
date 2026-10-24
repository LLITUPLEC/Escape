using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>Event-driven claim count for the shell navigation. Does not own the navigation click.</summary>
    [DisallowMultipleComponent]
    public sealed class AchievementNavigationBadge : MonoBehaviour
    {
        private CanvasGroup _group;
        private TMP_Text _count;
        public int PendingCount { get; private set; }

        public static AchievementNavigationBadge Bind(Button button)
        {
            if (button == null) return null;
            var badge = ExpeditionUi.Ensure<AchievementNavigationBadge>(button.gameObject);
            badge.Refresh();
            return badge;
        }

        private void OnEnable()
        {
            AchievementLifecycle.OnDataChanged += Refresh;
            Refresh();
        }
        private void OnDisable() { AchievementLifecycle.OnDataChanged -= Refresh; }

        public void Refresh()
        {
            if (_group == null) Build();
            PendingCount = AchievementUiPending.CountEligibleClaimSteps();
            _count.text = PendingCount > 99 ? "99+" : PendingCount.ToString();
            _group.alpha = PendingCount > 0 ? 1 : 0;
        }

        private void Build()
        {
            var root = transform.Find("ExpeditionAchievementBadge") as RectTransform;
            if (root == null) root = ExpeditionUi.Rect(transform, "ExpeditionAchievementBadge", 1, 1, 1, 1);
            root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
            root.anchoredPosition = new Vector2(-10, -8);
            root.sizeDelta = new Vector2(52, 48);
            root.SetAsLastSibling();
            _group = ExpeditionUi.Ensure<CanvasGroup>(root.gameObject);
            _group.blocksRaycasts = false; _group.interactable = false;
            var background = ExpeditionUi.Ensure<Image>(root.gameObject);
            background.sprite = ExpeditionArt.Frame(true);
            if (background.sprite == null) background.sprite = ExpeditionTheme.Plate;
            background.type = Image.Type.Sliced; background.pixelsPerUnitMultiplier = 4;
            background.color = Color.white; background.raycastTarget = false;
            var label = root.Find("Count") as RectTransform;
            if (label == null) label = ExpeditionUi.Rect(root, "Count", .06f, .06f, .94f, .94f);
            _count = ExpeditionUi.Ensure<TextMeshProUGUI>(label.gameObject);
            _count.font = ExpeditionTheme.Font; _count.fontSize = _count.fontSizeMax = 30; _count.fontSizeMin = 24;
            _count.enableAutoSizing = true; _count.alignment = TextAlignmentOptions.Center;
            _count.color = new Color32(35, 28, 18, 255); _count.raycastTarget = false;
        }
    }
}
