using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Top HUD: whose turn it is and the countdown timer.</summary>
    public sealed class Match3GameHUD : MonoBehaviour
    {
        [SerializeField] public TMP_Text turnText;
        [SerializeField] public TMP_Text timerText;
        [Tooltip("Подпись под таймером: «Время на решение» / «Анимация хода»")]
        [SerializeField] public TMP_Text timerPhaseText;
        [SerializeField] public TMP_Text extraTurnText;
        [Tooltip("«Спуск»: баннер last-turn наверху экрана")]
        [SerializeField] public TMP_Text raceLastTurnText;
        [Tooltip("«Спуск»: бонус маны чуть выше секундомера")]
        [SerializeField] public TMP_Text raceManaBonusText;
        [Tooltip("«Спуск»: постоянная подпись цели маны")]
        [SerializeField] public TMP_Text raceGoalText;

        [SerializeField] public Button affixButton;
        [SerializeField] public Image affixButtonIconImage;
        [SerializeField] public TMP_Text affixButtonLabel;
        [SerializeField] public RectTransform affixTooltipRoot;
        [SerializeField] public TMP_Text affixTooltipText;
        [SerializeField] public Button affixTooltipCloseButton;

        [SerializeField] public TMP_Text affixIconText;   // Legacy.
        [SerializeField] public TMP_Text affixEffectText; // Legacy.

        private Coroutine _extraTurnRoutine;
        private Canvas _affixTooltipCanvas;

        private float _timerFontSizeBase = 26f;
        private Color _timerNeutralColor = Color.white;

        private static readonly Color TimerGreen = new Color(0.22f, 0.88f, 0.42f, 1f);
        private static readonly Color TimerOrange = new Color(1f, 0.52f, 0.12f, 1f);
        private static readonly Color TimerOrangeDeep = new Color(1f, 0.34f, 0.10f, 1f);
        private static readonly Color TimerRed = new Color(0.93f, 0.16f, 0.14f, 1f);

        private const float TimerStressScaleMax = 2.12f;

        private void Awake()
        {
            ResolveReferences();
            EnsureExtraTurnText();
            EnsureRaceBanners();
            EnsureAffixUi();
        }

        private void ResolveReferences()
        {
            turnText ??= transform.Find("TurnText")?.GetComponent<TMP_Text>();
            timerText ??= transform.Find("TimerText")?.GetComponent<TMP_Text>();
            timerPhaseText ??= transform.Find("TimerPhaseText")?.GetComponent<TMP_Text>();
            extraTurnText ??= transform.Find("ExtraTurnText")?.GetComponent<TMP_Text>();
            raceLastTurnText ??= transform.Find("RaceLastTurnText")?.GetComponent<TMP_Text>();
            raceManaBonusText ??= transform.Find("RaceManaBonusText")?.GetComponent<TMP_Text>();
            raceGoalText ??= transform.Find("RaceGoalText")?.GetComponent<TMP_Text>();

            affixButton ??= transform.Find("AffixButton")?.GetComponent<Button>();
            affixButtonIconImage ??= transform.Find("AffixButton/Icon")?.GetComponent<Image>();
            affixButtonLabel ??= transform.Find("AffixButton/Label")?.GetComponent<TMP_Text>();
            affixTooltipRoot ??= transform.Find("AffixTooltip") as RectTransform;
            affixTooltipText ??= transform.Find("AffixTooltip/Text")?.GetComponent<TMP_Text>();
            affixTooltipCloseButton ??= transform.Find("AffixTooltip/Close")?.GetComponent<Button>();

            affixIconText ??= transform.Find("AffixIconText")?.GetComponent<TMP_Text>();
            affixEffectText ??= transform.Find("AffixEffectText")?.GetComponent<TMP_Text>();

            CacheTimerStyleBaseline();
        }

        private void CacheTimerStyleBaseline()
        {
            if (timerText == null) return;
            _timerFontSizeBase = timerText.fontSize;
            _timerNeutralColor = timerText.color;
        }

        private void EnsureExtraTurnText()
        {
            if (extraTurnText != null) return;

            var go = new GameObject("ExtraTurnText");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0f, -0.35f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            extraTurnText = go.AddComponent<TextMeshProUGUI>();
            extraTurnText.font = TMP_Settings.defaultFontAsset;
            extraTurnText.fontSize = 22;
            extraTurnText.alignment = TextAlignmentOptions.Center;
            extraTurnText.text = string.Empty;
            extraTurnText.gameObject.SetActive(false);
        }

        private void EnsureRaceBanners()
        {
            if (raceLastTurnText == null)
            {
                var go = new GameObject("RaceLastTurnText", typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                // Наверх экрана относительно HUD-полосы.
                var parent = transform.parent != null ? transform.parent : transform;
                rt.SetParent(parent, false);
                rt.anchorMin = new Vector2(0.05f, 0.92f);
                rt.anchorMax = new Vector2(0.95f, 0.99f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                raceLastTurnText = go.AddComponent<TextMeshProUGUI>();
                raceLastTurnText.font = TMP_Settings.defaultFontAsset;
                raceLastTurnText.fontSize = 30;
                raceLastTurnText.fontStyle = FontStyles.Bold;
                raceLastTurnText.alignment = TextAlignmentOptions.Center;
                raceLastTurnText.color = new Color(1f, 0.82f, 0.28f, 1f);
                raceLastTurnText.outlineWidth = 0.22f;
                raceLastTurnText.outlineColor = new Color32(0, 0, 0, 220);
                raceLastTurnText.raycastTarget = false;
                raceLastTurnText.text = string.Empty;
                go.SetActive(false);
            }

            if (raceManaBonusText == null)
            {
                var go = new GameObject("RaceManaBonusText", typeof(RectTransform));
                var rt = go.GetComponent<RectTransform>();
                var parent = timerText != null && timerText.transform.parent != null
                    ? timerText.transform.parent
                    : transform;
                rt.SetParent(parent, false);
                ApplyRaceManaBonusLayout(rt);

                raceManaBonusText = go.AddComponent<TextMeshProUGUI>();
                raceManaBonusText.font = TMP_Settings.defaultFontAsset;
                raceManaBonusText.fontStyle = FontStyles.Bold;
                raceManaBonusText.alignment = TextAlignmentOptions.Center;
                raceManaBonusText.color = new Color(0.45f, 0.9f, 1f, 1f);
                raceManaBonusText.outlineWidth = 0.18f;
                raceManaBonusText.outlineColor = new Color32(0, 0, 0, 210);
                raceManaBonusText.raycastTarget = false;
                raceManaBonusText.enableAutoSizing = true;
                raceManaBonusText.fontSizeMin = 18f;
                raceManaBonusText.fontSizeMax = 72f;
                raceManaBonusText.text = string.Empty;
                go.SetActive(false);
            }
            else
            {
                ApplyRaceManaBonusLayout(raceManaBonusText.rectTransform);
                raceManaBonusText.enableAutoSizing = true;
                raceManaBonusText.fontSizeMin = 18f;
                raceManaBonusText.fontSizeMax = 72f;
                raceManaBonusText.alignment = TextAlignmentOptions.Center;
            }

            if (raceGoalText == null)
                raceGoalText = FindOrCreateRaceGoalText();

            ApplyRaceGoalTextLayout(raceGoalText);
        }

        public void SetRaceGoalBanner(bool show, string text)
        {
            EnsureRaceBanners();
            if (raceGoalText == null) return;
            ApplyRaceGoalTextLayout(raceGoalText);
            if (!show || string.IsNullOrWhiteSpace(text))
            {
                raceGoalText.text = string.Empty;
                raceGoalText.gameObject.SetActive(false);
                return;
            }
            raceGoalText.text = text;
            raceGoalText.gameObject.SetActive(true);
        }

        private TMP_Text FindOrCreateRaceGoalText()
        {
            var canvasTr = ResolveMatchCanvasTransform();
            if (canvasTr == null) return null;

            // Уже на нужном Canvas.
            var existing = canvasTr.Find("RaceGoalText");
            // Старый баг: мог оказаться в корне сцены или под BoardCol.
            if (existing == null && transform.parent != null)
                existing = transform.parent.Find("RaceGoalText");
            if (existing == null)
            {
                var scene = gameObject.scene;
                if (scene.IsValid())
                {
                    foreach (var root in scene.GetRootGameObjects())
                    {
                        if (root != null && root.name == "RaceGoalText")
                        {
                            existing = root.transform;
                            break;
                        }
                    }
                }
            }
            if (existing == null)
            {
                foreach (var t in FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t != null && t.name == "RaceGoalText" && t.GetComponent<TMP_Text>() != null)
                    {
                        existing = t;
                        break;
                    }
                }
            }

            TMP_Text label = existing != null ? existing.GetComponent<TMP_Text>() : null;
            if (label == null)
            {
                var go = new GameObject("RaceGoalText", typeof(RectTransform));
                go.transform.SetParent(canvasTr, false);
                label = go.AddComponent<TextMeshProUGUI>();
                label.font = TMP_Settings.defaultFontAsset;
                label.fontSize = 40;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.color = new Color(0.92f, 0.95f, 1f, 1f);
                label.outlineWidth = 0.18f;
                label.outlineColor = new Color32(0, 0, 0, 210);
                label.raycastTarget = false;
                label.text = string.Empty;
                go.SetActive(false);
            }

            return label;
        }

        private Transform ResolveMatchCanvasTransform()
        {
            // Нужен Canvas под DuelMatch3Manager (с Bg), не Overlay-canvas инвайтов и т.п.
            var canvas = GetComponentInParent<Canvas>();
            if (canvas != null && canvas.transform.Find("Bg") != null)
                return canvas.transform;

            var t = transform;
            while (t != null)
            {
                var c = t.GetComponent<Canvas>();
                if (c != null && t.Find("Bg") != null)
                    return t;
                t = t.parent;
            }

            // Fallback: любой Canvas с Bg в сцене матча.
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (c != null && c.transform.Find("Bg") != null)
                    return c.transform;
            }

            return canvas != null ? canvas.transform : null;
        }

        /// <summary>
        /// DuelMatch3Manager/Canvas → сразу под Bg (второй child); top-stretch, Pos Y = -50.
        /// </summary>
        private void ApplyRaceGoalTextLayout(TMP_Text label)
        {
            if (label == null) return;
            var rt = label.rectTransform;
            var canvasTr = ResolveMatchCanvasTransform();
            if (canvasTr == null) return;

            if (rt.parent != canvasTr)
                rt.SetParent(canvasTr, false);

            var bg = canvasTr.Find("Bg");
            if (bg != null)
                rt.SetSiblingIndex(bg.GetSiblingIndex() + 1);
            else
                rt.SetSiblingIndex(Mathf.Min(1, canvasTr.childCount - 1));

            // top-stretch, Pos Y = -70
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -70f);
            rt.sizeDelta = new Vector2(0f, 48f);
            label.fontSize = 40;
            label.enableAutoSizing = false;
        }

        private static void ApplyRaceManaBonusLayout(RectTransform rt)
        {
            if (rt == null) return;
            // middle-stretch, height 50, Pos Y 100, left 10
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(10f, 100f);
            rt.sizeDelta = new Vector2(-10f, 50f);
        }

        public void SetRaceLastTurnBanner(bool show, string text)
        {
            EnsureRaceBanners();
            if (raceLastTurnText == null) return;
            if (!show || string.IsNullOrWhiteSpace(text))
            {
                raceLastTurnText.text = string.Empty;
                raceLastTurnText.gameObject.SetActive(false);
                return;
            }
            raceLastTurnText.text = text;
            raceLastTurnText.gameObject.SetActive(true);
            raceLastTurnText.transform.SetAsLastSibling();
        }

        public void SetRaceManaBonusBanner(bool show, string text)
        {
            EnsureRaceBanners();
            if (raceManaBonusText == null) return;
            if (!show || string.IsNullOrWhiteSpace(text))
            {
                raceManaBonusText.text = string.Empty;
                raceManaBonusText.gameObject.SetActive(false);
                return;
            }
            ApplyRaceManaBonusLayout(raceManaBonusText.rectTransform);
            raceManaBonusText.enableAutoSizing = true;
            raceManaBonusText.fontSizeMin = 18f;
            raceManaBonusText.fontSizeMax = 72f;
            raceManaBonusText.text = text;
            raceManaBonusText.gameObject.SetActive(true);
        }

        private void EnsureAffixUi()
        {
            if (affixIconText != null) affixIconText.gameObject.SetActive(false);
            if (affixEffectText != null) affixEffectText.gameObject.SetActive(false);

            if (affixButton == null)
            {
                var btnGo = new GameObject("AffixButton", typeof(RectTransform), typeof(Image), typeof(Button));
                var btnRt = btnGo.GetComponent<RectTransform>();
                btnRt.SetParent(transform, false);
                btnRt.anchorMin = new Vector2(0.82f, 0.14f);
                btnRt.anchorMax = new Vector2(0.90f, 0.86f);
                btnRt.offsetMin = Vector2.zero;
                btnRt.offsetMax = Vector2.zero;
                var bg = btnGo.GetComponent<Image>();
                bg.color = new Color(0.14f, 0.17f, 0.24f, 0.96f);
                affixButton = btnGo.GetComponent<Button>();
            }

            if (affixButtonIconImage == null)
            {
                var iconGo = new GameObject("Icon", typeof(RectTransform), typeof(Image));
                var iconRt = iconGo.GetComponent<RectTransform>();
                iconRt.SetParent(affixButton.transform, false);
                iconRt.anchorMin = new Vector2(0.10f, 0.10f);
                iconRt.anchorMax = new Vector2(0.90f, 0.90f);
                iconRt.offsetMin = Vector2.zero;
                iconRt.offsetMax = Vector2.zero;
                affixButtonIconImage = iconGo.GetComponent<Image>();
                affixButtonIconImage.preserveAspect = true;
                affixButtonIconImage.raycastTarget = false;
                affixButtonIconImage.color = new Color(1f, 1f, 1f, 0f);
            }

            if (affixButtonLabel == null)
            {
                var txtGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var txtRt = txtGo.GetComponent<RectTransform>();
                txtRt.SetParent(affixButton.transform, false);
                txtRt.anchorMin = Vector2.zero;
                txtRt.anchorMax = Vector2.one;
                txtRt.offsetMin = Vector2.zero;
                txtRt.offsetMax = Vector2.zero;
                affixButtonLabel = txtGo.GetComponent<TextMeshProUGUI>();
                affixButtonLabel.font = TMP_Settings.defaultFontAsset;
                affixButtonLabel.fontSize = 20;
                affixButtonLabel.alignment = TextAlignmentOptions.Center;
                affixButtonLabel.text = string.Empty;
                affixButtonLabel.raycastTarget = false;
            }

            if (affixTooltipRoot == null)
            {
                var tipGo = new GameObject("AffixTooltip", typeof(RectTransform), typeof(Image));
                affixTooltipRoot = tipGo.GetComponent<RectTransform>();
                affixTooltipRoot.SetParent(transform, false);
                ApplyAffixTooltipTopStretch(affixTooltipRoot, height: 120f);
                tipGo.GetComponent<Image>().color = new Color(0x3e / 255f, 0x73 / 255f, 0xdd / 255f, 0.96f);
            }

            if (affixTooltipText == null)
            {
                var txtGo = new GameObject("Text", typeof(RectTransform), typeof(TextMeshProUGUI));
                var txtRt = txtGo.GetComponent<RectTransform>();
                txtRt.SetParent(affixTooltipRoot, false);
                txtRt.anchorMin = new Vector2(0.06f, 0.06f);
                txtRt.anchorMax = new Vector2(0.88f, 0.94f);
                txtRt.offsetMin = Vector2.zero;
                txtRt.offsetMax = Vector2.zero;
                affixTooltipText = txtGo.GetComponent<TextMeshProUGUI>();
                affixTooltipText.font = TMP_Settings.defaultFontAsset;
                affixTooltipText.fontSize = 30;
                affixTooltipText.alignment = TextAlignmentOptions.TopLeft;
                affixTooltipText.text = string.Empty;
                affixTooltipText.textWrappingMode = TextWrappingModes.Normal;
                affixTooltipText.overflowMode = TextOverflowModes.Overflow;
            }

            if (affixTooltipCloseButton == null)
            {
                var closeGo = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
                var closeRt = closeGo.GetComponent<RectTransform>();
                closeRt.SetParent(affixTooltipRoot, false);
                closeRt.anchorMin = new Vector2(0.90f, 0.82f);
                closeRt.anchorMax = new Vector2(0.98f, 0.98f);
                closeRt.offsetMin = Vector2.zero;
                closeRt.offsetMax = Vector2.zero;
                var closeImg = closeGo.GetComponent<Image>();
                closeImg.color = new Color(0.45f, 0.18f, 0.18f, 0.96f);
                closeImg.raycastTarget = true;
                affixTooltipCloseButton = closeGo.GetComponent<Button>();
                affixTooltipCloseButton.targetGraphic = closeImg;

                var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
                var labelRt = labelGo.GetComponent<RectTransform>();
                labelRt.SetParent(closeGo.transform, false);
                labelRt.anchorMin = Vector2.zero;
                labelRt.anchorMax = Vector2.one;
                labelRt.offsetMin = Vector2.zero;
                labelRt.offsetMax = Vector2.zero;
                var label = labelGo.GetComponent<TextMeshProUGUI>();
                label.font = TMP_Settings.defaultFontAsset;
                label.fontSize = 16;
                label.alignment = TextAlignmentOptions.Center;
                label.text = "x";
                label.raycastTarget = false;
            }

            ApplyAffixTooltipChrome();
            EnsureAffixTooltipOnTop();

            affixTooltipRoot.gameObject.SetActive(false);
            affixButton.onClick.RemoveAllListeners();
            affixButton.onClick.AddListener(OnAffixButtonClicked);
            WireAffixTooltipCloseButton();
            affixButton.gameObject.SetActive(false);
        }

        private void OnAffixButtonClicked()
        {
            if (affixTooltipRoot == null) return;
            var show = !affixTooltipRoot.gameObject.activeSelf;
            affixTooltipRoot.gameObject.SetActive(show);
            if (show)
            {
                EnsureAffixTooltipOnTop();
                LayoutAffixTooltip();
            }
        }

        private void EnsureAffixTooltipOnTop()
        {
            if (affixTooltipRoot == null) return;
            affixTooltipRoot.SetAsLastSibling();
            if (_affixTooltipCanvas == null)
                _affixTooltipCanvas = affixTooltipRoot.GetComponent<Canvas>();
            if (_affixTooltipCanvas == null)
            {
                _affixTooltipCanvas = affixTooltipRoot.gameObject.AddComponent<Canvas>();
                _affixTooltipCanvas.overrideSorting = true;
                _affixTooltipCanvas.sortingOrder = 32000;
            }
            else
            {
                _affixTooltipCanvas.overrideSorting = true;
                _affixTooltipCanvas.sortingOrder = 32000;
            }
            if (affixTooltipRoot.GetComponent<GraphicRaycaster>() == null)
                affixTooltipRoot.gameObject.AddComponent<GraphicRaycaster>();
        }

        private void WireAffixTooltipCloseButton()
        {
            if (affixTooltipCloseButton == null) return;
            affixTooltipCloseButton.onClick.RemoveAllListeners();
            affixTooltipCloseButton.onClick.AddListener(CloseAffixTooltip);
        }

        private void CloseAffixTooltip()
        {
            if (affixTooltipRoot != null)
                affixTooltipRoot.gameObject.SetActive(false);
        }

        /// <summary>Unity preset Top-Stretch: anchors (0,1)-(1,1), Left/Right insets, Height.</summary>
        private static void ApplyAffixTooltipTopStretch(RectTransform rt, float height, float left = 16f, float right = 16f, float top = 0f)
        {
            if (rt == null) return;
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(-(left + right), Mathf.Max(1f, height));
            rt.anchoredPosition = new Vector2((left - right) * 0.5f, -top);
        }

        private void LayoutAffixTooltip()
        {
            if (affixTooltipRoot == null || affixTooltipText == null) return;

            var parent = affixTooltipRoot.parent as RectTransform;
            if (parent == null) return;

            Canvas.ForceUpdateCanvases();

            const float padL = 10f;
            const float padR = 10f;
            const float padTop = 6f;
            const float padBottom = 10f;
            const float closeReserve = 40f;
            const float panelLeft = 16f;
            const float panelRight = 16f;
            const float panelTop = 0f;

            float parentW = Mathf.Max(1f, parent.rect.width);
            float panelW = Mathf.Max(40f, parentW - panelLeft - panelRight);
            float maxTextW = Mathf.Max(40f, panelW - padL - padR);

            affixTooltipText.fontSize = 30;
            var pref = affixTooltipText.GetPreferredValues(affixTooltipText.text, maxTextW, 0f);
            float textH = Mathf.Max(pref.y, 30f);
            float totalH = padTop + closeReserve + textH + padBottom;

            // Только высота под текст; якоря всегда Top-Stretch (не top-custom).
            ApplyAffixTooltipTopStretch(affixTooltipRoot, totalH, panelLeft, panelRight, panelTop);

            var textRt = affixTooltipText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(padL, padBottom);
            textRt.offsetMax = new Vector2(-padR, -(padTop + closeReserve));

            if (affixTooltipCloseButton != null)
            {
                var closeRt = affixTooltipCloseButton.GetComponent<RectTransform>();
                closeRt.anchorMin = new Vector2(1f, 1f);
                closeRt.anchorMax = new Vector2(1f, 1f);
                closeRt.pivot = new Vector2(1f, 1f);
                closeRt.sizeDelta = new Vector2(36f, 32f);
                closeRt.anchoredPosition = new Vector2(-6f, -6f);
                affixTooltipCloseButton.transform.SetAsLastSibling();
            }
        }

        private void ApplyAffixTooltipChrome()
        {
            if (affixTooltipRoot == null) return;

            if (affixTooltipCloseButton == null)
            {
                foreach (var b in affixTooltipRoot.GetComponentsInChildren<Button>(true))
                {
                    if (b != null && string.Equals(b.gameObject.name, "Close", StringComparison.OrdinalIgnoreCase))
                    {
                        affixTooltipCloseButton = b;
                        break;
                    }
                }
            }

            var bg = affixTooltipRoot.GetComponent<Image>();
            if (bg != null)
            {
                bg.color = new Color(0x3e / 255f, 0x73 / 255f, 0xdd / 255f, 0.96f);
                bg.raycastTarget = true;
            }

            if (affixTooltipText != null)
            {
                affixTooltipText.fontSize = 30;
                affixTooltipText.raycastTarget = false;
                affixTooltipText.textWrappingMode = TextWrappingModes.Normal;
                affixTooltipText.overflowMode = TextOverflowModes.Overflow;
            }

            if (affixTooltipCloseButton != null)
            {
                affixTooltipCloseButton.interactable = true;
                var closeImg = affixTooltipCloseButton.GetComponent<Image>();
                if (closeImg != null)
                {
                    closeImg.raycastTarget = true;
                    affixTooltipCloseButton.targetGraphic = closeImg;
                }
                var cg = affixTooltipCloseButton.GetComponent<CanvasGroup>();
                if (cg != null)
                {
                    cg.blocksRaycasts = true;
                    cg.interactable = true;
                }
                affixTooltipCloseButton.transform.SetAsLastSibling();
            }
        }

        public void SetTurn(string text)
        {
            if (turnText != null) turnText.text = text;
        }

        /// <param name="remainingSeconds">
        /// Секунды до конца хода для градиента и лёгкого увеличения шрифта; &lt; 0 — нейтральный цвет/размер (пусто, «—», нет дедлайна).
        /// </param>
        public void SetTimer(string text, float remainingSeconds = -1f)
        {
            if (timerText == null) return;
            timerText.text = text;
            ApplyTimerCountdownStyle(text, remainingSeconds);
        }

        private void ApplyTimerCountdownStyle(string text, float remainingSeconds)
        {
            if (timerText == null) return;

            var neutral = string.IsNullOrEmpty(text) || text == "—" || remainingSeconds < 0f;
            if (neutral)
            {
                timerText.color = _timerNeutralColor;
                timerText.fontSize = _timerFontSizeBase;
                return;
            }

            var t = Mathf.Max(0f, remainingSeconds);
            Color c;
            if (t >= 30f)
                c = TimerGreen;
            else if (t >= 20f)
            {
                var k = (t - 20f) / 10f;
                c = Color.Lerp(TimerOrange, TimerGreen, k);
            }
            else if (t >= 10f)
            {
                var k = (t - 10f) / 10f;
                c = Color.Lerp(TimerOrangeDeep, TimerOrange, k);
            }
            else
            {
                var k = t / 10f;
                c = Color.Lerp(TimerRed, TimerOrangeDeep, k);
            }

            timerText.color = c;

            if (t < 10f)
            {
                var sizeMul = Mathf.Lerp(TimerStressScaleMax, 1f, t / 10f);
                timerText.fontSize = _timerFontSizeBase * sizeMul;
            }
            else
                timerText.fontSize = _timerFontSizeBase;
        }

        /// <param name="isMoveResolving">true — идёт откат чужого/своего хода (каскады); false — идёт отсчёт времени на решение.</param>
        public void SetTimerPhase(bool isMoveResolving)
        {
            if (timerPhaseText == null) return;
            timerPhaseText.text = isMoveResolving ? "Анимация хода" : "Время на решение";
        }

        public void ShowExtraTurnMessage(string message, Color color, float duration)
        {
            if (extraTurnText == null) return;
            if (_extraTurnRoutine != null) StopCoroutine(_extraTurnRoutine);
            _extraTurnRoutine = StartCoroutine(ShowExtraTurnRoutine(message, color, duration));
        }

        public void SetAffixInfo(string iconText, string effectText, Sprite iconSprite = null)
        {
            var has = !string.IsNullOrWhiteSpace(effectText);
            var hasIcon = iconSprite != null;
            if (affixButton != null)
                affixButton.gameObject.SetActive(has);
            if (affixTooltipRoot != null && !has)
                affixTooltipRoot.gameObject.SetActive(false);
            if (!has) return;

            if (affixTooltipText != null)
                affixTooltipText.text = effectText;
            EnsureAffixTooltipOnTop();
            LayoutAffixTooltip();
            WireAffixTooltipCloseButton();
            if (affixButtonLabel != null)
            {
                affixButtonLabel.gameObject.SetActive(!hasIcon);
                if (!hasIcon)
                    affixButtonLabel.text = string.IsNullOrWhiteSpace(iconText) ? "?" : iconText;
            }
            if (affixButtonIconImage != null)
            {
                affixButtonIconImage.sprite = iconSprite;
                affixButtonIconImage.color = hasIcon ? Color.white : new Color(1f, 1f, 1f, 0f);
            }
        }

        private IEnumerator ShowExtraTurnRoutine(string message, Color color, float duration)
        {
            extraTurnText.text = message;
            extraTurnText.color = color;
            extraTurnText.gameObject.SetActive(true);
            yield return new WaitForSeconds(Mathf.Max(0.2f, duration));
            extraTurnText.gameObject.SetActive(false);
            _extraTurnRoutine = null;
        }
    }
}
