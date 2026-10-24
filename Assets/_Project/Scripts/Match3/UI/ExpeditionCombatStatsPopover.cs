using UnityEngine;
using UnityEngine.EventSystems;

namespace Project.Match3
{
    /// <summary>Shows the live combat-stat widgets on a header tap, without duplicating combat data.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionCombatStatsPopover : MonoBehaviour, IPointerClickHandler
    {
        private Match3PlayerPanel _panel;
        private RectTransform _popup;
        private bool _expanded;

        public bool Expanded => _expanded && _popup != null && _popup.gameObject.activeSelf;

        public void Bind(Match3PlayerPanel panel, RectTransform popup)
        {
            _panel = panel;
            _popup = popup;
            ApplyVisibility();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || _panel == null || _panel.IsRaceHudMode) return;
            _expanded = !_expanded;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (_popup != null) _popup.gameObject.SetActive(_expanded && _panel != null && !_panel.IsRaceHudMode);
        }

        private void OnDisable()
        {
            _expanded = false;
            ApplyVisibility();
        }
    }
}
