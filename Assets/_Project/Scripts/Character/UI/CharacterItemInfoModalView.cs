using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class CharacterItemInfoModalView : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text slotText;
        [SerializeField] private TMP_Text descText;
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button equipToggleButton;
        [SerializeField] private TMP_Text equipToggleLabel;
        [SerializeField] private Button learnRecipeButton;
        [SerializeField] private TMP_Text learnRecipeLabel;
        [SerializeField] private Button salvageButton;

        [Header("Animation")]
        [SerializeField, Min(0.01f)] private float showDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float hideDuration = 0.14f;
        [SerializeField] private float startScale = 0.94f;

        private Coroutine _showRoutine;
        private Coroutine _hideRoutine;
        public float HideDuration => hideDuration;

        public RectTransform PanelRect => panel != null ? panel : transform as RectTransform;

        private void Awake()
        {
            EnsureDefaults();
            HideImmediate();
        }

        public void Bind(Action onClose, Action onEquipToggle, Action onSalvage, Action onLearnRecipe = null)
        {
            if (closeButton != null)
            {
                closeButton.onClick.RemoveAllListeners();
                if (onClose != null) closeButton.onClick.AddListener(() => onClose());
            }

            if (equipToggleButton != null)
            {
                equipToggleButton.onClick.RemoveAllListeners();
                if (onEquipToggle != null) equipToggleButton.onClick.AddListener(() => onEquipToggle());
            }

            if (learnRecipeButton != null)
            {
                learnRecipeButton.onClick.RemoveAllListeners();
                if (onLearnRecipe != null) learnRecipeButton.onClick.AddListener(() => onLearnRecipe());
            }

            if (salvageButton != null)
            {
                salvageButton.onClick.RemoveAllListeners();
                if (onSalvage != null) salvageButton.onClick.AddListener(() => onSalvage());
            }
        }

        public void ShowCentered()
        {
            EnsureDefaults();
            gameObject.SetActive(true);
            if (_showRoutine != null) StopCoroutine(_showRoutine);
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            _showRoutine = StartCoroutine(ShowRoutine());
        }

        public void HideAnimated()
        {
            if (!gameObject.activeSelf) return;
            if (_showRoutine != null) StopCoroutine(_showRoutine);
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            _hideRoutine = StartCoroutine(HideRoutine());
        }

        public void HideImmediate()
        {
            if (_showRoutine != null)
            {
                StopCoroutine(_showRoutine);
                _showRoutine = null;
            }
            if (_hideRoutine != null)
            {
                StopCoroutine(_hideRoutine);
                _hideRoutine = null;
            }

            if (panelCanvasGroup != null) panelCanvasGroup.alpha = 0f;
            if (PanelRect != null) PanelRect.localScale = Vector3.one * startScale;
            gameObject.SetActive(false);
        }

        public void SetTitle(string value, Color? color = null)
        {
            if (titleText == null) return;
            titleText.text = value ?? string.Empty;
            if (color.HasValue) titleText.color = color.Value;
        }

        public void SetSlot(string value)
        {
            if (slotText != null) slotText.text = value ?? string.Empty;
        }

        public void SetDescription(string value)
        {
            EnsureDescText();
            var has = !string.IsNullOrWhiteSpace(value);
            if (descText != null)
            {
                descText.gameObject.SetActive(has);
                descText.text = has ? value.Trim() : string.Empty;
            }
            AdjustStatsAnchorsForDesc(has);
        }

        public void SetStats(string value)
        {
            if (statsText != null) statsText.text = value ?? string.Empty;
        }

        public void SetEquipButton(bool visible, bool interactable, string text)
        {
            if (equipToggleButton != null) equipToggleButton.gameObject.SetActive(visible);
            if (equipToggleButton != null) equipToggleButton.interactable = interactable;
            if (equipToggleLabel != null) equipToggleLabel.text = text ?? string.Empty;
        }

        public void SetLearnRecipeButton(bool visible, bool interactable, string text)
        {
            if (learnRecipeButton != null) learnRecipeButton.gameObject.SetActive(visible);
            if (learnRecipeButton != null) learnRecipeButton.interactable = interactable;
            if (learnRecipeLabel != null) learnRecipeLabel.text = text ?? string.Empty;
            else if (learnRecipeButton != null)
            {
                var t = learnRecipeButton.GetComponentInChildren<TMP_Text>(true);
                if (t != null) t.text = text ?? string.Empty;
            }
        }

        public bool ContainsScreenPoint(Vector2 screenPoint, Camera eventCamera)
        {
            var rect = PanelRect;
            return rect != null && RectTransformUtility.RectangleContainsScreenPoint(rect, screenPoint, eventCamera);
        }

        private IEnumerator ShowRoutine()
        {
            var rect = PanelRect;
            if (panelCanvasGroup == null || rect == null)
            {
                yield break;
            }

            var t = 0f;
            panelCanvasGroup.alpha = 0f;
            rect.localScale = Vector3.one * startScale;
            while (t < showDuration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / showDuration);
                panelCanvasGroup.alpha = p;
                rect.localScale = Vector3.one * Mathf.Lerp(startScale, 1f, p);
                yield return null;
            }

            panelCanvasGroup.alpha = 1f;
            rect.localScale = Vector3.one;
            _showRoutine = null;
        }

        private IEnumerator HideRoutine()
        {
            var rect = PanelRect;
            if (panelCanvasGroup == null || rect == null)
            {
                gameObject.SetActive(false);
                yield break;
            }

            var t = 0f;
            var startAlpha = panelCanvasGroup.alpha;
            var startScaleValue = rect.localScale.x;
            while (t < hideDuration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / hideDuration);
                panelCanvasGroup.alpha = Mathf.Lerp(startAlpha, 0f, p);
                rect.localScale = Vector3.one * Mathf.Lerp(startScaleValue, startScale, p);
                yield return null;
            }

            panelCanvasGroup.alpha = 0f;
            rect.localScale = Vector3.one * startScale;
            gameObject.SetActive(false);
            _hideRoutine = null;
        }

        private void EnsureDefaults()
        {
            if (panel == null) panel = transform as RectTransform;
            if (panelCanvasGroup == null) panelCanvasGroup = GetComponent<CanvasGroup>() ?? gameObject.AddComponent<CanvasGroup>();
            if (titleText == null) titleText = transform.Find("Title")?.GetComponent<TMP_Text>();
            if (slotText == null) slotText = transform.Find("Slot")?.GetComponent<TMP_Text>();
            if (descText == null) descText = transform.Find("Desc")?.GetComponent<TMP_Text>();
            if (statsText == null) statsText = transform.Find("Stats")?.GetComponent<TMP_Text>();
            if (closeButton == null) closeButton = transform.Find("CloseButton")?.GetComponent<Button>();
            if (equipToggleButton == null) equipToggleButton = transform.Find("EquipButton")?.GetComponent<Button>();
            if (equipToggleLabel == null && equipToggleButton != null) equipToggleLabel = equipToggleButton.GetComponentInChildren<TMP_Text>(true);
            if (learnRecipeButton == null) learnRecipeButton = transform.Find("LearnRecipeButton")?.GetComponent<Button>();
            if (learnRecipeLabel == null && learnRecipeButton != null) learnRecipeLabel = learnRecipeButton.GetComponentInChildren<TMP_Text>(true);
            if (salvageButton == null) salvageButton = transform.Find("SalvageButton")?.GetComponent<Button>();

            EnsureLearnRecipeButtonFromPrefabIfMissing();
            EnsureDescText();

            EnsureButtonLabelRaycast(closeButton);
            EnsureButtonLabelRaycast(equipToggleButton);
            EnsureButtonLabelRaycast(learnRecipeButton);
            EnsureButtonLabelRaycast(salvageButton);
            EnsureButtonFx(closeButton);
            EnsureButtonFx(equipToggleButton);
            EnsureButtonFx(learnRecipeButton);
            EnsureButtonFx(salvageButton);
            if (descText != null) descText.richText = true;
            if (statsText != null) statsText.richText = true;

            if (titleText != null) titleText.fontStyle = FontStyles.Underline;
            if (descText != null) descText.fontStyle = FontStyles.Italic;
        }

        /// <summary>
        /// Старые префабы без Desc — создаём блок между Slot и Stats.
        /// </summary>
        private void EnsureDescText()
        {
            if (descText != null) return;
            descText = transform.Find("Desc")?.GetComponent<TMP_Text>();
            if (descText != null) return;

            var go = new GameObject("Desc", typeof(RectTransform), typeof(TextMeshProUGUI));
            var rt = go.GetComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = new Vector2(0.06f, 0.58f);
            rt.anchorMax = new Vector2(0.94f, 0.74f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.GetComponent<TextMeshProUGUI>();
            tmp.fontSize = 22;
            tmp.color = new Color(0.78f, 0.80f, 0.88f, 1f);
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.raycastTarget = false;
            tmp.richText = true;
            tmp.fontStyle = FontStyles.Italic;
            tmp.text = string.Empty;
            descText = tmp;

            // Stats мог занимать всю высоту — оставляем место под Desc.
            if (statsText != null)
            {
                var statsRt = statsText.rectTransform;
                if (statsRt.anchorMax.y > 0.57f)
                    statsRt.anchorMax = new Vector2(statsRt.anchorMax.x, 0.56f);
            }

            // Desc между Slot и Stats в иерархии.
            var slot = transform.Find("Slot");
            if (slot != null) rt.SetSiblingIndex(slot.GetSiblingIndex() + 1);
        }

        private void AdjustStatsAnchorsForDesc(bool hasDescription)
        {
            if (statsText == null) return;
            var statsRt = statsText.rectTransform;
            statsRt.anchorMin = new Vector2(0.06f, 0.22f);
            statsRt.anchorMax = new Vector2(0.94f, hasDescription ? 0.56f : 0.74f);
        }

        /// <summary>
        /// Старые префабы (CharacterUiPrefabCreator) имели только Equip + «Разобрать» без LearnRecipeButton —
        /// кнопка «Изучить» не находилась и не показывалась.
        /// </summary>
        private void EnsureLearnRecipeButtonFromPrefabIfMissing()
        {
            if (learnRecipeButton != null) return;
            var equip = transform.Find("EquipButton")?.GetComponent<Button>();
            var salvage = transform.Find("SalvageButton")?.GetComponent<Button>();
            if (equip == null || salvage == null) return;

            var go = Instantiate(equip.gameObject, equip.transform.parent);
            go.name = "LearnRecipeButton";
            var eqRt = equip.GetComponent<RectTransform>();
            var svRt = salvage.GetComponent<RectTransform>();
            var lrt = go.GetComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.36f, eqRt.anchorMin.y);
            lrt.anchorMax = new Vector2(0.64f, eqRt.anchorMax.y);
            eqRt.anchorMin = new Vector2(0.06f, eqRt.anchorMin.y);
            eqRt.anchorMax = new Vector2(0.34f, eqRt.anchorMax.y);
            svRt.anchorMin = new Vector2(0.66f, svRt.anchorMin.y);
            svRt.anchorMax = new Vector2(0.94f, svRt.anchorMax.y);

            var t = go.GetComponentInChildren<TMP_Text>(true);
            if (t != null) t.text = "Изучить";

            learnRecipeButton = go.GetComponent<Button>();
            learnRecipeLabel = t;
        }

        private static void EnsureButtonLabelRaycast(Button button)
        {
            if (button == null) return;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.raycastTarget = false;
        }

        private static void EnsureButtonFx(Button button)
        {
            if (button == null) return;
            if (button.GetComponent<CharacterModalButtonFx>() == null)
                button.gameObject.AddComponent<CharacterModalButtonFx>();
        }
    }
}

