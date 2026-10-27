using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Visual state only. The original ToggleGroup and pair callbacks own selection.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionArenaRadioVisual : MonoBehaviour, ISelectHandler, IDeselectHandler
    {
        private Toggle _toggle;
        private ExpeditionArenaRadioGraphic _ring, _dot;
        private bool _focused;
        public void Bind(Toggle toggle, ExpeditionArenaRadioGraphic ring, ExpeditionArenaRadioGraphic dot)
        {
            if (_toggle != null) _toggle.onValueChanged.RemoveListener(Changed);
            _toggle = toggle; _ring = ring; _dot = dot;
            if (isActiveAndEnabled && _toggle != null) _toggle.onValueChanged.AddListener(Changed);
            Refresh();
        }
        public void Refresh()
        {
            if (_toggle == null) return;
            var available = _toggle.IsInteractable();
            _ring?.Configure(false, _toggle.isOn, available, _focused && available);
            _dot?.Configure(true, _toggle.isOn, available, _focused && available);
            if (_dot != null) _dot.canvasRenderer.SetAlpha(_toggle.isOn ? 1 : 0);
        }
        public void OnSelect(BaseEventData _) { _focused = true; Refresh(); }
        public void OnDeselect(BaseEventData _) { _focused = false; Refresh(); }
        private void Changed(bool _) { Refresh(); }
        private void OnEnable() { if (_toggle != null) _toggle.onValueChanged.AddListener(Changed); Refresh(); }
        private void OnDisable() { if (_toggle != null) _toggle.onValueChanged.RemoveListener(Changed); _focused = false; }
        private void OnDestroy() { if (_toggle != null) _toggle.onValueChanged.RemoveListener(Changed); }
    }
}
