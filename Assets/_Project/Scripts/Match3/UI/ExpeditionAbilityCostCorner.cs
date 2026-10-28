using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Positions the live numeric cost on the authored beige corner, without changing the ability sprite.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionAbilityCostCorner : MonoBehaviour
    {
        private Image _icon;
        private TMP_Text _cost;
        private RectTransform _button;
        private Vector2 _lastSize;
        private Sprite _lastSprite;
        private Material _inkMaterial;
        private TMP_FontAsset _inkFont;
        private static readonly Color Ink = new Color32(38, 31, 23, 255);

        public void Bind(Image icon, TMP_Text cost)
        {
            _icon = icon;
            _cost = cost;
            _button = transform as RectTransform;
            Layout();
        }

        private void LateUpdate()
        {
            if (_icon == null || _cost == null) return;
            if (_lastSize != _icon.rectTransform.rect.size || _lastSprite != _icon.sprite) Layout();
        }

        public void RefreshPresentation()
        {
            if (_cost == null) return;
            _cost.color = Ink;
            _cost.fontStyle |= FontStyles.Bold;
            _cost.fontWeight = FontWeight.Bold;
            ApplyInkWeight();
            _cost.enableAutoSizing = true;
            _cost.fontSizeMin = 20;
            _cost.fontSizeMax = 26;
            _cost.fontSize = 26;
        }

        private void ApplyInkWeight()
        {
            if (_cost.font == null) return;
            if (_inkMaterial == null || _inkFont != _cost.font)
            {
                ReleaseInkMaterial();
                _inkFont = _cost.font;
                // PT Serif ships as Regular with synthetic bold, not a separate heavy typeface.
                // A local SDF face dilation makes the tiny corner digits visibly solid without changing the shared font/material.
                _inkMaterial = new Material(_cost.font.material) { name = "AbilityCost Ink Weight", hideFlags = HideFlags.HideAndDontSave };
                if (_inkMaterial.HasProperty("_FaceDilate")) _inkMaterial.SetFloat("_FaceDilate", .16f);
                if (_inkMaterial.HasProperty("_OutlineWidth")) _inkMaterial.SetFloat("_OutlineWidth", .035f);
                if (_inkMaterial.HasProperty("_OutlineColor")) _inkMaterial.SetColor("_OutlineColor", Ink);
            }
            if (_cost.fontSharedMaterial != _inkMaterial) _cost.fontSharedMaterial = _inkMaterial;
            _cost.UpdateMeshPadding();
        }

        private void OnDestroy() => ReleaseInkMaterial();
        private void ReleaseInkMaterial()
        {
            if (_inkMaterial == null) return;
            if (_cost != null && _cost.fontSharedMaterial == _inkMaterial && _cost.font != null) _cost.fontSharedMaterial = _cost.font.material;
            if (Application.isPlaying) Destroy(_inkMaterial); else DestroyImmediate(_inkMaterial);
            _inkMaterial = null; _inkFont = null;
        }

        private void OnRectTransformDimensionsChange() => Layout();

        private void Layout()
        {
            if (_icon == null || _cost == null || _button == null) return;
            _lastSize = _icon.rectTransform.rect.size;
            _lastSprite = _icon.sprite;
            var drawing = _icon.GetPixelAdjustedRect();
            if (_icon.preserveAspect && _icon.sprite != null && drawing.height > 0)
            {
                var aspect = _icon.sprite.rect.width / _icon.sprite.rect.height;
                var fitted = drawing.width / drawing.height > aspect
                    ? new Vector2(drawing.height * aspect, drawing.height) : new Vector2(drawing.width, drawing.width / aspect);
                drawing = new Rect(drawing.center - fitted * .5f, fitted);
            }
            var cornerCenter = new Vector2(drawing.x + drawing.width * .815f, drawing.y + drawing.height * .105f);
            var center = _button.InverseTransformPoint(_icon.rectTransform.TransformPoint(cornerCenter));
            var rect = _cost.rectTransform;
            // Keep AbilityCost a direct child: Match3AbilityPanel's normal update
            // path finds this exact name and continues writing the live number.
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = (Vector2)center - _button.rect.center;
            rect.sizeDelta = new Vector2(drawing.width * .32f, drawing.height * .21f);
            rect.localScale = Vector3.one;
            rect.SetAsLastSibling();
            _cost.color = Ink;
            _cost.alignment = TextAlignmentOptions.Center;
            _cost.margin = Vector4.zero;
            _cost.textWrappingMode = TextWrappingModes.NoWrap;
            _cost.overflowMode = TextOverflowModes.Overflow;
            _cost.enableAutoSizing = true;
            _cost.raycastTarget = false;
            RefreshPresentation();
        }
    }
}
