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

        private void Awake()
        {
            ResolveReferences();
            EnsureExtraTurnText();
            EnsureAffixUi();
        }

        private void ResolveReferences()
        {
            turnText ??= transform.Find("TurnText")?.GetComponent<TMP_Text>();
            timerText ??= transform.Find("TimerText")?.GetComponent<TMP_Text>();
            timerPhaseText ??= transform.Find("TimerPhaseText")?.GetComponent<TMP_Text>();
            extraTurnText ??= transform.Find("ExtraTurnText")?.GetComponent<TMP_Text>();

            affixButton ??= transform.Find("AffixButton")?.GetComponent<Button>();
            affixButtonIconImage ??= transform.Find("AffixButton/Icon")?.GetComponent<Image>();
            affixButtonLabel ??= transform.Find("AffixButton/Label")?.GetComponent<TMP_Text>();
            affixTooltipRoot ??= transform.Find("AffixTooltip") as RectTransform;
            affixTooltipText ??= transform.Find("AffixTooltip/Text")?.GetComponent<TMP_Text>();
            affixTooltipCloseButton ??= transform.Find("AffixTooltip/Close")?.GetComponent<Button>();

            affixIconText ??= transform.Find("AffixIconText")?.GetComponent<TMP_Text>();
            affixEffectText ??= transform.Find("AffixEffectText")?.GetComponent<TMP_Text>();
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
                affixTooltipRoot.anchorMin = new Vector2(0.62f, 0.10f);
                affixTooltipRoot.anchorMax = new Vector2(0.98f, 0.90f);
                affixTooltipRoot.offsetMin = Vector2.zero;
                affixTooltipRoot.offsetMax = Vector2.zero;
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
                affixTooltipText.fontSize = 14;
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
            const float topInset = 56f;

            float parentW = parent.rect.width;
            float span = 0.98f - 0.62f;
            float panelW = span * parentW;
            float maxTextW = Mathf.Max(40f, panelW - padL - padR);

            var pref = affixTooltipText.GetPreferredValues(affixTooltipText.text, maxTextW, 0f);
            float textH = Mathf.Max(pref.y, 16f);
            float totalH = padTop + closeReserve + textH + padBottom;

            affixTooltipRoot.pivot = new Vector2(0.5f, 1f);
            affixTooltipRoot.anchorMin = new Vector2(0.62f, 1f);
            affixTooltipRoot.anchorMax = new Vector2(0.98f, 1f);
            affixTooltipRoot.sizeDelta = new Vector2(0f, totalH);
            affixTooltipRoot.anchoredPosition = new Vector2(0f, -topInset);

            var textRt = affixTooltipText.rectTransform;
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = new Vector2(padL, padBottom);
            textRt.offsetMax = new Vector2(-padR, -(padTop + closeReserve));

            if (affixTooltipCloseButton != null)
            {
                var closeRt = affixTooltipCloseButton.GetComponent<RectTransform>();
                closeRt.anchorMin = new Vector2(0.88f, 1f);
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

        public void SetTimer(string text)
        {
            if (timerText != null) timerText.text = text;
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
