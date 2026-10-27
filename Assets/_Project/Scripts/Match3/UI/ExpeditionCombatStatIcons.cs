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
        public Image[] Icons => _icons;

        public void Bind()
        {
            for (var i = 0; i < Keys.Length; i++)
            {
                var left = i % 2 == 0 ? .045f : .525f;
                var bottom = i < 2 ? .405f : .065f;
                if (_cells[i] == null)
                {
                    _cells[i] = ExpeditionUi.Rect(transform, "CombatStat_" + Keys[i], left, bottom, left + .43f, bottom + .28f);
                    var iconRect = ExpeditionUi.Rect(_cells[i], "Icon", 0, .1f, .25f, .9f);
                    _icons[i] = iconRect.gameObject.AddComponent<Image>();
                    _icons[i].preserveAspect = true;
                    _icons[i].raycastTarget = false;
                    _values[i] = ExpeditionUi.Text(_cells[i], "Value", "—", 25, .29f, 0, 1, 1, TextAlignmentOptions.MidlineLeft);
                    _values[i].color = ExpeditionTheme.Ivory;
                    _values[i].enableAutoSizing = true;
                    _values[i].fontSizeMin = 16;
                    _values[i].fontSizeMax = 25;
                    _values[i].textWrappingMode = TextWrappingModes.NoWrap;
                }
                _icons[i].sprite = ExpeditionArt.Icon(Keys[i]);
                _icons[i].color = Color.white;
            }
        }

        public void SetValues(int damage, int armor, int healing, float criticalPercent, bool showHealing)
        {
            if (_values[0] == null) Bind();
            _values[0].text = Mathf.Max(0, damage).ToString();
            _values[1].text = Mathf.Max(0, armor).ToString();
            _values[2].text = Mathf.Max(0, healing).ToString();
            _values[3].text = Mathf.Max(0, criticalPercent).ToString("0.#") + "%";
            _cells[2].gameObject.SetActive(showHealing);
        }
    }
}
