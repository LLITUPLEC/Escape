using Project.Nakama;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Independent input cover: never overwrites a button owner's busy/progression state.</summary>
    public sealed class GameplayConnectionButton : MonoBehaviour, IPointerClickHandler, IPointerDownHandler
    {
        public static void Bind(Button button)
        {
            if (button == null || button.transform.Find("OfflinePlayCover") != null) return;
            var image = ExpeditionUi.Fill(button.transform, "OfflinePlayCover", 0, 0, 1, 1, new Color(0.015f, .035f, .045f, .62f));
            image.raycastTarget = true;
            var guard = image.gameObject.AddComponent<GameplayConnectionButton>();
            guard.Refresh();
        }
        private void LateUpdate() => Refresh();
        private void Refresh()
        {
            // Keep this component alive to observe recovery; only its drawing/hit surface is toggled.
            var image = GetComponent<Image>();
            if (image != null) { image.enabled = !ServerConnectionGuard.CanPlay; image.raycastTarget = !ServerConnectionGuard.CanPlay; }
        }
        public void OnPointerClick(PointerEventData eventData) { if (!ServerConnectionGuard.CanPlay) ServerConnectionGuard.ShowUnavailable(); }
        public void OnPointerDown(PointerEventData eventData) { }
    }
}
