using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    public sealed class DamagePopupView : MonoBehaviour
    {
        [Header("Wiring")]
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private Image critBackground;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Style")]
        [SerializeField] private Color normalColor = new Color(1f, 0.22f, 0.22f, 1f);
        [SerializeField] private Color critColor = new Color(1f, 0.12f, 0.12f, 1f);
        [SerializeField] private int normalFontSize = 22;
        [SerializeField] private int critFontSize = 30;

        [Header("Animation")]
        [SerializeField] private float duration = 1.75f;
        [SerializeField] private float punchScale = 1.18f;
        [SerializeField] private float critPunchScale = 1.32f;
        [SerializeField] private float shakePx = 8f;

        private Coroutine _routine;
        private Vector3 _baseScale;
        private Vector2 _baseAnchoredPos;
        private RectTransform _rt;

        private void Awake()
        {
            _rt = transform as RectTransform;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            if (valueText == null)
                valueText = transform.Find("Value")?.GetComponent<TMP_Text>() ?? GetComponentInChildren<TMP_Text>(true);
            if (valueText != null)
            {
                valueText.overflowMode = TMPro.TextOverflowModes.Overflow;
                valueText.enableWordWrapping = false;
            }
            if (_rt != null)
            {
                var w = Mathf.Max(_rt.sizeDelta.x, 280f);
                var h = Mathf.Max(_rt.sizeDelta.y, 120f);
                _rt.sizeDelta = new Vector2(w, h);
            }
            _baseScale = transform.localScale;
            if (_rt != null) _baseAnchoredPos = _rt.anchoredPosition;
            HideImmediate();
        }

        public void Play(int damageAmount, bool isCrit)
        {
            if (damageAmount <= 0) return;
            // Keep the object active permanently; hide via CanvasGroup to avoid
            // "Coroutine couldn't be started ... object is inactive".
            if (!gameObject.activeInHierarchy) gameObject.SetActive(true);
            if (valueText != null)
            {
                valueText.text = "-" + damageAmount;
                valueText.color = isCrit ? critColor : normalColor;
                valueText.fontSize = isCrit ? critFontSize : normalFontSize;
                valueText.fontStyle = isCrit ? FontStyles.Bold : FontStyles.Normal;
            }
            if (critBackground != null) critBackground.enabled = isCrit;

            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(Animate(isCrit));
        }

        private IEnumerator Animate(bool isCrit)
        {
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            var t = 0f;
            var rt = _rt;
            var startScale = _baseScale;
            var targetScale = _baseScale * (isCrit ? critPunchScale : punchScale);

            while (t < duration)
            {
                t += Time.deltaTime;
                var k = Mathf.Clamp01(t / Mathf.Max(0.0001f, duration));

                // scale punch (ease out then back)
                var p = 1f - Mathf.Pow(1f - k, 3f);
                var scale = Vector3.Lerp(targetScale, startScale, p);
                transform.localScale = scale;

                // shake + slight upward drift
                if (rt != null)
                {
                    var shake = (1f - k) * shakePx;
                    var dx = (Mathf.PerlinNoise(Time.time * 25f, 0.13f) - 0.5f) * 2f * shake;
                    var dy = (Mathf.PerlinNoise(0.27f, Time.time * 25f) - 0.5f) * 2f * shake;
                    rt.anchoredPosition = _baseAnchoredPos + new Vector2(dx, dy + k * 12f);
                }

                if (canvasGroup != null)
                    canvasGroup.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.45f) / 0.55f));

                yield return null;
            }

            HideImmediate();
            _routine = null;
        }

        private void HideImmediate()
        {
            if (_rt != null) _rt.anchoredPosition = _baseAnchoredPos;
            transform.localScale = _baseScale;
            if (canvasGroup != null) canvasGroup.alpha = 0f;
            if (critBackground != null) critBackground.enabled = false;
        }
    }
}

