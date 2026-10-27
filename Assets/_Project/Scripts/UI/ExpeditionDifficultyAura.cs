using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>A small shared radial texture behind an unchanged monster sprite. No per-monster texture variants.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionDifficultyAura : MonoBehaviour
    {
        private static Sprite _sharedGlow;
        private Image _target, _glow;
        private Color _color;
        private float _speed, _opacity, _nextTick;
        public string Difficulty { get; private set; }
        public Image Glow => _glow;

        public static ExpeditionDifficultyAura Set(Image target, string difficulty, bool visible = true)
        {
            if (target == null) return null;
            var view = target.GetComponent<ExpeditionDifficultyAura>();
            if (!visible || string.IsNullOrWhiteSpace(difficulty))
            {
                if (view != null) { view.enabled = false; if (view._glow != null) view._glow.enabled = false; }
                return view;
            }
            if (view == null) view = target.gameObject.AddComponent<ExpeditionDifficultyAura>();
            view._target = target;
            view.Difficulty = Normalize(difficulty);
            view._color = ColorFor(view.Difficulty);
            view._speed = view.Difficulty == "hard" ? 2.2f : view.Difficulty == "medium" ? 1.7f : 1.2f;
            view._opacity = view.Difficulty == "hard" ? .5f : view.Difficulty == "medium" ? .43f : .35f;
            if (view._glow == null)
            {
                var go = new GameObject(target.name + "DifficultyAura", typeof(RectTransform), typeof(Image));
                go.transform.SetParent(target.transform.parent, false);
                go.transform.SetSiblingIndex(target.transform.GetSiblingIndex());
                view._glow = go.GetComponent<Image>();
                view._glow.sprite = SharedGlow;
                view._glow.raycastTarget = false;
            }
            view.enabled = true;
            view.Refresh();
            return view;
        }

        public static string Normalize(string difficulty)
        {
            if (string.Equals(difficulty, "hard", StringComparison.OrdinalIgnoreCase) || string.Equals(difficulty, "Тяжёлая", StringComparison.OrdinalIgnoreCase)) return "hard";
            if (string.Equals(difficulty, "medium", StringComparison.OrdinalIgnoreCase) || string.Equals(difficulty, "Средняя", StringComparison.OrdinalIgnoreCase)) return "medium";
            return "easy";
        }

        public static Color ColorFor(string difficulty)
        {
            switch (Normalize(difficulty))
            {
                case "hard": return new Color32(255, 61, 58, 255);
                case "medium": return new Color32(255, 139, 38, 255);
                default: return new Color32(255, 220, 76, 255);
            }
        }

        public static Sprite SharedGlow
        {
            get
            {
                if (_sharedGlow != null) return _sharedGlow;
                const int side = 64;
                var texture = new Texture2D(side, side, TextureFormat.RGBA32, false)
                { name = "SharedMineDifficultyGlow", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
                var pixels = new Color32[side * side];
                for (var y = 0; y < side; y++)
                for (var x = 0; x < side; x++)
                {
                    var u = (x + .5f) / (side * .5f) - 1;
                    var v = (y + .5f) / (side * .5f) - 1;
                    var alpha = Mathf.Clamp01(1 - u * u - v * v);
                    pixels[y * side + x] = new Color32(255, 255, 255, (byte)(alpha * alpha * 255));
                }
                texture.SetPixels32(pixels);
                texture.Apply(false, true);
                _sharedGlow = Sprite.Create(texture, new Rect(0, 0, side, side), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
                _sharedGlow.name = "SharedMineDifficultyGlow";
                _sharedGlow.hideFlags = HideFlags.HideAndDontSave;
                return _sharedGlow;
            }
        }

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + .066f;
            Refresh();
        }

        private void Refresh()
        {
            if (_target == null || _glow == null) return;
            _glow.enabled = _target.enabled && _target.gameObject.activeInHierarchy && _target.sprite != null;
            if (!_glow.enabled) return;
            var source = _target.rectTransform;
            var rect = _glow.rectTransform;
            if (rect.parent != source.parent) rect.SetParent(source.parent, false);
            var sourceIndex = source.GetSiblingIndex();
            var glowIndex = rect.GetSiblingIndex();
            if (glowIndex > sourceIndex) rect.SetSiblingIndex(sourceIndex);
            else if (glowIndex < sourceIndex - 1) rect.SetSiblingIndex(sourceIndex - 1);
            rect.anchorMin = source.anchorMin; rect.anchorMax = source.anchorMax; rect.pivot = source.pivot;
            rect.sizeDelta = source.sizeDelta + source.rect.size * .14f;
            rect.anchoredPosition3D = source.anchoredPosition3D;
            rect.localRotation = source.localRotation;
            rect.localScale = source.localScale;
            var color = _color;
            color.a = _opacity * (.72f + .28f * (.5f + .5f * Mathf.Sin(Time.unscaledTime * _speed)));
            _glow.color = color;
        }

        private void OnDisable() { if (_glow != null) _glow.enabled = false; }
        private void OnDestroy()
        {
            if (_glow == null) return;
            if (Application.isPlaying) Destroy(_glow.gameObject); else DestroyImmediate(_glow.gameObject);
        }
    }
}
