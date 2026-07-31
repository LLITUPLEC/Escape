using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>
    /// Визуал switch-toggle: ползунок влево/вправо и цвет фона (серый / зелёный).
    /// Объект должен жить на сцене/в префабе; этот скрипт только анимирует состояние.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Toggle))]
    public sealed class SwitchToggleVisual : MonoBehaviour
    {
        [SerializeField] private Toggle toggle;
        [SerializeField] private Image background;
        [SerializeField] private RectTransform handle;
        [SerializeField] private Color offColor = new Color(0.45f, 0.45f, 0.48f, 1f);
        [SerializeField] private Color onColor = new Color(0.22f, 0.72f, 0.32f, 1f);
        [SerializeField] private float animDuration = 0.18f;
        [SerializeField] private float handlePadding = 5f;

        private Coroutine _anim;
        private RectTransform _root;

        private void Awake()
        {
            if (toggle == null) toggle = GetComponent<Toggle>();
            _root = transform as RectTransform;
            EnsureSprites();
        }

        private void OnEnable()
        {
            EnsureSprites();
            if (toggle != null)
                toggle.onValueChanged.AddListener(OnToggleChanged);
            ApplyImmediate(toggle != null && toggle.isOn);
        }

        private void OnDisable()
        {
            if (toggle != null)
                toggle.onValueChanged.RemoveListener(OnToggleChanged);
            if (_anim != null)
            {
                StopCoroutine(_anim);
                _anim = null;
            }
        }

        private void OnToggleChanged(bool isOn)
        {
            if (!isActiveAndEnabled)
            {
                ApplyImmediate(isOn);
                return;
            }

            if (_anim != null) StopCoroutine(_anim);
            _anim = StartCoroutine(AnimateTo(isOn));
        }

        private IEnumerator AnimateTo(bool isOn)
        {
            float fromX = handle != null ? handle.anchoredPosition.x : 0f;
            float toX = HandleX(isOn);
            Color fromC = background != null ? background.color : offColor;
            Color toC = isOn ? onColor : offColor;

            float t = 0f;
            float dur = Mathf.Max(0.01f, animDuration);
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / dur));
                if (handle != null)
                {
                    var p = handle.anchoredPosition;
                    p.x = Mathf.Lerp(fromX, toX, u);
                    handle.anchoredPosition = p;
                }
                if (background != null)
                    background.color = Color.Lerp(fromC, toC, u);
                yield return null;
            }

            ApplyImmediate(isOn);
            _anim = null;
        }

        private void ApplyImmediate(bool isOn)
        {
            if (background != null)
                background.color = isOn ? onColor : offColor;
            if (handle != null)
            {
                var p = handle.anchoredPosition;
                p.x = HandleX(isOn);
                handle.anchoredPosition = p;
            }
        }

        private float HandleX(bool isOn)
        {
            if (_root == null) _root = transform as RectTransform;
            if (_root == null || handle == null) return 0f;

            float halfTrack = _root.rect.width * 0.5f;
            float halfHandle = handle.rect.width * 0.5f;
            float max = Mathf.Max(0f, halfTrack - halfHandle - handlePadding);
            return isOn ? max : -max;
        }

        private void EnsureSprites()
        {
            var white = ModalPanelCloseButton.WhiteSprite();
            if (background != null && background.sprite == null)
                background.sprite = white;
            if (handle != null)
            {
                var img = handle.GetComponent<Image>();
                if (img != null && img.sprite == null)
                    img.sprite = white;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (toggle == null) toggle = GetComponent<Toggle>();
            if (_root == null) _root = transform as RectTransform;
            EnsureSprites();
            if (!Application.isPlaying)
                ApplyImmediate(toggle != null && toggle.isOn);
        }
#endif
    }
}
