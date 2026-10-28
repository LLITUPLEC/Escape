using System;
using Project.UI;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

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

        private const string DefaultCancelSpritePath = "Assets/_Project/img/match3/btmc_exit.png";

        [Header("Preset")]
        [SerializeField] private SearchOverlayPreset visualPreset = SearchOverlayPreset.SciFi;
        [SerializeField] private bool applyPresetOnAwake = true;

        [Header("Core References")]
        [SerializeField] public TMP_Text statusText;
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

        [Header("Cancel Button (bottom-stretch)")]
        [SerializeField] private float cancelLeft = 100f;
        [SerializeField] private float cancelRight = 100f;
        [SerializeField] private float cancelPosY = 100f;
        [SerializeField] private float cancelHeight = 200f;
        [SerializeField] private Sprite cancelButtonSprite;
        [SerializeField] private string cancelLabelText = "Отмена";

        [Header("Colors")]
        [SerializeField] private Color dimBackgroundColor = new(0.01f, 0.01f, 0.02f, 1f);
        [SerializeField] private Color modalWindowColor = new(0.1f, 0.12f, 0.18f, 0.94f);
        [SerializeField] private Color statusTextColor = Color.white;
        [SerializeField] private Color cancelButtonColor = Color.white;
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
        private bool _expeditionStyle;
        private Button _boundCancel;
        private TMP_Text _modeText, _elapsedText, _headingText;
        private TMP_Text _opponentCaption;
        private Image _opponentPortrait;
        private bool _matchReady;
        private string _modeLabel;
        private float _searchStartedAt;
        private int _lastElapsedSecond = -1;
#if UNITY_EDITOR
        private bool _pendingValidateApply;
#endif

        private void Awake()
        {
            ResolveReferences();
            if (applyPresetOnAwake)
                ApplyPresetIfNeeded();
            ApplyVisualSettings();

            BindCancelCallback();

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
            // RectTransform/TMP нельзя трогать прямо в OnValidate — SendMessage warning.
#if UNITY_EDITOR
            if (_pendingValidateApply) return;
            _pendingValidateApply = true;
            EditorApplication.delayCall += ApplyVisualSettingsDeferred;
#endif
        }

#if UNITY_EDITOR
        private void ApplyVisualSettingsDeferred()
        {
            _pendingValidateApply = false;
            if (this == null) return;
            ResolveReferences();
            if (applyPresetOnAwake)
                ApplyPresetIfNeeded();
            ApplyVisualSettings();
        }
