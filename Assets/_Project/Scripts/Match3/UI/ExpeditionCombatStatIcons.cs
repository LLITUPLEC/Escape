using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Four live combat values using the same canonical icons as the Hero sheet.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionCombatStatIcons : MonoBehaviour
    {
        private readonly TMP_Text[] _values = new TMP_Text[4];
        private readonly Image[] _icons = new Image[4];
        private readonly RectTransform[] _cells = new RectTransform[4];
        private static readonly string[] Keys = { "stat-damage", "stat-armor", "stat-healing", "stat-critical" };
        private bool _showHealing = true;
        private bool _layingOut;
        public Image[] Icons => _icons;

        public void Bind()
        {
            // The serialized stats frame may still have the old text-column layout.
            // It must not reposition these explicitly centred icon/value pairs.
            foreach (var group in GetComponents<LayoutGroup>()) group.enabled = false;
            foreach (var fitter in GetComponents<ContentSizeFitter>()) fitter.enabled = false;
            for (var i = 0; i < Keys.Length; i++)
            {
                if (_cells[i] == null)
                {
                    _cells[i] = ExpeditionUi.Rect(transform, "CombatStat_" + Keys[i], 0, 0, 1, 1);
                    var iconRect = ExpeditionUi.Rect(_cells[i], "Icon", 0, 0, 1, 1);
                    _icons[i] = iconRect.gameObject.AddComponent<Image>();
                    _icons[i].preserveAspect = true;
                    _icons[i].raycastTarget = false;
                    _values[i] = ExpeditionUi.Text(_cells[i], "Value", "—", 50, 0, 0, 1, 1, TextAlignmentOptions.MidlineLeft);
                    _values[i].color = ExpeditionTheme.Ivory;
                    _values[i].enableAutoSizing = true;
                    _values[i].fontSizeMin = 32;
                    _values[i].fontSizeMax = 50;
                    _values[i].textWrappingMode = TextWrappingModes.NoWrap;
                    _values[i].margin = Vector4.zero;
                }
                _icons[i].sprite = ExpeditionArt.Icon(Keys[i]);
                _icons[i].color = Color.white;
            }
            Reflow();
        }

        public void SetValues(int damage, int armor, int healing, float criticalPercent, bool showHealing)
        {
            if (_values[0] == null) Bind();
            _values[0].text = Mathf.Max(0, damage).ToString();
            _values[1].text = Mathf.Max(0, armor).ToString();
            _values[2].text = Mathf.Max(0, healing).ToString();
            _values[3].text = Mathf.Max(0, criticalPercent).ToString("0.#") + "%";
            _showHealing = showHealing;
            _cells[2].gameObject.SetActive(showHealing);
            Reflow();
        }

        private void OnRectTransformDimensionsChange() => Reflow();

        private void Reflow()
        {
            if (_layingOut) return;
            _layingOut = true;
            try
            {
                for (var i = 0; i < _cells.Length; i++)
                {
                    if (_cells[i] == null) continue;
                    var left = i % 2 == 0 ? .055f : .52f;
                    if (i == 3 && !_showHealing) left = .2875f;
                    var bottom = i < 2 ? .46f : .10f;
                    ExpeditionUi.SetRect(_cells[i], left, bottom, left + .425f, bottom + .33f);
                    var valueWidth = Mathf.Clamp(_cells[i].rect.width - 72, 44, 134);
                    Centre(_icons[i].rectTransform, new Vector2(-(valueWidth + 12) * .5f, 0), new Vector2(50, 50));
                    Centre(_values[i].rectTransform, new Vector2(31, 0), new Vector2(valueWidth, 62));
                }
            }
            finally { _layingOut = false; }
        }

        private static void Centre(RectTransform rect, Vector2 position, Vector2 size)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }
    }
}
