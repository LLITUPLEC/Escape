using UnityEngine;
using UnityEngine.UI;
using Project.Match3;

namespace Project.UI
{
    /// <summary>One environment across the physical screen, independent of safe-area UI pages.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionSceneWorld : MonoBehaviour
    {
        private RectTransform _viewport, _picture, _groundTarget, _figure;
        private Image _image;
        private string _art, _groundArt;
        private Vector2 _groundUv, _targetUv, _figureUv;
        private Vector2 _lastSize, _lastTarget;
        private bool _dirty = true;
        private Image _figureImage;
        private Image _figureShadow;
        private Sprite _lastFigureSprite;
        private bool _lastFigureEnabled;
        private Vector2 _lastFigureParentSize;
        public string Art => _art;
        public Image Illustration => _image;

        public void Build(RectTransform viewport)
        {
            _viewport = viewport;
            _picture = ExpeditionUi.Rect(viewport, "WorldIllustration", 0, 0, 1, 1);
            _image = _picture.gameObject.AddComponent<Image>();
            _image.raycastTarget = false;
        }

        public void Show(string art)
        {
            if (_art == art) return;
            var sprite = Resources.Load<Sprite>("Expedition/Art/" + art);
            if (sprite == null) return;
            _art = art;
            _image.sprite = sprite;
            _image.color = Color.white;
            _dirty = true;
        }

        public void Ground(string art, RectTransform target, Vector2 targetUv, Vector2 sourceUv)
        {
            _groundArt = art; _groundTarget = target; _targetUv = targetUv; _groundUv = sourceUv;
            _dirty = true;
        }

        public void PlaceShelterFigure(RectTransform figure, Vector2 sourceUv)
        {
            _figure = figure; _figureImage = figure != null ? figure.GetComponent<Image>() : null;
            _figureShadow = figure != null ? figure.parent.Find("ShelterContactShadow")?.GetComponent<Image>() : null;
            _figureUv = sourceUv; _dirty = true;
        }

        private void LateUpdate()
        {
            if (_viewport == null || _image == null || _image.sprite == null) return;
            var size = _viewport.rect.size;
            if (_figureImage != null && (_lastFigureSprite != _figureImage.sprite || _lastFigureEnabled != _figureImage.enabled))
            { _lastFigureSprite = _figureImage.sprite; _lastFigureEnabled = _figureImage.enabled; _dirty = true; }
            if (_figure != null && _figure.parent is RectTransform figureParent && _lastFigureParentSize != figureParent.rect.size)
            { _lastFigureParentSize = figureParent.rect.size; _dirty = true; }
            var target = Vector2.zero;
            var grounded = _art == _groundArt && _groundTarget != null && _groundTarget.gameObject.activeInHierarchy;
            if (grounded)
            {
                var rect = _groundTarget.rect;
                var point = _groundTarget.TransformPoint(new Vector2(Mathf.Lerp(rect.xMin, rect.xMax, _targetUv.x), Mathf.Lerp(rect.yMin, rect.yMax, _targetUv.y)));
                target = _viewport.InverseTransformPoint(point);
            }
            if (!_dirty && size == _lastSize && (!grounded || target == _lastTarget)) return;
            _dirty = false; _lastSize = size; _lastTarget = target;
            var source = _image.sprite.rect.size;
            var scale = Mathf.Max(size.x / source.x, size.y / source.y);
            var center = Vector2.zero;
            if (grounded)
            {
                // Enlarge only as far as needed to align the illustrated floor and cover every edge.
                scale = Mathf.Max(scale, (target.y + size.y * .5f) / Mathf.Max(1, source.y * _groundUv.y),
                    (size.y * .5f - target.y) / Mathf.Max(1, source.y * (1 - _groundUv.y)));
                center = target - Vector2.Scale(_groundUv - Vector2.one * .5f, source * scale);
                center.x = Mathf.Clamp(center.x, (size.x - source.x * scale) * .5f, (source.x * scale - size.x) * .5f);
            }
            _picture.anchorMin = _picture.anchorMax = _picture.pivot = Vector2.one * .5f;
            _picture.sizeDelta = source * scale;
            _picture.anchoredPosition = center;
            if ((_art == "hub-backdrop" || _art == "shelter-mine-light") && _figure != null)
            {
                var point = _picture.TransformPoint(Vector2.Scale(_figureUv - Vector2.one * .5f, _picture.rect.size));
                var parent = _figure.parent as RectTransform;
                Vector2 position = parent.InverseTransformPoint(point);
                _figure.anchorMin = _figure.anchorMax = Vector2.one * .5f;
                if (_art == "shelter-mine-light" && _figureImage != null && _figureImage.sprite != null)
                {
                    ExpeditionCombatantGrounding.TryGet(_figureImage.sprite, out var footprint);
                    _figure.pivot = footprint?.Contact ?? new Vector2(.5f, .02f);
                    _figure.sizeDelta = ExpeditionCombatantGrounding.FullImageSize(_figureImage.sprite, footprint, parent.rect.width * .43f, parent.rect.height * .385f);
                    // The contact point follows the painted floor. Keep the opaque body clear of the left controls and physical right edge on tall screens.
                    var solid = footprint?.SolidBounds ?? new Rect(0, 0, 1, 1);
                    var min = parent.rect.xMin + parent.rect.width * .37f - (solid.xMin - _figure.pivot.x) * _figure.sizeDelta.x;
                    var max = parent.rect.xMin + parent.rect.width * .965f - (solid.xMax - _figure.pivot.x) * _figure.sizeDelta.x;
                    position.x = Mathf.Clamp(position.x, min, Mathf.Max(min, max));
                }
                else
                {
                    _figure.pivot = new Vector2(.5f, .02f);
                    _figure.sizeDelta = new Vector2(parent.rect.width * .18f, parent.rect.height * .17f);
                }
                _figure.anchoredPosition = position;
                if (_figureShadow != null)
                {
                    _figureShadow.enabled = _art == "shelter-mine-light" && _figureImage != null && _figureImage.sprite != null && _figureImage.enabled;
                    if (_figureShadow.enabled)
                    {
                        ExpeditionCombatantGrounding.TryGet(_figureImage.sprite, out var footprint);
                        var solid = footprint?.SolidBounds ?? new Rect(0, 0, 1, 1);
                        var shadow = _figureShadow.rectTransform;
                        shadow.anchorMin = shadow.anchorMax = shadow.pivot = Vector2.one * .5f;
                        shadow.anchoredPosition = position + new Vector2((solid.center.x - _figure.pivot.x) * _figure.sizeDelta.x, -1);
                        var width = Mathf.Min(parent.rect.width * .35f, solid.width * _figure.sizeDelta.x * .72f);
                        shadow.sizeDelta = new Vector2(width, Mathf.Clamp(width * .08f, 12, 27));
                    }
                }
            }
        }
    }
}
