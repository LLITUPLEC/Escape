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
                var root = ExpeditionUi.Rect(transform, "ExpeditionMailBadge", 1, 1, 1, 1); root.pivot = Vector2.one; root.anchoredPosition = new Vector2(-8, -8); root.sizeDelta = new Vector2(52, 48);
                _group = root.gameObject.AddComponent<CanvasGroup>(); _group.blocksRaycasts = false; _group.interactable = false;
                var image = root.gameObject.AddComponent<Image>(); image.sprite = ExpeditionArt.Frame(true) ?? ExpeditionTheme.Plate; image.type = Image.Type.Sliced; image.pixelsPerUnitMultiplier = 4; image.raycastTarget = false;
                _count = ExpeditionUi.Text(root, "UnreadCount", "", 29, .04f, .04f, .96f, .96f, TextAlignmentOptions.Center); _count.enableAutoSizing = true; _count.fontSizeMin = 22; _count.fontSizeMax = 29; _count.margin = Vector4.zero; _count.color = new Color32(35, 28, 18, 255);
            }
            var unread = MailService.UnreadCount; _count.text = unread > 99 ? "99+" : unread.ToString(); _group.alpha = unread > 0 ? 1 : 0;
        }
    }
}
