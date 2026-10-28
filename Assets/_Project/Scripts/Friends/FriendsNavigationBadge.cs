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
        private ExpeditionGoldCountBadge _presentation;
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
            _presentation.Present(PendingCount);
        }
        private void Build()
        {
            _presentation = ExpeditionGoldCountBadge.Build(transform, "ExpeditionFriendsBadge", "Count", new Vector2(-10, -8));
            BadgeRect = _presentation.Rect; _group = _presentation.Group; _count = _presentation.Label;
        }
    }
}
