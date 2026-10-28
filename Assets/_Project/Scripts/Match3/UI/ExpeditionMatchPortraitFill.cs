using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Fills a masked matchup card using solid artwork bounds instead of PNG padding.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionMatchPortraitFill : MonoBehaviour
    {
        private Image _image;
        private Sprite _sprite;
        private Vector2 _parentSize;
        private bool _refreshing;

        public void Bind(Image image)
        {
            _image = image;
            enabled = true;
            Refresh();
        }

        private void LateUpdate()
        {
            if (_image == null) return;
            var parent = transform.parent as RectTransform;
            if (_sprite != _image.sprite || parent != null && parent.rect.size != _parentSize) Refresh();
        }

        private void OnRectTransformDimensionsChange()
        {
            if (isActiveAndEnabled) Refresh();
        }

        public void Refresh()
        {
            if (_refreshing || _image == null || _image.sprite == null || !(transform.parent is RectTransform parent)) return;
            var available = parent.rect.size;
            if (available.x <= 0 || available.y <= 0) return;
            _refreshing = true;
            try
            {
                _sprite = _image.sprite;
                _parentSize = available;
                var solid = ExpeditionCombatantGrounding.TryGet(_sprite, out var footprint)
                    ? footprint.SolidBounds : new Rect(0, 0, 1, 1);
                var source = _sprite.rect.size;
                var scale = Mathf.Max(available.x / (source.x * solid.width), available.y / (source.y * solid.height));
                var size = source * scale;
                var rect = _image.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
                rect.sizeDelta = size;
                // Keep the head at the top; the existing mask crops the lower body
                // and sides as it does for a player's bust portrait.
                rect.anchoredPosition = new Vector2((.5f - solid.center.x) * size.x,
                    available.y * .5f - (solid.yMax - .5f) * size.y);
                _image.preserveAspect = true;
            }
            finally { _refreshing = false; }
        }
    }
}
