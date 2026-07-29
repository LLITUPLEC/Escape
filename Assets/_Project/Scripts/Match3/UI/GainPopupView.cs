using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Project.Match3
{
    /// <summary>
    /// Попап прироста HP/маны (+N). Вылетает снизу вверх по лёгкой дуге.
    /// Хилл и мана могут идти параллельно (разные flyout-ы).
    /// </summary>
    public sealed class GainPopupView : MonoBehaviour
    {
        public enum GainKind
        {
            Heal = 0,
            Mana = 1,
        }

        /// <summary>-1 влево, +1 вправо. 0 = выбрать случайно.</summary>
        public enum ArcSide
        {
            Random = 0,
            Left = -1,
            Right = 1,
        }

        private const string DropShadowMaterialResourcePath =
            "Fonts & Materials/LiberationSans SDF - Drop Shadow";

        [Header("Wiring")]
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Style")]
        [SerializeField] private Color healColor = new Color(0.23921569f, 0.8039216f, 0.23921569f, 1f);
        [SerializeField] private Color manaColor = new Color(0.27450982f, 0.5882353f, 0.8627451f, 1f);
        [SerializeField] private int fontSize = 50;

        [Header("Animation")]
        [SerializeField] private float duration = 1.55f;
        [SerializeField] private float punchScale = 1.05f;
        [SerializeField] private float startOffsetY = -100f;
        [SerializeField] private float endOffsetY = 58f;
        [SerializeField] private float arcHorizontal = 60f;
        [SerializeField] private float arcBulge = 20f;

        private static Material _dropShadowMaterial;

        private readonly List<Flyout> _active = new();
        private readonly Stack<Flyout> _pool = new();
        private Vector3 _baseScale = Vector3.one;
        private RectTransform _rt;
        private bool _prepared;

        private sealed class Flyout
        {
            public RectTransform Rt;
            public CanvasGroup Group;
            public TMP_Text Text;
            public Coroutine Routine;
        }

        private void Awake() => EnsurePrepared();

        private void EnsurePrepared()
        {
            if (_prepared) return;
            _prepared = true;

            _rt = transform as RectTransform;
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
            // Родительский CG не должен гасить flyout-ы (раньше alpha=0 прятал всё).
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            if (valueText == null)
                valueText = transform.Find("Value")?.GetComponent<TMP_Text>() ?? GetComponentInChildren<TMP_Text>(true);

            // Шаблон Value больше не рисуем — только источник стиля для Instantiate.
            if (valueText != null)
            {
                ApplyFlyoutTextStyle(valueText);
                valueText.gameObject.SetActive(false);
            }

            _baseScale = transform.localScale == Vector3.zero ? Vector3.one : transform.localScale;
        }

        /// <summary>Одиночный показ: дуга в случайную сторону.</summary>
        public void Play(int amount, GainKind kind) => Play(amount, kind, ArcSide.Random);

        public void Play(int amount, GainKind kind, ArcSide side)
        {
            if (amount <= 0) return;
            EnsurePrepared();
            if (!gameObject.activeInHierarchy) gameObject.SetActive(true);

            var sign = side == ArcSide.Random
                ? (Random.value < 0.5f ? -1f : 1f)
                : (float)(int)side;

            var fly = RentFlyout();
            fly.Text.text = "+" + amount;
            fly.Text.color = kind == GainKind.Mana ? manaColor : healColor;
            ApplyFlyoutTextStyle(fly.Text);
            fly.Group.alpha = 1f;
            fly.Rt.localScale = _baseScale;
            fly.Rt.anchoredPosition = new Vector2(0f, startOffsetY);
            fly.Rt.gameObject.SetActive(true);

            if (fly.Routine != null) StopCoroutine(fly.Routine);
            fly.Routine = StartCoroutine(AnimateFlyout(fly, sign));
        }

        private IEnumerator AnimateFlyout(Flyout fly, float sideSign)
        {
            _active.Add(fly);

            var t = 0f;
            var startScale = _baseScale;
            var peakScale = _baseScale * punchScale;
            var p0 = new Vector2(0f, startOffsetY);
            var p2 = new Vector2(sideSign * arcHorizontal, endOffsetY);
            var p1 = new Vector2(sideSign * (arcHorizontal * 0.35f), (startOffsetY + endOffsetY) * 0.5f + arcBulge);

            while (t < duration)
            {
                t += Time.deltaTime;
                var k = Mathf.Clamp01(t / Mathf.Max(0.0001f, duration));
                var eased = 1f - Mathf.Pow(1f - k, 2.2f);

                fly.Rt.anchoredPosition = EvalQuadBezier(p0, p1, p2, eased);

                var punch = k < 0.28f ? Mathf.Sin((k / 0.28f) * Mathf.PI) : 0f;
                fly.Rt.localScale = Vector3.Lerp(startScale, peakScale, punch);

                fly.Group.alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.5f) / 0.5f));

                yield return null;
            }

            fly.Routine = null;
            _active.Remove(fly);
            ReturnFlyout(fly);
        }

        private static Vector2 EvalQuadBezier(Vector2 p0, Vector2 p1, Vector2 p2, float k)
        {
            var one = 1f - k;
            return one * one * p0 + 2f * one * k * p1 + k * k * p2;
        }

        private Flyout RentFlyout()
        {
            if (_pool.Count > 0)
                return _pool.Pop();

            var go = new GameObject("GainFlyout");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = _rt != null && _rt.rect.width > 1f
                ? new Vector2(Mathf.Max(80f, _rt.rect.width), Mathf.Max(80f, _rt.rect.height))
                : new Vector2(120f, 120f);

            var group = go.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;
            // На случай, если у родителя снова поставят alpha=0.
            group.ignoreParentGroups = true;

            TMP_Text text;
            if (valueText != null)
            {
                var wasActive = valueText.gameObject.activeSelf;
                valueText.gameObject.SetActive(true);
                text = Instantiate(valueText, rt, false);
                valueText.gameObject.SetActive(wasActive);
                text.gameObject.name = "Value";
                text.gameObject.SetActive(true);
                var textRt = text.rectTransform;
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;
            }
            else
            {
                var textGo = new GameObject("Value");
                var textRt = textGo.AddComponent<RectTransform>();
                textRt.SetParent(rt, false);
                textRt.anchorMin = Vector2.zero;
                textRt.anchorMax = Vector2.one;
                textRt.offsetMin = Vector2.zero;
                textRt.offsetMax = Vector2.zero;
                text = textGo.AddComponent<TextMeshProUGUI>();
                text.alignment = TextAlignmentOptions.Center;
            }

            ApplyFlyoutTextStyle(text);
            return new Flyout { Rt = rt, Group = group, Text = text };
        }

        private void ApplyFlyoutTextStyle(TMP_Text text)
        {
            if (text == null) return;
            text.overflowMode = TextOverflowModes.Overflow;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.raycastTarget = false;
            text.enableAutoSizing = false;
            text.fontSize = fontSize;
            text.fontStyle = FontStyles.Bold;
            var mat = DropShadowMaterial;
            if (mat != null)
                text.fontSharedMaterial = mat;
        }

        private static Material DropShadowMaterial
        {
            get
            {
                if (_dropShadowMaterial != null) return _dropShadowMaterial;
                _dropShadowMaterial = Resources.Load<Material>(DropShadowMaterialResourcePath);
                return _dropShadowMaterial;
            }
        }

        private void ReturnFlyout(Flyout fly)
        {
            if (fly == null) return;
            fly.Group.alpha = 0f;
            fly.Rt.gameObject.SetActive(false);
            _pool.Push(fly);
        }
    }
}
