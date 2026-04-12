using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    public sealed class CharacterItemActionModalView : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private CanvasGroup panelCanvasGroup;
        [SerializeField] private Button infoButton;
        [SerializeField] private Button sellButton;
        [SerializeField, Min(0.01f)] private float showDuration = 0.16f;
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

        public void Bind(Action onInfo, Action onSell)
        {
            if (infoButton != null)
            {
                infoButton.onClick.RemoveAllListeners();
                if (onInfo != null) infoButton.onClick.AddListener(() => onInfo());
            }

            if (sellButton != null)
            {
                sellButton.onClick.RemoveAllListeners();
                if (onSell != null) sellButton.onClick.AddListener(() => onSell());
            }
        }

        public void ShowAt(Vector2 anchoredPosition)
        {
            EnsureDefaults();
            var rect = PanelRect;
            if (rect != null) rect.anchoredPosition = anchoredPosition;

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
            if (infoButton == null) infoButton = transform.Find("InfoButton")?.GetComponent<Button>();
            if (sellButton == null) sellButton = transform.Find("SellButton")?.GetComponent<Button>();

            EnsureButtonLabelRaycast(infoButton);
            EnsureButtonLabelRaycast(sellButton);
            EnsureButtonFx(infoButton);
            EnsureButtonFx(sellButton);
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

