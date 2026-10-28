using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Only count presentation. Existing mail/friend/quest owners continue to choose when and what to display.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionGoldCountBadge : MonoBehaviour
    {
        public CanvasGroup Group { get; private set; }
        public TMP_Text Label { get; private set; }
        public RectTransform Rect => transform as RectTransform;
        private Material _ink;
        private TMP_Text _caption;
        private Vector2 _captionOffsetMax;

        public static ExpeditionGoldCountBadge Build(Transform parent, string name, string labelName, Vector2 offset)
        {
            var root = parent.Find(name) as RectTransform;
            if (root == null) root = ExpeditionUi.Rect(parent, name, 1, 1, 1, 1);
            var badge = ExpeditionUi.Ensure<ExpeditionGoldCountBadge>(root.gameObject); badge.Configure(labelName, offset); return badge;
        }

        private void Configure(string labelName, Vector2 offset)
        {
            Rect.anchorMin = Rect.anchorMax = Rect.pivot = Vector2.one; Rect.anchoredPosition = offset; Rect.SetAsLastSibling();
            Group = ExpeditionUi.Ensure<CanvasGroup>(gameObject); Group.blocksRaycasts = Group.interactable = false;
            var legacy = GetComponent<Image>(); if (legacy != null) { legacy.enabled = false; legacy.raycastTarget = false; }
            var face = ExpeditionUi.Ensure<ExpeditionGoldCountGraphic>(gameObject); face.raycastTarget = false;
            var label = transform.Find(labelName) as RectTransform;
            if (label == null) label = ExpeditionUi.Rect(transform, labelName, 0, 0, 1, 1);
            ExpeditionUi.SetRect(label, 0, 0, 1, 1); label.offsetMin = new Vector2(8, 3); label.offsetMax = new Vector2(-8, -3);
            Label = ExpeditionUi.Ensure<TextMeshProUGUI>(label.gameObject); Label.font = ExpeditionTheme.Font;
            Label.fontSize = Label.fontSizeMin = Label.fontSizeMax = 34; Label.enableAutoSizing = false; Label.fontStyle = FontStyles.Bold; Label.fontWeight = FontWeight.Bold;
            Label.color = new Color32(53, 39, 22, 255); Label.margin = Vector4.zero; Label.raycastTarget = false;
            Label.alignment = TextAlignmentOptions.Midline; Label.textWrappingMode = TextWrappingModes.NoWrap; Label.overflowMode = TextOverflowModes.Overflow;
            if (_ink == null)
            {
                _ink = new Material(Label.font.material) { name = "Gold Count Ink", hideFlags = HideFlags.HideAndDontSave };
                if (_ink.HasProperty("_FaceDilate")) _ink.SetFloat("_FaceDilate", .16f);
            }
            Label.fontSharedMaterial = _ink; Label.UpdateMeshPadding();
            if (_caption == null)
            {
                var caption = transform.parent.Find("Caption") ?? transform.parent.Find("Label");
                _caption = caption != null ? caption.GetComponent<TMP_Text>() : null;
                if (_caption != null) _captionOffsetMax = _caption.rectTransform.offsetMax;
            }
        }

        public void Present(int count)
        {
            count = Mathf.Max(0, count); Label.text = count.ToString(CultureInfo.InvariantCulture);
            Rect.sizeDelta = new Vector2(Mathf.Max(42, Mathf.Ceil(Label.GetPreferredValues(Label.text, float.PositiveInfinity, float.PositiveInfinity).x) + 20), 44);
            Group.alpha = count > 0 ? 1 : 0; Rect.SetAsLastSibling();
            if (_caption != null && transform.parent is RectTransform parent)
            {
                var originalRight = parent.rect.width * _caption.rectTransform.anchorMax.x + _captionOffsetMax.x;
                var right = count > 0 ? Mathf.Min(originalRight, parent.rect.width + Rect.anchoredPosition.x - Rect.sizeDelta.x - 8) : originalRight;
                _caption.rectTransform.offsetMax = new Vector2(_captionOffsetMax.x + right - originalRight, _captionOffsetMax.y);
            }
        }
        private void OnDestroy()
        {
            if (_ink == null) return;
            if (Label != null && Label.font != null && Label.fontSharedMaterial == _ink) Label.fontSharedMaterial = Label.font.material;
            if (Application.isPlaying) Destroy(_ink); else DestroyImmediate(_ink); _ink = null;
        }
    }
}
