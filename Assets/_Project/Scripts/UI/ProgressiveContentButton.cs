using Project.Character;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>A separate interaction layer preserves the button owner's own disabled/busy state. No unlock explanations.</summary>
    [DisallowMultipleComponent]
    public sealed class ProgressiveContentButton : MonoBehaviour
    {
        private bool _workshop;
        private CanvasGroup _group;
        public static ProgressiveContentButton Bind(Button button, bool workshop)
        {
            if (button == null) return null;
            var gate = ExpeditionUi.Ensure<ProgressiveContentButton>(button.gameObject);
            gate._workshop = workshop; gate.Refresh(); PlayerGuidanceService.RequestRefresh(true); return gate;
        }
        private void OnEnable() { PlayerGuidanceService.Changed += Refresh; Refresh(); }
        private void OnDisable() { PlayerGuidanceService.Changed -= Refresh; }
        private void LateUpdate() { PlayerGuidanceService.RequestRefresh(); Refresh(); }
        private void Refresh() => Apply(_workshop ? PlayerGuidanceService.WorkshopUnlocked : PlayerGuidanceService.TournamentsUnlocked);
        private void Apply(bool allowed)
        {
            if (_group == null) _group = ExpeditionUi.Ensure<CanvasGroup>(gameObject);
            _group.alpha = allowed ? 1 : .38f;
            _group.interactable = allowed; _group.blocksRaycasts = allowed;
        }
    }
}
