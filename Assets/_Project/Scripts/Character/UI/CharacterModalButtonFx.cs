using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Project.Character.UI
{
    [DisallowMultipleComponent]
    public sealed class CharacterModalButtonFx : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform targetRect;
        [SerializeField] private Graphic targetGraphic;
        [SerializeField] private TMP_Text label;

        [Header("Scale")]
        [SerializeField] private float normalScale = 1f;
        [SerializeField] private float hoverScale = 1.04f;
        [SerializeField] private float pressedScale = 0.96f;

        [Header("Color")]
        [SerializeField] private Color normalColor = new(0.25f, 0.25f, 0.34f, 1f);
        [SerializeField] private Color hoverColor = new(0.30f, 0.30f, 0.40f, 1f);
        [SerializeField] private Color pressedColor = new(0.20f, 0.20f, 0.28f, 1f);
        [SerializeField] private Color normalLabelColor = Color.white;
        [SerializeField] private Color hoverLabelColor = new(1f, 0.95f, 0.82f, 1f);
        [SerializeField] private Color pressedLabelColor = new(1f, 0.90f, 0.72f, 1f);

        [Header("Timing")]
        [SerializeField, Min(0.01f)] private float transitionDuration = 0.08f;

        private Coroutine _transition;
        private bool _hovered;
        private bool _pressed;

        private void Awake()
        {
            if (targetRect == null) targetRect = transform as RectTransform;
            if (targetGraphic == null) targetGraphic = GetComponent<Graphic>();
            if (label == null) label = GetComponentInChildren<TMP_Text>(true);
            ApplyInstant(normalScale, normalColor, normalLabelColor);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            UpdateState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            _pressed = false;
            UpdateState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            _pressed = true;
            UpdateState();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            _pressed = false;
            UpdateState();
        }

        private void UpdateState()
        {
            if (_pressed)
            {
                AnimateTo(pressedScale, pressedColor, pressedLabelColor);
                return;
            }

            if (_hovered)
            {
                AnimateTo(hoverScale, hoverColor, hoverLabelColor);
                return;
            }

            AnimateTo(normalScale, normalColor, normalLabelColor);
        }

        private void AnimateTo(float scale, Color color, Color labelColor)
        {
            if (_transition != null) StopCoroutine(_transition);
            _transition = StartCoroutine(TransitionRoutine(scale, color, labelColor));
        }

        private IEnumerator TransitionRoutine(float toScale, Color toColor, Color toLabelColor)
        {
            var fromScale = targetRect != null ? targetRect.localScale.x : 1f;
            var fromColor = targetGraphic != null ? targetGraphic.color : Color.white;
            var fromLabelColor = label != null ? label.color : Color.white;
            var t = 0f;
            while (t < transitionDuration)
            {
                t += Time.unscaledDeltaTime;
                var p = Mathf.Clamp01(t / transitionDuration);
                ApplyInstant(
                    Mathf.Lerp(fromScale, toScale, p),
                    Color.Lerp(fromColor, toColor, p),
                    Color.Lerp(fromLabelColor, toLabelColor, p));
                yield return null;
            }

            ApplyInstant(toScale, toColor, toLabelColor);
            _transition = null;
        }

        private void ApplyInstant(float scale, Color color, Color labelColor)
        {
            if (targetRect != null) targetRect.localScale = Vector3.one * scale;
            if (targetGraphic != null) targetGraphic.color = color;
            if (label != null) label.color = labelColor;
        }
    }
}

