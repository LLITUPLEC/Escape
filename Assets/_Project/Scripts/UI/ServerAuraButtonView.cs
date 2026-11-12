using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Показывает сейсмограф убежища и оставшееся время активной аномалии.</summary>
    public sealed class ServerAuraButtonView : MonoBehaviour
    {
        [SerializeField] private RectTransform border;
        [SerializeField] private TMP_Text timerTmp;
        [SerializeField] private Text timerText;
        private bool _anomalyActive;
        private bool _readoutInstalled;
        private long _endsAtUnix;
        private float _timerAccum;

        private void Awake()
        {
            EnsureRefs();
        }

        private void OnEnable()
        {
            EnsureRefs();
            RefreshTimerLabel();
        }

        private void Update()
        {
            if (!_anomalyActive) return;
            _timerAccum += Time.unscaledDeltaTime;
            if (_timerAccum >= 0.25f)
            {
                _timerAccum = 0f;
                RefreshTimerLabel();
            }
        }

        internal void InstallReadout()
        {
            if (_readoutInstalled) return;
            EnsureRefs();
            var button = border != null ? border.GetComponentInChildren<Button>(true) : null;
            if (button == null) return;
            _readoutInstalled = true;
            var oldIcon = transform.Find("img");
            if (oldIcon != null) oldIcon.gameObject.SetActive(false);

            ExpeditionUi.SetRect(button.transform as RectTransform, 0, 0, 1, 1);
            ExpeditionUi.StyleButton(button);
            var screen = ExpeditionUi.Rect(button.transform, "AnomalySeismograph", .06f, .16f, .372f, .84f);
            var waveform = screen.gameObject.AddComponent<ExpeditionAuraSignal>();
            waveform.raycastTarget = false;

            var title = ExpeditionUi.Text(button.transform, "ExpeditionAuraTitle", "АНОМАЛИЯ", 25,
                .427f, .50f, .956f, .89f, TextAlignmentOptions.MidlineLeft);
            title.fontStyle = FontStyles.Bold;
            title.enableAutoSizing = true;
            title.fontSizeMin = 18;
            title.fontSizeMax = 25;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.margin = Vector4.zero;
            title.color = new Color32(179, 243, 220, 255);

            if (timerTmp != null)
            {
                timerTmp.transform.SetParent(button.transform, false);
                ExpeditionUi.SetRect(timerTmp.rectTransform, .427f, .13f, .956f, .50f);
                timerTmp.font = ExpeditionTheme.Font;
                timerTmp.fontSize = 27;
                timerTmp.enableAutoSizing = true;
                timerTmp.fontSizeMin = 17;
                timerTmp.fontSizeMax = 27;
                timerTmp.textWrappingMode = TextWrappingModes.NoWrap;
                timerTmp.alignment = TextAlignmentOptions.MidlineLeft;
                timerTmp.margin = Vector4.zero;
                timerTmp.color = ExpeditionTheme.Ivory;
                timerTmp.raycastTarget = false;
            }
            if (timerText != null)
            {
                timerText.transform.SetParent(button.transform, false);
                ExpeditionUi.SetRect(timerText.rectTransform, .427f, .13f, .956f, .50f);
                timerText.font = ExpeditionTheme.LegacyFont;
                timerText.fontSize = 25;
                timerText.alignment = TextAnchor.MiddleLeft;
                timerText.color = ExpeditionTheme.Ivory;
                timerText.raycastTarget = false;
            }
        }

        public void SetAnomalyActive(bool active, long endsAtUnix = 0)
        {
            EnsureRefs();
            _anomalyActive = active;
            _endsAtUnix = active ? endsAtUnix : 0;
            _timerAccum = 0f;
            RefreshTimerLabel();
            gameObject.SetActive(_anomalyActive);
        }

        private void RefreshTimerLabel()
        {
            if (!_anomalyActive || _endsAtUnix <= 0)
            {
                SetTimerLabel(_anomalyActive ? "Активна" : "");
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var remain = _endsAtUnix - now;
            if (remain <= 0)
            {
                SetAnomalyActive(false);
                return;
            }
            var h = remain / 3600L;
            var m = (remain % 3600L) / 60L;
            var s = remain % 60L;
            SetTimerLabel($"{h:00}:{m:00}:{s:00}");

        }

        private void SetTimerLabel(string value)
        {
            if (timerTmp != null) timerTmp.text = value ?? "";
            if (timerText != null) timerText.text = value ?? "";
        }

        private void EnsureRefs()
        {
            if (border == null)
            {
                var t = transform.Find("border");
                if (t != null)
                    border = t as RectTransform ?? t.GetComponent<RectTransform>();
                if (border == null)
                {
                    foreach (var rt in GetComponentsInChildren<RectTransform>(true))
                    {
                        if (rt != null && rt != (RectTransform)transform &&
                            string.Equals(rt.name, "border", StringComparison.OrdinalIgnoreCase))
                        {
                            border = rt;
                            break;
                        }
                    }
                }
            }

            if (timerTmp == null && timerText == null)
            {
                var timerTr = transform.Find("timer");
                if (timerTr == null)
                {
                    foreach (var t in GetComponentsInChildren<Transform>(true))
                    {
                        if (t != null && string.Equals(t.name, "timer", StringComparison.OrdinalIgnoreCase))
                        {
                            timerTr = t;
                            break;
                        }
                    }
                }

                if (timerTr != null)
                {
                    timerTmp = timerTr.GetComponent<TMP_Text>() ?? timerTr.GetComponentInChildren<TMP_Text>(true);
                    timerText = timerTr.GetComponent<Text>() ?? timerTr.GetComponentInChildren<Text>(true);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureRefs();
        }
#endif
    }
}
