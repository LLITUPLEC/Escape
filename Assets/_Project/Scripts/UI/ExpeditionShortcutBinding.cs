using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Mirrors the state owned by the original shortcut and its reward/invite badge.</summary>
    public sealed class ExpeditionShortcutBinding : MonoBehaviour
    {
        private Button _source, _target;
        private Transform _sourceBadge;
        private CanvasGroup _sourceBadgeGroup;
        private TMP_Text _sourceCount, _count;
        private GameObject _badge;
        private float _nextRefresh;

        public void Bind(Button source)
        {
            _source = source;
            _target = GetComponent<Button>();
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .25f;
            Refresh();
        }

        public void Refresh()
        {
            if (_target == null) return;
            // The retired HUD's parent CanvasGroup is intentionally non-interactable.
            // The original Button itself still owns its loading / availability state.
            _target.interactable = _source != null && _source.interactable;
            if (_source != null && _sourceBadge == null)
            {
                _sourceBadge = _source.transform.Find("PendingClaimBadge");
                if (_sourceBadge != null)
                {
                    _sourceBadgeGroup = _sourceBadge.GetComponent<CanvasGroup>();
                    _sourceCount = _sourceBadge.GetComponentInChildren<TMP_Text>(true);
                }
            }
            var value = _sourceCount != null ? _sourceCount.text : null;
            var visible = _sourceBadge != null && _sourceBadge.gameObject.activeSelf &&
                (_sourceBadgeGroup == null || _sourceBadgeGroup.alpha > .01f) &&
                !string.IsNullOrWhiteSpace(value) && value != "0";
            if (visible && _badge == null)
            {
                var image = ExpeditionUi.Panel(transform, "ExpeditionPendingBadge", 1, 1, 1, 1, ExpeditionTheme.Amber);
                var rect = image.rectTransform;
                rect.sizeDelta = new Vector2(46, 46);
                rect.anchoredPosition = new Vector2(-12, -8);
                _badge = image.gameObject;
                _count = ExpeditionUi.Text(rect, "Count", value, 26, 0, 0, 1, 1, TextAlignmentOptions.Center);
                _count.margin = Vector4.zero;
                _count.color = ExpeditionTheme.Panel;
                _count.enableAutoSizing = true;
                _count.fontSizeMin = 19;
                _count.fontSizeMax = 26;
            }
            if (_badge == null) return;
            if (_badge.activeSelf != visible) _badge.SetActive(visible);
            if (visible && _count.text != value) _count.text = value;
        }
    }

    /// <summary>Displays the existing presence count without another network poll.</summary>
    public sealed class ExpeditionOnlineStatus : MonoBehaviour
    {
        private TMP_Text _source, _label;
        private Text _legacySource;
        private string _lastValue;
        private float _nextRefresh;

        public void Bind(Transform source)
        {
            var count = source != null ? source.Find("CountText") : null;
            if (count != null) { _source = count.GetComponent<TMP_Text>(); _legacySource = count.GetComponent<Text>(); }
            _label = ExpeditionUi.Text(transform, "OnlineCount", "Онлайн: —", 24, 0, .03f, 1, .97f, TextAlignmentOptions.MidlineLeft);
            _label.color = ExpeditionTheme.Muted;
            _label.margin = Vector4.zero;
            _label.textWrappingMode = TextWrappingModes.NoWrap;
            _label.enableAutoSizing = true;
            _label.fontSizeMin = 20;
            _label.fontSizeMax = 25;
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextRefresh) return;
            _nextRefresh = Time.unscaledTime + .5f;
            Refresh();
        }
        private void Refresh()
        {
            var value = _source != null ? _source.text : _legacySource != null ? _legacySource.text : "—";
            if (_label == null || value == _lastValue) return;
            _lastValue = value;
            // Existing count source only: no new presence requests. Escape any rich text from its label.
            value = value.Replace("<", "").Replace(">", "");
            _label.text = "<color=#A6DE71>●</color> Онлайн  <color=#B6F27C>" + value + "</color>";
        }
    }
}
