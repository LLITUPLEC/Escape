using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Висит на <c>aura_btn</c>: показ при активной серверной аномалии, вращение <c>border</c>, таймер в <c>timer</c>.
    /// </summary>
    public sealed class ServerAuraButtonView : MonoBehaviour
    {
        [SerializeField] private RectTransform border;
        [SerializeField] private TMP_Text timerTmp;
        [SerializeField] private Text timerText;
        [Tooltip("Скорость вращения border против часовой стрелки (градусы/сек).")]
        [SerializeField] private float borderRotationSpeedDegreesPerSecond = 45f;

        private bool _spinning;
        private long _endsAtUnix;
        private float _timerAccum;

        public float BorderRotationSpeedDegreesPerSecond
        {
            get => borderRotationSpeedDegreesPerSecond;
            set => borderRotationSpeedDegreesPerSecond = value;
        }

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
            if (!_spinning) return;

            if (border != null)
            {
                // Положительный Z в Unity UI — против часовой стрелки.
                border.Rotate(0f, 0f, borderRotationSpeedDegreesPerSecond * Time.unscaledDeltaTime, Space.Self);
            }

            _timerAccum += Time.unscaledDeltaTime;
            if (_timerAccum >= 0.25f)
            {
                _timerAccum = 0f;
                RefreshTimerLabel();
            }
        }

        /// <summary>Показать/скрыть кнопку, вращение border и обратный отсчёт до <paramref name="endsAtUnix"/>.</summary>
        public void SetAnomalyActive(bool active, long endsAtUnix = 0)
        {
            EnsureRefs();
            _spinning = active;
            _endsAtUnix = endsAtUnix;
            _timerAccum = 0f;
            if (!active && border != null)
                border.localRotation = Quaternion.identity;
            if (!active)
                SetTimerLabel("");
            else
                RefreshTimerLabel();
            gameObject.SetActive(active);
        }

        private void RefreshTimerLabel()
        {
            if (!_spinning || _endsAtUnix <= 0)
            {
                SetTimerLabel(_spinning ? "00:00:00" : "");
                return;
            }

            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var remain = Math.Max(0L, _endsAtUnix - now);
            var h = remain / 3600L;
            var m = (remain % 3600L) / 60L;
            var s = remain % 60L;
            SetTimerLabel($"{h:00}:{m:00}:{s:00}");

            if (remain <= 0)
            {
                // Время вышло — прячем до следующего RPC-обновления.
                SetAnomalyActive(false);
            }
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
            if (borderRotationSpeedDegreesPerSecond < 0f)
                borderRotationSpeedDegreesPerSecond = 0f;
        }
#endif
    }
}
