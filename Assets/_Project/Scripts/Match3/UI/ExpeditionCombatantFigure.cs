using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Reuses the authoritative portrait art in the diorama, with a tiny throttled idle movement.</summary>
    public sealed class ExpeditionCombatantFigure : MonoBehaviour
    {
        private Image _source, _figure;
        private RectTransform _rect;
        private Vector2 _rest;
        private float _nextFrame, _phase;
        private bool _player;
        private Sprite _explorer;
        public void Bind(Image source, bool player)
        {
            _source = source;
            _figure = GetComponent<Image>();
            _rect = (RectTransform)transform;
            _rest = _rect.anchoredPosition;
            _phase = player ? 0 : 1.6f;
            _player = player;
            _explorer = Resources.Load<Sprite>("Expedition/Art/hero");
            ApplySprite(source != null ? source.sprite : null);
        }
        private void Update()
        {
            if (_figure == null || Time.unscaledTime < _nextFrame) return;
            _nextFrame = Time.unscaledTime + .08f;
            var sprite = _source != null ? _source.sprite : null;
            if (_figure.sprite != sprite) ApplySprite(sprite);
            _rect.anchoredPosition = _rest + Vector2.up * (Mathf.Sin(Time.unscaledTime * 1.7f + _phase) * .35f);
        }
        private void ApplySprite(Sprite sprite)
        {
            _figure.sprite = sprite;
            _figure.enabled = sprite != null;
            // The authored explorer faces left; the stone guardians face right.
            // Mirror only the scene figure so each looks toward the opponent.
            var flip = sprite == _explorer ? _player : !_player;
            _rect.localScale = new Vector3(flip ? -1 : 1, 1, 1);
        }
        private void OnDisable() { if (_rect != null) _rect.anchoredPosition = _rest; }
    }
}
