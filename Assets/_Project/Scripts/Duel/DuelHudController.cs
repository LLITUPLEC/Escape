using System.Collections;
using Project.Player;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Duel
{
    public sealed class DuelHudController : MonoBehaviour
    {
        [SerializeField] private Button exitButton;
        [SerializeField] private Button jumpButton;
        [SerializeField] private CanvasGroup confirmGroup;
        [SerializeField] private Button confirmYesButton;
        [SerializeField] private Button confirmNoButton;
        [SerializeField] private CanvasGroup bannerGroup;
        [SerializeField] private Text bannerText;
        [Header("Banner FX")]
        [SerializeField] private float bannerAutoHideSeconds = 5f;
        [SerializeField] private float bannerPulseSpeed = 1.8f;
        [SerializeField] private float bannerPulseScaleAmplitude = 0.03f;
        [SerializeField] private float bannerPulseAlphaAmplitude = 0.14f;

        private DuelRoomManager _room;
        private Coroutine _bannerRoutine;
        private RectTransform _bannerRect;
        private Vector3 _bannerBaseScale = Vector3.one;
        private PlayerMovementController _jumpMover;
        private bool _jumpUiCreated;

        public void Bind(
            DuelRoomManager room,
            Button exit,
            CanvasGroup confirm,
            Button yes,
            Button no,
            CanvasGroup banner,
            Text bannerLabel)
        {
            _room = room;
            exitButton = exit;
            confirmGroup = confirm;
            confirmYesButton = yes;
            confirmNoButton = no;
            bannerGroup = banner;
            bannerText = bannerLabel;
        }

        private void Awake()
        {
            if (_room == null)
            {
                _room = FindAnyObjectByType<DuelRoomManager>();
            }

            if (exitButton != null) exitButton.onClick.AddListener(OnExitClicked);
            WireJumpButton();
            if (confirmYesButton != null) confirmYesButton.onClick.AddListener(OnConfirmYes);
            if (confirmNoButton != null) confirmNoButton.onClick.AddListener(OnConfirmNo);

            SetConfirmVisible(false);
            SetBannerVisible(false);
            CacheBannerRect();
        }

        /// <summary> Вызывается после спавна локального игрока (кнопка «Прыжок» для тач-управления). </summary>
        public void EnsureJumpButton(PlayerMovementController mover)
        {
            _jumpMover = mover;
            if (jumpButton == null && !_jumpUiCreated && exitButton != null)
            {
                var canvasRt = exitButton.transform.parent as RectTransform;
                if (canvasRt != null)
                {
                    var go = new GameObject("JumpButton", typeof(Image), typeof(Button));
                    var rt = go.GetComponent<RectTransform>();
                    rt.SetParent(canvasRt, false);
                    rt.anchorMin = new Vector2(1f, 0f);
                    rt.anchorMax = new Vector2(1f, 0f);
                    rt.pivot = new Vector2(1f, 0f);
                    rt.anchoredPosition = new Vector2(-40f, 40f);
                    rt.sizeDelta = new Vector2(220f, 100f);
                    go.GetComponent<Image>().color = new Color(0.2f, 0.55f, 0.95f, 1f);

                    var textGo = new GameObject("Text", typeof(Text));
                    textGo.transform.SetParent(go.transform, false);
                    var txt = textGo.GetComponent<Text>();
                    txt.text = "Прыжок";
                    txt.alignment = TextAnchor.MiddleCenter;
                    txt.color = Color.white;
                    txt.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                    txt.fontSize = 40;
                    var trt = textGo.GetComponent<RectTransform>();
                    trt.anchorMin = Vector2.zero;
                    trt.anchorMax = Vector2.one;
                    trt.offsetMin = Vector2.zero;
                    trt.offsetMax = Vector2.zero;

                    jumpButton = go.GetComponent<Button>();
                    _jumpUiCreated = true;
                    WireJumpButton();
                }
            }
            else
                WireJumpButton();
        }

        private void WireJumpButton()
        {
            if (jumpButton == null) return;
            jumpButton.onClick.RemoveListener(OnJumpClicked);
            jumpButton.onClick.AddListener(OnJumpClicked);
        }

        private void OnJumpClicked()
        {
            _jumpMover?.RequestJump();
        }

        private void OnExitClicked()
        {
            Debug.Log("[DuelHUD] Exit clicked");
            SetConfirmVisible(true);
        }

        private void OnConfirmYes()
        {
            Debug.Log("[DuelHUD] Confirm YES");
            SetConfirmVisible(false);
            if (_room == null)
            {
                Debug.LogWarning("[DuelHUD] DuelRoomManager not found; cannot quit.");
                return;
            }
            _room.QuitMatchAndReturnToMenu();
        }

        private void OnConfirmNo()
        {
            Debug.Log("[DuelHUD] Confirm NO");
            SetConfirmVisible(false);
        }

        public void ShowBanner(string text)
        {
            if (bannerText != null) bannerText.text = text ?? string.Empty;
            SetBannerVisible(true);
            RestartBannerRoutine();
        }

        private void OnDisable()
        {
            StopBannerRoutine();
            RestoreBannerVisual();
        }

        private void SetConfirmVisible(bool visible)
        {
            if (confirmGroup == null) return;
            confirmGroup.alpha = visible ? 1f : 0f;
            confirmGroup.interactable = visible;
            confirmGroup.blocksRaycasts = visible;
        }

        private void SetBannerVisible(bool visible)
        {
            if (bannerGroup == null) return;
            bannerGroup.alpha = visible ? 1f : 0f;
            bannerGroup.interactable = visible;
            bannerGroup.blocksRaycasts = visible;
        }

        private void CacheBannerRect()
        {
            if (bannerGroup == null) return;
            _bannerRect = bannerGroup.transform as RectTransform;
            if (_bannerRect != null)
                _bannerBaseScale = _bannerRect.localScale;
        }

        private void RestartBannerRoutine()
        {
            StopBannerRoutine();
            CacheBannerRect();
            RestoreBannerVisual();
            _bannerRoutine = StartCoroutine(BannerPulseAndAutoHideRoutine());
        }

        private void StopBannerRoutine()
        {
            if (_bannerRoutine == null) return;
            StopCoroutine(_bannerRoutine);
            _bannerRoutine = null;
        }

        private void RestoreBannerVisual()
        {
            if (_bannerRect != null)
                _bannerRect.localScale = _bannerBaseScale;
            if (bannerGroup != null && bannerGroup.alpha > 0f)
                bannerGroup.alpha = 1f;
        }

        private IEnumerator BannerPulseAndAutoHideRoutine()
        {
            var duration = Mathf.Max(0.1f, bannerAutoHideSeconds);
            var pulseSpeed = Mathf.Max(0.1f, bannerPulseSpeed);
            var scaleAmp = Mathf.Clamp(bannerPulseScaleAmplitude, 0f, 0.25f);
            var alphaAmp = Mathf.Clamp(bannerPulseAlphaAmplitude, 0f, 0.45f);
            var elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                var wave = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI * 2f);
                var alpha = Mathf.Clamp01(1f - alphaAmp + alphaAmp * wave);
                if (bannerGroup != null)
                    bannerGroup.alpha = alpha;
                if (_bannerRect != null)
                {
                    var scale = 1f + scaleAmp * wave;
                    _bannerRect.localScale = _bannerBaseScale * scale;
                }
                yield return null;
            }

            // Небольшой финальный fade-out, чтобы скрытие выглядело мягко.
            var fade = 0f;
            var fadeDur = 0.2f;
            var startAlpha = bannerGroup != null ? bannerGroup.alpha : 0f;
            while (fade < fadeDur)
            {
                fade += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(fade / fadeDur);
                if (bannerGroup != null)
                    bannerGroup.alpha = Mathf.Lerp(startAlpha, 0f, t);
                yield return null;
            }

            SetBannerVisible(false);
            RestoreBannerVisual();
            _bannerRoutine = null;
        }
    }
}

