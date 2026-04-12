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
        [SerializeField] private TMP_Text statsText;
        [SerializeField] private Button closeButton;
        [SerializeField] private Button equipToggleButton;
        [SerializeField] private TMP_Text equipToggleLabel;
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

        public void Bind(Action onClose, Action onEquipToggle, Action onSalvage)
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

        public void SetTitle(string value)
        {
            if (titleText != null) titleText.text = value ?? string.Empty;
        }

        public void SetSlot(string value)
        {
            if (slotText != null) slotText.text = value ?? string.Empty;
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
            if (statsText == null) statsText = transform.Find("Stats")?.GetComponent<TMP_Text>();
            if (closeButton == null) closeButton = transform.Find("CloseButton")?.GetComponent<Button>();
            if (equipToggleButton == null) equipToggleButton = transform.Find("EquipButton")?.GetComponent<Button>();
            if (equipToggleLabel == null && equipToggleButton != null) equipToggleLabel = equipToggleButton.GetComponentInChildren<TMP_Text>(true);
            if (salvageButton == null) salvageButton = transform.Find("SalvageButton")?.GetComponent<Button>();

            EnsureButtonLabelRaycast(closeButton);
            EnsureButtonLabelRaycast(equipToggleButton);
            EnsureButtonLabelRaycast(salvageButton);
            EnsureButtonFx(closeButton);
            EnsureButtonFx(equipToggleButton);
            EnsureButtonFx(salvageButton);
            if (statsText != null) statsText.richText = true;
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

