using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Unread server mail count on the visible Shelter shortcut.</summary>
    public sealed class ExpeditionMailBadge : MonoBehaviour
    {
        private CanvasGroup _group;
        private TMP_Text _count;
        private ExpeditionGoldCountBadge _presentation;
        public static ExpeditionMailBadge Bind(Button button)
        {
            if (button == null) return null;
            var badge = ExpeditionUi.Ensure<ExpeditionMailBadge>(button.gameObject); badge.Refresh(); MailService.RequestRefresh(true); return badge;
        }
        private void OnEnable() { MailService.Changed += Refresh; Refresh(); }
        private void OnDisable() => MailService.Changed -= Refresh;
        private void Update() { MailService.RequestRefresh(); Refresh(); }
        private void Refresh()
        {
            if (_group == null)
            {
                _presentation = ExpeditionGoldCountBadge.Build(transform, "ExpeditionMailBadge", "UnreadCount", new Vector2(-8, -8));
                _group = _presentation.Group; _count = _presentation.Label;
            }
            _presentation.Present(MailService.UnreadCount);
        }
    }
}