#endif

        private void Update()
        {
            if (!gameObject.activeInHierarchy || spinnerRect == null)
                return;

            if (_expeditionStyle)
            {
                spinnerRect.Rotate(0, 0, -95 * Time.unscaledDeltaTime);
                var seconds = Mathf.Max(0, Mathf.FloorToInt(Time.unscaledTime - _searchStartedAt));
                if (seconds != _lastElapsedSecond && _elapsedText != null)
                {
                    _lastElapsedSecond = seconds;
                    _elapsedText.text = "Прошло " + (seconds / 60).ToString("00") + ":" + (seconds % 60).ToString("00");
                }
                return;
            }

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
            _searchStartedAt = Time.unscaledTime;
            _lastElapsedSecond = -1;
            if (spinnerRect != null)
                spinnerRect.localScale = _spinnerBaseScale;
        }

        public void Show(string message)
        {
            if (!_expeditionStyle) UseExpeditionStyle();
            BindCancelCallback();
            gameObject.SetActive(true);
            if (statusText != null) statusText.text = message;
            if (_headingText != null)
            {
                var connecting = !string.IsNullOrEmpty(message) &&
                    (message.IndexOf("Восстановление", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     message.IndexOf("подключения", StringComparison.OrdinalIgnoreCase) >= 0);
                _headingText.text = connecting ? "ПОДКЛЮЧЕНИЕ К БОЮ" : _matchReady ? "МАТЧ ГОТОВ" : "ПОИСК СОПЕРНИКА";
            }
        }

        public void Hide() => gameObject.SetActive(false);

        /// <summary>Replace the unknown slot only after a server match has been joined.</summary>
        public void ShowReady(Sprite portrait, string opponentName, bool upperBody, string message)
        {
            if (!_expeditionStyle) UseExpeditionStyle();
            _matchReady = true;
            if (_opponentPortrait != null)
            {
                _opponentPortrait.sprite = portrait;
                _opponentPortrait.enabled = portrait != null;
                _opponentPortrait.color = Color.white;
                var question = _opponentPortrait.transform.parent.Find("UnknownOpponent");
                if (question != null) question.gameObject.SetActive(false);
                var fit = _opponentPortrait.GetComponent<AspectRatioFitter>();
                if (fit != null) fit.enabled = false;
                var monsterFill = _opponentPortrait.GetComponent<ExpeditionMatchPortraitFill>();
                if (monsterFill != null) monsterFill.enabled = false;
                ExpeditionUi.SetRect(_opponentPortrait.rectTransform, 0, 0, 1, 1);
                if (upperBody) ExpeditionMatchStates.CropUpperBody(_opponentPortrait);
                else if (portrait != null) ExpeditionUi.Ensure<ExpeditionMatchPortraitFill>(_opponentPortrait.gameObject).Bind(_opponentPortrait);
            }
            if (_opponentCaption != null)
                _opponentCaption.text = string.IsNullOrWhiteSpace(opponentName) ? "СОПЕРНИК" : opponentName;
            Show(message);
        }

        public void SetModeLabel(string modeLabel)
        {
            _modeLabel = modeLabel;
            if (_modeText != null)
                _modeText.text = string.IsNullOrWhiteSpace(_modeLabel) ? "Подготовка матча" : "Режим: " + _modeLabel;
        }

        public void UseExpeditionStyle(string modeLabel = null)
        {
            ResolveReferences();
            var safe = ExpeditionMatchStates.PrepareOverlay(this, 2500);
            if (modeLabel != null) SetModeLabel(modeLabel);
            if (_expeditionStyle) { BindCancelCallback(); return; }
            _expeditionStyle = true;
            foreach (var layout in GetComponentsInChildren<LayoutGroup>(true)) layout.enabled = false;
            foreach (var fitter in GetComponentsInChildren<ContentSizeFitter>(true)) fitter.enabled = false;
            var oldSpinner = spinnerRect;
            var heading = ExpeditionMatchStates.Panel(safe, "SearchHeadingFrame", .10f, .76f, .90f, .89f);
            _headingText = ExpeditionUi.Text(heading.transform, "SearchHeading", "ПОИСК СОПЕРНИКА", 53,
                .055f, .40f, .945f, .86f, TextAlignmentOptions.Center);
            _headingText.fontStyle = FontStyles.Bold;
            _headingText.enableAutoSizing = true; _headingText.fontSizeMin = 34; _headingText.fontSizeMax = 53;
            _modeText = ExpeditionUi.Text(heading.transform, "ActualSearchMode", "", 30,
                .06f, .12f, .94f, .39f, TextAlignmentOptions.Center);
            _modeText.color = ExpeditionTheme.Muted;
            SetModeLabel(_modeLabel);
            ExpeditionMatchStates.Portrait(safe, "LocalExplorerPortrait", .075f, .455f, .39f, .73f, false, true);
            _opponentPortrait = ExpeditionMatchStates.Portrait(safe, "UnknownOpponentPortrait", .61f, .455f, .925f, .73f, true, true);
            var you = ExpeditionUi.Text(safe, "LocalParticipantCaption", "ВЫ", 24, .08f, .425f, .385f, .452f, TextAlignmentOptions.Center);
            you.color = ExpeditionTheme.Amber;
            _opponentCaption = ExpeditionUi.Text(safe, "UnknownParticipantCaption", "СОПЕРНИК", 24, .615f, .425f, .92f, .452f, TextAlignmentOptions.Center);
            _opponentCaption.enableAutoSizing = true;
            _opponentCaption.fontSizeMin = 16; _opponentCaption.fontSizeMax = 24;
            spinnerRect = ExpeditionUi.Rect(safe, "MechanicalSearchRotor", .5f, .59f, .5f, .59f);
            spinnerRect.sizeDelta = new Vector2(190, 190);
            spinnerRect.gameObject.AddComponent<ExpeditionMatchSpinner>().raycastTarget = false;
            _spinnerImage = null; _spinnerBaseScale = Vector3.one;
            _elapsedText = ExpeditionUi.Text(safe, "ElapsedSearchTime", "Прошло 00:00", 25,
                .345f, .435f, .655f, .48f, TextAlignmentOptions.Center);
            if (statusText == null)
                statusText = ExpeditionUi.Text(safe, "ActualSearchStatus", "", 30, .10f, .30f, .90f, .365f);
            ExpeditionMatchStates.Place(statusText, safe, .10f, .30f, .90f, .365f, 30);
            if (cancelButton == null)
                cancelButton = ExpeditionUi.Button(safe, "CancelBtn", "Отмена", .26f, .205f, .74f, .282f, null, false, 42);
            cancelButton.transform.SetParent(safe, false);
            ExpeditionUi.SetRect(cancelButton.transform as RectTransform, .26f, .205f, .74f, .282f);
            ExpeditionUi.StyleButton(cancelButton, false, 42);
            ExpeditionUi.Label(cancelButton, string.IsNullOrEmpty(cancelLabelText) ? "Отмена" : cancelLabelText);
            ExpeditionMatchStates.StyleCancel(cancelButton);
            if (oldSpinner != null) oldSpinner.gameObject.SetActive(false);
            if (modalWindowRect != null) modalWindowRect.gameObject.SetActive(false);
            BindCancelCallback();
        }

        private void BindCancelCallback()
        {
            if (_boundCancel == cancelButton) return;
            if (_boundCancel != null) _boundCancel.onClick.RemoveListener(HandleCancel);
            _boundCancel = cancelButton;
            if (_boundCancel != null) _boundCancel.onClick.AddListener(HandleCancel);
        }

        private void HandleCancel() => OnCancelClicked?.Invoke();

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
                if (tf != null) statusText = tf.GetComponent<TMP_Text>();
            }

            if (cancelButton == null)
            {
                var tf = transform.Find("CancelBtn");
                if (tf == null && modalWindowRect != null)
                    tf = modalWindowRect.Find("CancelBtn");
                if (tf != null) cancelButton = tf.GetComponent<Button>();
            }

            if (cancelButtonImage == null && cancelButton != null)
                cancelButtonImage = cancelButton.GetComponent<Image>();

            if (cancelButtonSprite == null)
                cancelButtonSprite = LoadDefaultCancelSprite();
        }

        private static Sprite LoadDefaultCancelSprite()
        {
#if UNITY_EDITOR
            return AssetDatabase.LoadAssetAtPath<Sprite>(DefaultCancelSpritePath);
#else
            return null;
#endif
        }

        private void ApplyVisualSettings()
        {
            if (_expeditionStyle) return;
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
                statusText.alignment = TextAlignmentOptions.Center;
            }

            ApplyCancelButtonLayout();
        }

        private void ApplyCancelButtonLayout()
        {
            if (cancelButton == null) return;

            var rt = cancelButton.transform as RectTransform;
            if (rt != null)
            {
                // Bottom-stretch: Left / Right / Pos Y / Height (как в инспекторе)
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.offsetMin = new Vector2(cancelLeft, cancelPosY);
                rt.offsetMax = new Vector2(-cancelRight, cancelPosY + cancelHeight);
            }

            var btnText = cancelButton.GetComponentInChildren<TMP_Text>(true);
            if (btnText != null)
            {
                btnText.gameObject.SetActive(true);
                btnText.text = string.IsNullOrEmpty(cancelLabelText) ? "Отмена" : cancelLabelText;
                btnText.fontSize = Mathf.Max(10, cancelFontSize);
                btnText.alignment = TextAlignmentOptions.Center;
            }

            if (cancelButtonImage != null)
            {
                cancelButtonImage.color = cancelButtonColor;
                if (cancelButtonSprite != null)
                {
                    cancelButtonImage.sprite = cancelButtonSprite;
                    // Sliced: у btmc_exit задан spriteBorder.
                    cancelButtonImage.type = Image.Type.Sliced;
                    cancelButtonImage.preserveAspect = false;
                }
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

            // CancelBtn layout/color/sprite — фиксированные, пресеты их не трогают.
            cancelButtonColor = Color.white;
        }
    }
}
