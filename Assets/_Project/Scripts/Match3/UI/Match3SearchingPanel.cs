using System;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Full-screen overlay shown while waiting for an opponent.</summary>
    public sealed class Match3SearchingPanel : MonoBehaviour
    {
        public enum SearchOverlayPreset
        {
            Custom = 0,
            Minimal = 1,
            SciFi = 2,
            Dark = 3
        }

        [Header("Preset")]
        [SerializeField] private SearchOverlayPreset visualPreset = SearchOverlayPreset.SciFi;
        [SerializeField] private bool applyPresetOnAwake = true;

        [Header("Core References")]
        [SerializeField] public Text statusText;
        [SerializeField] public Button cancelButton;
        [SerializeField] private RectTransform spinnerRect;

        [Header("Optional References (auto-resolved by name if empty)")]
        [SerializeField] private Image dimBackgroundImage;
        [SerializeField] private RectTransform modalWindowRect;
        [SerializeField] private Image modalWindowImage;
        [SerializeField] private Image cancelButtonImage;

        [Header("Layout")]
        [SerializeField] private Vector2 modalAnchorMin = new(0.22f, 0.34f);
        [SerializeField] private Vector2 modalAnchorMax = new(0.78f, 0.66f);
        [SerializeField] private Vector2 spinnerSize = new(86f, 86f);
        [SerializeField] private Vector2 statusAnchorMin = new(0.08f, 0.62f);
        [SerializeField] private Vector2 statusAnchorMax = new(0.92f, 0.86f);
        [SerializeField] private Vector2 cancelAnchorMin = new(0.2f, 0.12f);
        [SerializeField] private Vector2 cancelAnchorMax = new(0.8f, 0.32f);

        [Header("Colors")]
        [SerializeField] private Color dimBackgroundColor = new(0.01f, 0.01f, 0.02f, 1f);
        [SerializeField] private Color modalWindowColor = new(0.1f, 0.12f, 0.18f, 0.94f);
        [SerializeField] private Color statusTextColor = Color.white;
        [SerializeField] private Color cancelButtonColor = new(0.45f, 0.12f, 0.12f, 1f);
        [SerializeField] private Color spinnerColor = new(0.85f, 0.9f, 1f, 0.95f);

        [Header("Typography")]
        [SerializeField, Min(10)] private int statusFontSize = 42;
        [SerializeField, Min(10)] private int cancelFontSize = 30;

        [Header("Animation")]
        [SerializeField] private float spinnerRotationSpeed = -180f;
        [SerializeField] private float spinnerPulseSpeed = 2.4f;
        [SerializeField] private float spinnerPulseScale = 0.12f;
        [SerializeField] private float spinnerMinAlpha = 0.45f;
        [SerializeField] private float spinnerMaxAlpha = 1f;

        /// <summary>Fired when the player presses Cancel.</summary>
        public event Action OnCancelClicked;
        private Image _spinnerImage;
        private Vector3 _spinnerBaseScale = Vector3.one;
        private float _animTime;

        private void Awake()
        {
            ResolveReferences();
            if (applyPresetOnAwake)
                ApplyPresetIfNeeded();
            ApplyVisualSettings();

            if (cancelButton != null)
                cancelButton.onClick.AddListener(() => OnCancelClicked?.Invoke());

            if (spinnerRect != null)
            {
                _spinnerImage = spinnerRect.GetComponent<Image>();
                _spinnerBaseScale = spinnerRect.localScale;
                if (_spinnerImage != null)
                {
                    // Use a radial segment so animation is obvious even with simple circle sprites.
                    _spinnerImage.type = Image.Type.Filled;
                    _spinnerImage.fillMethod = Image.FillMethod.Radial360;
                    _spinnerImage.fillOrigin = (int)Image.Origin360.Top;
                    _spinnerImage.fillClockwise = true;
                    _spinnerImage.fillAmount = 0.78f;
                }
            }

            // Keep this overlay above all other UI canvases (Duel + Match3).
            var canvas = GetComponent<Canvas>();
            if (canvas == null) canvas = gameObject.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 2500;

            if (GetComponent<GraphicRaycaster>() == null)
                gameObject.AddComponent<GraphicRaycaster>();
        }

        private void OnValidate()
        {
            ResolveReferences();
            ApplyPresetIfNeeded();
            ApplyVisualSettings();
        }

        private void Update()
        {
            if (!gameObject.activeInHierarchy || spinnerRect == null)
                return;

            spinnerRect.Rotate(0f, 0f, spinnerRotationSpeed * Time.unscaledDeltaTime);

            _animTime += Time.unscaledDeltaTime * Mathf.Max(0.1f, spinnerPulseSpeed);
            var wave01 = 0.5f + 0.5f * Mathf.Sin(_animTime * Mathf.PI * 2f);
            var scaleK = 1f + (wave01 - 0.5f) * 2f * Mathf.Clamp(spinnerPulseScale, 0f, 0.5f);
            spinnerRect.localScale = _spinnerBaseScale * scaleK;

            if (_spinnerImage != null)
            {
                var c = _spinnerImage.color;
                c.a = Mathf.Lerp(spinnerMinAlpha, spinnerMaxAlpha, wave01);
                _spinnerImage.color = c;
                _spinnerImage.fillAmount = Mathf.Lerp(0.35f, 0.9f, wave01);
            }
        }

        private void OnEnable()
        {
            _animTime = 0f;
            if (spinnerRect != null)
                spinnerRect.localScale = _spinnerBaseScale;
        }

        public void Show(string message)
        {
            gameObject.SetActive(true);
            if (statusText != null) statusText.text = message;
        }

        public void Hide() => gameObject.SetActive(false);

        private void ResolveReferences()
        {
            if (dimBackgroundImage == null)
                dimBackgroundImage = GetComponent<Image>();

            if (modalWindowRect == null)
            {
                var tf = transform.Find("ModalWindow");
                if (tf != null) modalWindowRect = tf as RectTransform;
            }

            if (modalWindowImage == null && modalWindowRect != null)
                modalWindowImage = modalWindowRect.GetComponent<Image>();

            if (spinnerRect == null && modalWindowRect != null)
            {
                var tf = modalWindowRect.Find("Spinner");
                if (tf != null) spinnerRect = tf as RectTransform;
            }

            if (statusText == null && modalWindowRect != null)
            {
                var tf = modalWindowRect.Find("StatusText");
                if (tf != null) statusText = tf.GetComponent<Text>();
            }

            if (cancelButton == null && modalWindowRect != null)
            {
                var tf = modalWindowRect.Find("CancelBtn");
                if (tf != null) cancelButton = tf.GetComponent<Button>();
            }

            if (cancelButtonImage == null && cancelButton != null)
                cancelButtonImage = cancelButton.GetComponent<Image>();
        }

        private void ApplyVisualSettings()
        {
            if (dimBackgroundImage != null)
                dimBackgroundImage.color = dimBackgroundColor;

            if (modalWindowRect != null)
            {
                modalWindowRect.anchorMin = modalAnchorMin;
                modalWindowRect.anchorMax = modalAnchorMax;
                modalWindowRect.offsetMin = Vector2.zero;
                modalWindowRect.offsetMax = Vector2.zero;
            }

            if (modalWindowImage != null)
                modalWindowImage.color = modalWindowColor;

            if (spinnerRect != null)
            {
                spinnerRect.anchorMin = new Vector2(0.5f, 0.44f);
                spinnerRect.anchorMax = new Vector2(0.5f, 0.44f);
                spinnerRect.anchoredPosition = Vector2.zero;
                spinnerRect.sizeDelta = spinnerSize;
            }

            if (statusText != null)
            {
                var rt = statusText.rectTransform;
                rt.anchorMin = statusAnchorMin;
                rt.anchorMax = statusAnchorMax;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                statusText.color = statusTextColor;
                statusText.fontSize = Mathf.Max(10, statusFontSize);
                statusText.alignment = TextAnchor.MiddleCenter;
            }

            if (cancelButton != null)
            {
                var rt = cancelButton.transform as RectTransform;
                if (rt != null)
                {
                    rt.anchorMin = cancelAnchorMin;
                    rt.anchorMax = cancelAnchorMax;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }

                var btnText = cancelButton.GetComponentInChildren<Text>(true);
                if (btnText != null)
                {
                    btnText.fontSize = Mathf.Max(10, cancelFontSize);
                    btnText.alignment = TextAnchor.MiddleCenter;
                }
            }

            if (cancelButtonImage != null)
                cancelButtonImage.color = cancelButtonColor;

            if (spinnerRect != null)
            {
                var img = spinnerRect.GetComponent<Image>();
                if (img != null)
                    img.color = spinnerColor;
            }
        }

        private void ApplyPresetIfNeeded()
        {
            switch (visualPreset)
            {
                case SearchOverlayPreset.Minimal:
                    dimBackgroundColor = new Color(0f, 0f, 0f, 0.94f);
                    modalWindowColor = new Color(0.12f, 0.12f, 0.12f, 0.95f);
                    statusTextColor = Color.white;
                    cancelButtonColor = new Color(0.32f, 0.14f, 0.14f, 1f);
                    spinnerColor = new Color(0.92f, 0.92f, 0.92f, 1f);
                    statusFontSize = 40;
                    cancelFontSize = 28;
                    spinnerRotationSpeed = -150f;
                    spinnerPulseSpeed = 2.1f;
                    spinnerPulseScale = 0.08f;
                    spinnerMinAlpha = 0.55f;
                    spinnerMaxAlpha = 1f;
                    modalAnchorMin = new Vector2(0.26f, 0.37f);
                    modalAnchorMax = new Vector2(0.74f, 0.63f);
                    spinnerSize = new Vector2(82f, 82f);
                    break;

                case SearchOverlayPreset.SciFi:
                    dimBackgroundColor = new Color(0.01f, 0.01f, 0.02f, 1f);
                    modalWindowColor = new Color(0.1f, 0.12f, 0.18f, 0.94f);
                    statusTextColor = new Color(0.95f, 0.97f, 1f, 1f);
                    cancelButtonColor = new Color(0.45f, 0.12f, 0.12f, 1f);
                    spinnerColor = new Color(0.82f, 0.9f, 1f, 0.95f);
                    statusFontSize = 42;
                    cancelFontSize = 30;
                    spinnerRotationSpeed = -180f;
                    spinnerPulseSpeed = 2.4f;
                    spinnerPulseScale = 0.12f;
                    spinnerMinAlpha = 0.45f;
                    spinnerMaxAlpha = 1f;
                    modalAnchorMin = new Vector2(0.22f, 0.34f);
                    modalAnchorMax = new Vector2(0.78f, 0.66f);
                    spinnerSize = new Vector2(86f, 86f);
                    break;

                case SearchOverlayPreset.Dark:
                    dimBackgroundColor = new Color(0f, 0f, 0f, 1f);
                    modalWindowColor = new Color(0.06f, 0.06f, 0.08f, 0.96f);
                    statusTextColor = new Color(0.92f, 0.92f, 0.94f, 1f);
                    cancelButtonColor = new Color(0.28f, 0.1f, 0.1f, 1f);
                    spinnerColor = new Color(0.85f, 0.85f, 0.88f, 0.95f);
                    statusFontSize = 40;
                    cancelFontSize = 28;
                    spinnerRotationSpeed = -135f;
                    spinnerPulseSpeed = 1.9f;
                    spinnerPulseScale = 0.06f;
                    spinnerMinAlpha = 0.6f;
                    spinnerMaxAlpha = 1f;
                    modalAnchorMin = new Vector2(0.24f, 0.36f);
                    modalAnchorMax = new Vector2(0.76f, 0.64f);
                    spinnerSize = new Vector2(80f, 80f);
                    break;
            }
        }
    }
}
