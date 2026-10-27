using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Friends
{
    /// <summary>The visible shell shortcut observes the controller's trusted count; owns no friend action, polling or RPC.</summary>
    [DisallowMultipleComponent]
    public sealed class FriendsNavigationBadge : MonoBehaviour
    {
        private FriendsIncomingInviteState _state;
        private CanvasGroup _group;
        private TMP_Text _count;
        public int PendingCount => _state?.Count ?? 0;
        public bool IsVisible => _group != null && _group.alpha > 0;
        public RectTransform BadgeRect { get; private set; }
        public static FriendsNavigationBadge Bind(Button button, FriendsPanelController source) => BindState(button, source?.IncomingInvites);
        public static FriendsNavigationBadge BindState(Button button, FriendsIncomingInviteState state)
        {
            if (button == null) return null;
            var badge = ExpeditionUi.Ensure<FriendsNavigationBadge>(button.gameObject);
            if (badge._state != null) badge._state.Changed -= badge.Refresh;
            badge._state = state;
            if (badge.isActiveAndEnabled && state != null) state.Changed += badge.Refresh;
            state?.SynchronizeAccount(); badge.Refresh(); return badge;
        }
        private void OnEnable() { if (_state != null) { _state.Changed += Refresh; _state.SynchronizeAccount(); } Refresh(); }
        private void OnDisable() { if (_state != null) _state.Changed -= Refresh; }
        private void Update() { _state?.SynchronizeAccount(); }
        private void Refresh()
        {
            if (_group == null) Build();
            _count.text = PendingCount > 99 ? "99+" : PendingCount.ToString();
            _group.alpha = PendingCount > 0 ? 1 : 0;
        }
        private void Build()
        {
            var root = transform.Find("ExpeditionFriendsBadge") as RectTransform;
            if (root == null) root = ExpeditionUi.Rect(transform, "ExpeditionFriendsBadge", 1, 1, 1, 1);
            BadgeRect = root; root.anchorMin = root.anchorMax = root.pivot = Vector2.one;
            root.anchoredPosition = new Vector2(-10, -8); root.sizeDelta = new Vector2(52, 48); root.SetAsLastSibling();
            _group = ExpeditionUi.Ensure<CanvasGroup>(root.gameObject); _group.blocksRaycasts = false; _group.interactable = false;
            var image = ExpeditionUi.Ensure<Image>(root.gameObject); image.sprite = ExpeditionArt.Frame(true) ?? ExpeditionTheme.Plate;
            image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 4; image.color = Color.white; image.raycastTarget = false;
            var label = root.Find("Count") as RectTransform;
            if (label == null) label = ExpeditionUi.Rect(root, "Count", .06f, .06f, .94f, .94f);
            _count = ExpeditionUi.Ensure<TextMeshProUGUI>(label.gameObject);
            _count.font = ExpeditionTheme.Font; _count.fontSize = _count.fontSizeMax = 30; _count.fontSizeMin = 24;
            _count.enableAutoSizing = true; _count.alignment = TextAlignmentOptions.Center;
            _count.color = new Color32(35, 28, 18, 255); _count.raycastTarget = false;
        }
    }
}
