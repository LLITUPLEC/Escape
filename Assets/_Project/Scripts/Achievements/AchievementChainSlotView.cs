using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Achievements
{
    public sealed class AchievementChainSlotView : MonoBehaviour
    {
        [SerializeField] private Image frameImage;
        [SerializeField] private Image iconImage;
        [SerializeField] private Image fillImage;
        [SerializeField] private Slider progressSlider;
        [SerializeField] private TMP_Text progressTmp;
        [SerializeField] private TMP_Text rewardTmp;
        [SerializeField] private GameObject lockOverlay;
        [Header("Optional runtime color overrides")]
        [SerializeField] private bool overrideFrameColorByState;
        [SerializeField] private bool overrideFillColorsByTier;
        [Header("Claimed visuals")]
        [SerializeField] private bool showClaimedOverlay = true;
        [SerializeField] private Color claimedOverlayColor = new Color(0.45f, 0.95f, 0.55f, 0.22f);

        [Header("Claim hint (threshold met, reward not claimed)")]
        [SerializeField] private bool showUnclaimedHint = true;
        [SerializeField] private Color unclaimedHintColor = new Color(1f, 0.25f, 0.25f, 0.9f);
        [SerializeField] private Sprite unclaimedHintHeadSprite;
        [SerializeField] private Color unclaimedHintTrailColor = new Color(1f, 0.85f, 0.15f, 0.55f);
        [SerializeField, Min(2f)] private float unclaimedHintDotSize = 14f;
        [SerializeField, Min(2f)] private float unclaimedHintTrailThickness = 3f;
        [SerializeField, Min(4f)] private float unclaimedHintTrailLength = 22f;
        [SerializeField, Min(10f)] private float unclaimedHintSpeedPxPerSecond = 220f;
        [SerializeField, Range(0f, 40f)] private float unclaimedHintInset = 2f;
        [Header("Unclaimed glow (neon pulse)")]
        [SerializeField] private bool showUnclaimedGlow = true;
        [SerializeField] private Color unclaimedGlowColor = new Color(0.25f, 1f, 0.35f, 0.85f);
        [SerializeField, Min(0f)] private float unclaimedGlowMinAlpha = 0.12f;
        [SerializeField, Min(0f)] private float unclaimedGlowMaxAlpha = 0.45f;
        [SerializeField, Min(0.1f)] private float unclaimedGlowPulseSpeed = 0.7f;
        [SerializeField, Min(0f)] private float unclaimedGlowOutsetHorizontal = 77f;
        [SerializeField, Min(0f)] private float unclaimedGlowOutsetVertical = 100f;
        [SerializeField, Range(0f, 4f)] private float unclaimedGlowZOffset = 0.1f;

        private CanvasGroup _rootCanvasGroup;
        private Button _infoButton;
        private Action<string, int> _onStepClick;
        private string _chainId;
        private int _stepIndex;

        private RectTransform _claimHintRoot;
        private RectTransform _claimDotA;
        private RectTransform _claimDotB;
        private RectTransform _claimTrailA;
        private RectTransform _claimTrailB;
        private bool _claimHintActive;

        private static Sprite _runtimeWhiteSprite;
        private static Sprite _runtimeTriangleSprite;
        private static Sprite _runtimeTrailGradientSprite;
        private static Sprite _runtimeOuterGlowSprite;

        private RectTransform _glowRt;
        private Image _glowImg;

        private Coroutine _sliderLayoutRefreshCo;

        private void Awake()
        {
            _rootCanvasGroup = GetComponent<CanvasGroup>();
        }

        private void OnDisable()
        {
            if (_sliderLayoutRefreshCo != null)
            {
                StopCoroutine(_sliderLayoutRefreshCo);
                _sliderLayoutRefreshCo = null;
            }
            SetClaimHintActive(false);
        }

        public void Apply(
            string chainId,
            int stepIndex,
            Color tierAccent,
            string requirementText,
            string rewardLine,
            int numerator,
            int denominator,
            bool lockedByChain,
            bool thresholdMet,
            bool rewardClaimed,
            bool canClaimReward,
            Action<string, int> onStepClick,
            Sprite chainIcon = null)
        {
            if (rewardTmp != null)
                rewardTmp.text = rewardLine ?? string.Empty;

            var grayLocked = lockedByChain && !thresholdMet;

            if (progressTmp != null)
                progressTmp.text = denominator > 0 ? numerator + "/" + denominator : string.Empty;

            if (progressSlider != null)
            {
                var numClamped = Mathf.Clamp(numerator, 0, denominator);
                var denClamped = Mathf.Max(1, denominator);
                var t01 = Mathf.Clamp01(numClamped / (float)denClamped);

                // Interactable=false у Slider часто откладывает/пропускает пересчёт Fill (якоря fillRect) при value из кода,
                // пока не дернётся инспектор/ребилд — полоска визуально остаётся «полной». Клики всё равно не нужны: raycast ниже.
                progressSlider.interactable = true;
                progressSlider.wholeNumbers = true;
                progressSlider.minValue = 0;
                progressSlider.maxValue = denClamped;
                progressSlider.value = numClamped;
                Canvas.ForceUpdateCanvases();

                // После Instantiate + Scroll/layout ширина трека в первом кадре может быть ещё неверной —
                // Slider рисует fillRect до финального layout и «колбаска» выглядит полной. Повтор через кадр.
                ApplyProgressFillManual(t01);
                ScheduleProgressSliderLayoutRefresh(numClamped, denClamped);

                // Slider двигает только RectTransform.Fill; режим Image «Filled» рисуется по fillAmount и игнорирует ширину — нужен Simple.
                Image fillImg = progressSlider.fillRect != null ? progressSlider.fillRect.GetComponent<Image>() : null;
                if (fillImg != null)
                {
                    fillImg.type = Image.Type.Simple;
                    fillImg.fillAmount = 1f;
                    if (overrideFillColorsByTier)
                        fillImg.color = tierAccent;
                }

                var bg = progressSlider.transform.Find("Background");
                if (bg != null)
                {
                    var bgImg = bg.GetComponent<Image>();
                    if (bgImg != null)
                        bgImg.color = new Color(0.1f, 0.1f, 0.12f, 0.95f);
                }
            }

            if (fillImage != null)
            {
                var sameAsSliderFill = progressSlider != null && progressSlider.fillRect != null
                    && fillImage.gameObject == progressSlider.fillRect.gameObject;
                if (!sameAsSliderFill)
                {
                    fillImage.type = Image.Type.Simple;
                    fillImage.fillAmount = 1f;
                    if (overrideFillColorsByTier)
                        fillImage.color = tierAccent;
                }
            }

            if (frameImage != null && overrideFrameColorByState)
            {
                Color frameCol = grayLocked
                    ? new Color(0.35f, 0.35f, 0.37f, 1f)
                    : canClaimReward
                        ? new Color(0.95f, 0.76f, 0.22f, 1f)
                        : tierAccent;
                frameImage.color = frameCol;
            }

            if (iconImage != null)
            {
                if (chainIcon != null)
                    iconImage.sprite = chainIcon;
                iconImage.enabled = iconImage.sprite != null;
                iconImage.color = grayLocked ? new Color(0.35f, 0.35f, 0.38f, 1f) : Color.white;
                iconImage.raycastTarget = false;
            }

            if (rewardTmp != null)
                rewardTmp.raycastTarget = false;
            if (progressTmp != null)
                progressTmp.raycastTarget = false;
            if (progressSlider != null)
            {
                foreach (var g in progressSlider.GetComponentsInChildren<Graphic>(true))
                    g.raycastTarget = false;
            }

            if (lockOverlay != null)
            {
                var showOverlay = grayLocked || (showClaimedOverlay && rewardClaimed);
                lockOverlay.SetActive(showOverlay);
                var lockImg = lockOverlay.GetComponent<Image>();
                if (lockImg != null)
                {
                    if (rewardClaimed && !grayLocked)
                        lockImg.color = claimedOverlayColor;
                    lockImg.raycastTarget = false;
                }
            }

            if (_rootCanvasGroup != null)
                _rootCanvasGroup.alpha = rewardClaimed ? 1f : grayLocked ? 0.55f : 1f;

            var showHint = showUnclaimedHint && !grayLocked && thresholdMet && canClaimReward && !rewardClaimed;
            SetClaimHintActive(showHint);
            SetGlowActive(showHint && showUnclaimedGlow);

            gameObject.name = "Step_" + stepIndex + "_" + chainId;

            _chainId = chainId;
            _stepIndex = stepIndex;
            _onStepClick = onStepClick;
            EnsureInfoButton();
            if (_infoButton != null)
            {
                _infoButton.onClick.RemoveListener(HandleSlotClick);
                if (_onStepClick != null)
                    _infoButton.onClick.AddListener(HandleSlotClick);
                _infoButton.interactable = _onStepClick != null;
            }
        }

        /// <summary>
        /// Повторная установка value после того, как ScrollRect/Layout успеют рассчитать размеры трека.
        /// </summary>
        private void ScheduleProgressSliderLayoutRefresh(float valueClamped, float maxClamped)
        {
            if (progressSlider == null)
                return;
            if (_sliderLayoutRefreshCo != null)
                StopCoroutine(_sliderLayoutRefreshCo);
            _sliderLayoutRefreshCo = StartCoroutine(CoProgressSliderAfterLayout(valueClamped, maxClamped));
        }

        private IEnumerator CoProgressSliderAfterLayout(float valueClamped, float maxClamped)
        {
            yield return null;
            yield return null;

            _sliderLayoutRefreshCo = null;

            var s = progressSlider;
            if (s == null || !s.isActiveAndEnabled)
                yield break;

            var slotRt = transform as RectTransform;
            if (slotRt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(slotRt);

            if (s.fillRect != null)
            {
                var fillParent = s.fillRect.parent as RectTransform;
                if (fillParent != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(fillParent);
                LayoutRebuilder.ForceRebuildLayoutImmediate(s.fillRect);
            }

            var rt = s.transform as RectTransform;
            if (rt != null)
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

            Canvas.ForceUpdateCanvases();

            // На всякий случай ещё раз фиксируем fill вручную, без зависимости от Slider.UpdateVisuals.
            var den = Mathf.Max(1f, maxClamped);
            ApplyProgressFillManual(Mathf.Clamp01((valueClamped / den)));

            s.value = valueClamped;
            Canvas.ForceUpdateCanvases();
        }

        private void ApplyProgressFillManual(float t01)
        {
            var s = progressSlider;
            if (s == null || s.fillRect == null)
                return;

            // Делаем fillRect полностью растянутым по треку и масштабируем по X с pivot слева.
            // Это самый надёжный способ: не зависит от внутреннего UpdateVisuals Slider’а и любых лагов layout’а.
            var fr = s.fillRect;
            fr.anchorMin = new Vector2(0f, 0f);
            fr.anchorMax = new Vector2(1f, 1f);
            fr.offsetMin = Vector2.zero;
            fr.offsetMax = Vector2.zero;
            fr.pivot = new Vector2(0f, 0.5f);
            fr.anchoredPosition = Vector2.zero;

            var sc = fr.localScale;
            sc.x = Mathf.Clamp01(t01);
            sc.y = 1f;
            sc.z = 1f;
            fr.localScale = sc;
        }

        private void HandleSlotClick()
        {
            _onStepClick?.Invoke(_chainId, _stepIndex);
        }

        private void EnsureInfoButton()
        {
            if (_infoButton != null)
                return;
            _infoButton = GetComponent<Button>();
            if (_infoButton == null)
                _infoButton = gameObject.AddComponent<Button>();
            _infoButton.transition = Selectable.Transition.None;
            if (frameImage != null)
            {
                frameImage.raycastTarget = true;
                _infoButton.targetGraphic = frameImage;
            }
        }

        private void SetClaimHintActive(bool active)
        {
            if (_claimHintActive == active)
                return;

            _claimHintActive = active;
            if (!_claimHintActive)
            {
                if (_claimHintRoot != null)
                    _claimHintRoot.gameObject.SetActive(false);
                return;
            }

            EnsureClaimHintBuilt();
            if (_claimHintRoot == null)
                return;

            _claimHintRoot.gameObject.SetActive(true);
            UpdateClaimHintVisuals();
        }

        private void EnsureClaimHintBuilt()
        {
            if (_claimHintRoot != null)
                return;

            var hostRt = transform as RectTransform;
            if (hostRt == null)
                return;

            var go = new GameObject("UnclaimedHint", typeof(RectTransform));
            go.transform.SetParent(transform, false);
            _claimHintRoot = go.GetComponent<RectTransform>();
            _claimHintRoot.anchorMin = Vector2.zero;
            _claimHintRoot.anchorMax = Vector2.one;
            _claimHintRoot.offsetMin = Vector2.zero;
            _claimHintRoot.offsetMax = Vector2.zero;
            _claimHintRoot.pivot = new Vector2(0.5f, 0.5f);
            go.SetActive(false);

            _claimDotA = CreateHintDot(_claimHintRoot, "DotA");
            _claimDotB = CreateHintDot(_claimHintRoot, "DotB");
            _claimTrailA = FindChildRt(_claimDotA, "Trail");
            _claimTrailB = FindChildRt(_claimDotB, "Trail");

            // Make sure it renders above everything.
            _claimHintRoot.SetAsLastSibling();
        }

        private RectTransform CreateHintDot(RectTransform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = Vector2.zero;

            var trailGo = new GameObject("Trail", typeof(RectTransform), typeof(Image));
            trailGo.transform.SetParent(go.transform, false);
            var trailRt = trailGo.GetComponent<RectTransform>();
            trailRt.anchorMin = new Vector2(0.5f, 0.5f);
            trailRt.anchorMax = new Vector2(0.5f, 0.5f);
            trailRt.pivot = new Vector2(1f, 0.5f); // tail extends backwards along -X of dot
            trailRt.anchoredPosition = Vector2.zero;
            trailRt.sizeDelta = new Vector2(unclaimedHintTrailLength, unclaimedHintTrailThickness);
            var trailImg = trailGo.GetComponent<Image>();
            trailImg.raycastTarget = false;
            trailImg.color = unclaimedHintTrailColor;
            trailImg.sprite = GetRuntimeTrailGradientSprite();
            trailImg.type = Image.Type.Simple;

            var headGo = new GameObject("Head", typeof(RectTransform), typeof(Image));
            headGo.transform.SetParent(go.transform, false);
            var headRt = headGo.GetComponent<RectTransform>();
            headRt.anchorMin = new Vector2(0.5f, 0.5f);
            headRt.anchorMax = new Vector2(0.5f, 0.5f);
            headRt.pivot = new Vector2(0.5f, 0.5f);
            headRt.anchoredPosition = Vector2.zero;
            headRt.sizeDelta = new Vector2(unclaimedHintDotSize, unclaimedHintDotSize);
            var headImg = headGo.GetComponent<Image>();
            headImg.raycastTarget = false;
            headImg.color = unclaimedHintColor;
            headImg.sprite = unclaimedHintHeadSprite != null ? unclaimedHintHeadSprite : GetRuntimeTriangleSprite();
            headImg.type = Image.Type.Simple;

            return rt;
        }

        private static Sprite GetRuntimeWhiteSprite()
        {
            if (_runtimeWhiteSprite != null)
                return _runtimeWhiteSprite;

            var tex = Texture2D.whiteTexture;
            if (tex == null)
                return null;

            _runtimeWhiteSprite = Sprite.Create(
                tex,
                new Rect(0, 0, tex.width, tex.height),
                new Vector2(0.5f, 0.5f),
                100f);

            return _runtimeWhiteSprite;
        }

        private static Sprite GetRuntimeTriangleSprite()
        {
            if (_runtimeTriangleSprite != null)
                return _runtimeTriangleSprite;

            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            // Right-pointing triangle in texture space.
            for (var y = 0; y < size; y++)
            {
                var fy = (y + 0.5f) / size; // 0..1
                var half = Mathf.Abs(fy - 0.5f) * 2f; // 0 center, 1 edges
                var xMin = Mathf.RoundToInt(half * (size * 0.15f));
                var xMax = size - 1;
                for (var x = 0; x < size; x++)
                {
                    var inside = x >= xMin && x <= xMax;
                    tex.SetPixel(x, y, inside ? Color.white : new Color(1, 1, 1, 0));
                }
            }
            tex.Apply(false, true);

            _runtimeTriangleSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _runtimeTriangleSprite;
        }

        private static Sprite GetRuntimeTrailGradientSprite()
        {
            if (_runtimeTrailGradientSprite != null)
                return _runtimeTrailGradientSprite;

            // Horizontal alpha gradient: left (tail) = 0, right (near head) = 1.
            const int w = 64;
            const int h = 4;
            var tex = new Texture2D(w, h, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            for (var y = 0; y < h; y++)
            {
                // Slight vertical softness.
                var vy = 1f - Mathf.Abs((y + 0.5f) / h - 0.5f) * 2f; // 0..1
                vy = Mathf.SmoothStep(0.0f, 1.0f, vy);
                for (var x = 0; x < w; x++)
                {
                    var fx = (x + 0.5f) / w; // 0..1
                    var a = Mathf.SmoothStep(0f, 1f, fx) * vy;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply(false, true);

            _runtimeTrailGradientSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _runtimeTrailGradientSprite;
        }

        private static Sprite GetRuntimeOuterGlowSprite()
        {
            if (_runtimeOuterGlowSprite != null)
                return _runtimeOuterGlowSprite;

            // Procedural OUTER glow:
            // - Center is transparent (so it doesn't look like a scaled copy of Frame).
            // - Glow exists only outside an inner rect, fading outward.
            const int size = 192;
            const int innerMargin = 58;
            const float falloffPx = 30f;

            var tex = new Texture2D(size, size, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var x0 = innerMargin;
            var y0 = innerMargin;
            var x1 = size - 1 - innerMargin;
            var y1 = size - 1 - innerMargin;

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // Inside inner rect: fully transparent.
                    if (x >= x0 && x <= x1 && y >= y0 && y <= y1)
                    {
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, 0f));
                        continue;
                    }

                    var dx = 0f;
                    if (x < x0) dx = x0 - x;
                    else if (x > x1) dx = x - x1;

                    var dy = 0f;
                    if (y < y0) dy = y0 - y;
                    else if (y > y1) dy = y - y1;

                    var d = Mathf.Sqrt(dx * dx + dy * dy);
                    var t = Mathf.Clamp01(1f - (d / falloffPx));
                    // "Neon" profile: sharper near border, softer outside.
                    t = t * t;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, t));
                }
            }

            tex.Apply(false, true);
            _runtimeOuterGlowSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
            return _runtimeOuterGlowSprite;
        }

        private void Update()
        {
            if (!_claimHintActive || _claimHintRoot == null || _claimDotA == null || _claimDotB == null)
                return;

            var host = transform as RectTransform;
            if (host == null)
                return;

            var w = Mathf.Max(1f, host.rect.width - unclaimedHintInset * 2f);
            var h = Mathf.Max(1f, host.rect.height - unclaimedHintInset * 2f);
            var per = 2f * (w + h);
            if (per <= 0.01f)
                return;

            var t = (Time.unscaledTime * unclaimedHintSpeedPxPerSecond) % per;
            var t2 = (t + per * 0.5f) % per;
            SetDotPose(_claimDotA, w, h, t);
            SetDotPose(_claimDotB, w, h, t2);

            TickGlow();
        }

        private void UpdateClaimHintVisuals()
        {
            if (_claimHintRoot == null)
                return;
            foreach (var img in _claimHintRoot.GetComponentsInChildren<Image>(true))
            {
                if (img == null) continue;
                if (img.gameObject.name == "Trail")
                    img.color = unclaimedHintTrailColor;
                else if (img.gameObject.name == "Head")
                    img.color = unclaimedHintColor;
            }

            ApplyDotSizes(_claimDotA);
            ApplyDotSizes(_claimDotB);
        }

        private void SetGlowActive(bool active)
        {
            if (!active)
            {
                if (_glowRt != null) _glowRt.gameObject.SetActive(false);
                return;
            }

            EnsureGlowBuilt();
            if (_glowRt == null) return;
            _glowRt.gameObject.SetActive(true);
            ApplyGlowVisuals();
        }

        private void EnsureGlowBuilt()
        {
            if (_glowRt != null)
                return;

            var go = new GameObject("UnclaimedGlow", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(transform, false);
            _glowRt = go.GetComponent<RectTransform>();
            _glowRt.anchorMin = Vector2.zero;
            _glowRt.anchorMax = Vector2.one;
            _glowRt.pivot = new Vector2(0.5f, 0.5f);
            _glowRt.localScale = Vector3.one;
            _glowRt.localPosition = new Vector3(0f, 0f, unclaimedGlowZOffset);

            _glowImg = go.GetComponent<Image>();
            _glowImg.raycastTarget = false;
            _glowImg.sprite = GetRuntimeOuterGlowSprite();
            _glowImg.type = Image.Type.Simple;
            _glowImg.preserveAspect = false;

            // Put it behind frame (but still above background).
            _glowRt.SetAsFirstSibling();
            if (frameImage != null)
                _glowRt.SetSiblingIndex(Mathf.Max(0, frameImage.transform.GetSiblingIndex()));
        }

        private void ApplyGlowVisuals()
        {
            if (_glowRt == null || _glowImg == null) return;
            var ox = Mathf.Max(0f, unclaimedGlowOutsetHorizontal);
            var oy = Mathf.Max(0f, unclaimedGlowOutsetVertical);
            _glowRt.offsetMin = new Vector2(-ox, -oy);
            _glowRt.offsetMax = new Vector2(ox, oy);

            var c = unclaimedGlowColor;
            c.a = Mathf.Clamp01(c.a);
            _glowImg.color = c;
        }

        private void TickGlow()
        {
            if (_glowImg == null || _glowRt == null || !_glowRt.gameObject.activeSelf)
                return;

            var t = Time.unscaledTime * Mathf.Max(0.01f, unclaimedGlowPulseSpeed);
            var k = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f);
            // A bit snappier “neon” pulse.
            k = k * k * (3f - 2f * k);

            var a = Mathf.Lerp(unclaimedGlowMinAlpha, unclaimedGlowMaxAlpha, k);
            var c = unclaimedGlowColor;
            c.a = a;
            _glowImg.color = c;
        }

        /// <summary>
        /// Движение против часовой: левый верх → левый низ → правый низ → правый верх → левый верх.
        /// Координаты — в локальном пространстве хоста, центр в (0,0).
        /// </summary>
        private void ApplyDotSizes(RectTransform dot)
        {
            if (dot == null) return;
            var head = FindChildRt(dot, "Head");
            var trail = FindChildRt(dot, "Trail");
            if (head != null) head.sizeDelta = new Vector2(unclaimedHintDotSize, unclaimedHintDotSize);
            if (trail != null) trail.sizeDelta = new Vector2(unclaimedHintTrailLength, unclaimedHintTrailThickness);
        }

        private static RectTransform FindChildRt(RectTransform parent, string name)
        {
            if (parent == null) return null;
            var tr = parent.Find(name);
            return tr as RectTransform;
        }

        private void SetDotPose(RectTransform dot, float w, float h, float dist)
        {
            var hw = w * 0.5f;
            var hh = h * 0.5f;

            var x = -hw;
            var y = hh;
            var d = dist;
            var dir = Vector2.down;

            // Left edge вниз
            var seg = h;
            if (d <= seg)
            {
                y = hh - d;
                dir = Vector2.down;
                ApplyPose(dot, x, y, dir);
                return;
            }
            d -= seg;

            // Bottom edge вправо
            seg = w;
            y = -hh;
            if (d <= seg)
            {
                x = -hw + d;
                dir = Vector2.right;
                ApplyPose(dot, x, y, dir);
                return;
            }
            d -= seg;

            // Right edge вверх
            seg = h;
            x = hw;
            if (d <= seg)
            {
                y = -hh + d;
                dir = Vector2.up;
                ApplyPose(dot, x, y, dir);
                return;
            }
            d -= seg;

            // Top edge влево
            y = hh;
            x = hw - Mathf.Min(w, d);
            dir = Vector2.left;
            ApplyPose(dot, x, y, dir);
        }

        private static void ApplyPose(RectTransform dot, float x, float y, Vector2 dir)
        {
            if (dot == null) return;
            dot.anchoredPosition = new Vector2(x, y);
            var a = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
            dot.localRotation = Quaternion.Euler(0f, 0f, a);
        }
    }
}
