using Project.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>A separate interaction layer preserves the button owner's own disabled and busy states.</summary>
    [DisallowMultipleComponent]
    public sealed class ProgressiveContentButton : MonoBehaviour
    {
        private bool _workshop;
        private CanvasGroup _group;
        private TMP_Text _modeDescription;
        private string _availableDescription;
        private const string TournamentRequirement = "Откроется после 10 побед в шахте";
        private static readonly Color LockedDescription = new Color32(224, 188, 132, 255);

        public static ProgressiveContentButton Bind(Button button, bool workshop, TMP_Text modeDescription = null)
        {
            if (button == null) return null;
            var gate = ExpeditionUi.Ensure<ProgressiveContentButton>(button.gameObject);
            gate._workshop = workshop;
            if (modeDescription != null && gate._modeDescription != modeDescription)
            {
                gate._modeDescription = modeDescription;
                gate._availableDescription = modeDescription.text;
                var colors = button.colors;
                colors.disabledColor = new Color(.76f, .78f, .79f, 1f);
                button.colors = colors;
            }
            gate.Refresh();
            PlayerGuidanceService.RequestRefresh(true);
            return gate;
        }
        private void OnEnable() { PlayerGuidanceService.Changed += Refresh; Refresh(); }
        private void OnDisable() { PlayerGuidanceService.Changed -= Refresh; }
        private void LateUpdate() { PlayerGuidanceService.RequestRefresh(); Refresh(); }
        private void Refresh() => Apply(_workshop ? PlayerGuidanceService.WorkshopUnlocked : PlayerGuidanceService.TournamentsUnlocked);
        private void Apply(bool allowed)
        {
            if (_group == null) _group = ExpeditionUi.Ensure<CanvasGroup>(gameObject);
            _group.alpha = allowed || _modeDescription != null ? 1 : .38f;
            _group.interactable = allowed; _group.blocksRaycasts = allowed;
            if (_modeDescription != null)
            {
                _modeDescription.text = allowed ? _availableDescription : TournamentRequirement;
                _modeDescription.color = allowed ? ExpeditionTheme.Muted : LockedDescription;
            }
        }
    }
}
