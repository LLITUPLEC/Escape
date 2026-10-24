using Project.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>A short portrait response to actual combat damage; never moves the interactive board.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionBattleResponse : MonoBehaviour
    {
        private RectTransform _portrait;
        private Image _rim;
        private Vector2 _rest;
        private float _remaining;
        private Color _tint;

        public void Bind(Image portrait)
        {
            if (portrait == null || _rim != null) return;
            _portrait = portrait.rectTransform;
            _rest = _portrait.anchoredPosition;
            _rim = ExpeditionUi.Panel(_portrait, "CombatImpactRim", -.035f, -.025f, 1.035f, 1.025f);
            _rim.fillCenter = false;
            _rim.color = Color.clear;
        }
        public void Pulse(bool critical, bool mana)
        {
            if (_portrait == null) return;
            _remaining = critical ? .3f : .22f;
            _tint = mana ? ExpeditionTheme.Amber : ExpeditionTheme.Coral;
        }
        private void Update()
        {
            if (_remaining <= 0 || _portrait == null) return;
            _remaining = Mathf.Max(0, _remaining - Time.unscaledDeltaTime);
            _portrait.anchoredPosition = _rest + Vector2.right * (Mathf.Sin(_remaining * 72) * 4 * Mathf.Clamp01(_remaining / .22f));
            _tint.a = Mathf.Clamp01(_remaining / .22f) * .85f;
            _rim.color = _tint;
        }
        private void OnDisable()
        {
            _remaining = 0;
            if (_portrait != null) _portrait.anchoredPosition = _rest;
            if (_rim != null) _rim.color = Color.clear;
        }
    }
}
