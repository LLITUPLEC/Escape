using System;
using System.Collections;
using System.Collections.Generic;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Achievements
{
    /// <summary>A single non-blocking, account-scoped overlay for completions and confirmed reward claims.</summary>
    public sealed class AchievementInGameToastHost : MonoBehaviour
    {
        private static AchievementInGameToastHost _instance;
        private readonly Queue<AchievementUnlockInfo> _queue = new Queue<AchievementUnlockInfo>();
        private readonly HashSet<string> _seen = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> _queuedAt = new Dictionary<string, float>(StringComparer.Ordinal);
        private Coroutine _routine;
        private CanvasGroup _panelGroup;
        private RectTransform _panelRt;
        private TMP_Text _headlineTmp, _titleTmp, _rewardTmp;
        private Canvas _internalCanvas;
        private RectTransform _safeArea;
        private string _scope;
        private bool _scopeLoaded, _subscribed;
        private const int MaxPending = 4;
        private const float NoticeLifetime = 14f, ShowSeconds = 3.1f;
        private static readonly Vector2 RestPosition = new Vector2(0, -12);
        public AchievementUnlockInfo CurrentNotice { get; private set; }
        public int PresentedCount { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() => Ensure();

        public static AchievementInGameToastHost Ensure()
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<AchievementInGameToastHost>(FindObjectsInactive.Include);
            if (_instance == null)
            {
                var root = new GameObject("AchievementInGameToastHost", typeof(RectTransform));
                _instance = root.AddComponent<AchievementInGameToastHost>();
            }
            _instance.Initialize();
            return _instance;
        }

        private void Awake()
        {
            if (_instance != null && _instance != this) { Destroy(gameObject); return; }
            _instance = this;
            if (Application.isPlaying) DontDestroyOnLoad(gameObject);
            Initialize();
        }

        private void Initialize()
        {
            if (_internalCanvas == null || _panelRt == null || _titleTmp == null) BuildUi();
            // Rebind idempotently after Editor/domain reloads as well as fresh launches.
            AchievementLifecycle.OnAwaitingClaim -= Handle;
            AchievementLifecycle.OnRewardClaimed -= Handle;
            SceneManager.activeSceneChanged -= HandleSceneChanged;
            AchievementLifecycle.OnAwaitingClaim += Handle;
            AchievementLifecycle.OnRewardClaimed += Handle;
            SceneManager.activeSceneChanged += HandleSceneChanged;
            _subscribed = true;
            RefreshScope();
        }

        private void OnEnable()
        {
            if (_internalCanvas == null) return;
            Initialize();
            StartPending();
        }

        private void OnDisable() => StopPresentation(false);

        private void OnDestroy()
        {
            if (_subscribed)
            {
                AchievementLifecycle.OnAwaitingClaim -= Handle;
                AchievementLifecycle.OnRewardClaimed -= Handle;
                SceneManager.activeSceneChanged -= HandleSceneChanged;
            }
            if (_instance == this) _instance = null;
        }

        private void Update() => RefreshScope();

        private void RefreshScope()
        {
            var scope = LocalAccountCacheScope.Current;
            if (_scopeLoaded && string.Equals(_scope, scope, StringComparison.Ordinal)) return;
            StopPresentation(true);
            _seen.Clear();
            _scope = scope;
            _scopeLoaded = true;
        }

        private void HandleSceneChanged(Scene previous, Scene next)
        {
            // Notifications belong to the screen on which they happened. Do not replay
            // a battle backlog when a different page is opened.
            StopPresentation(true);
        }

        private void StopPresentation(bool clearQueue)
        {
            StopAllCoroutines();
            _routine = null;
            CurrentNotice = null;
            if (_panelGroup != null) _panelGroup.alpha = 0;
            if (_panelRt != null)
            {
                _panelRt.anchoredPosition = RestPosition;
                _panelRt.localScale = Vector3.one;
            }
            if (!clearQueue) return;
            _queue.Clear();
            _queuedAt.Clear();
        }

        private static string Token(AchievementUnlockInfo info) =>
            info.ChainId + "|" + info.StepIndex + "|" + (int)info.NoticeKind;

        private void Handle(AchievementUnlockInfo info)
        {
            if (info == null || string.IsNullOrEmpty(info.ChainId)) return;
            RefreshScope();
            var token = Token(info);
            if (!_seen.Add(token)) return;
            // Bound a burst of independently completed chains; old popups never
            // consume minutes of another screen's time.
            while (_queue.Count >= MaxPending) _queuedAt.Remove(Token(_queue.Dequeue()));
            _queue.Enqueue(info);
            _queuedAt[token] = Time.realtimeSinceStartup;
            StartPending();
        }

        private void StartPending()
        {
            if (_routine == null && _queue.Count > 0 && isActiveAndEnabled && Application.isPlaying)
                _routine = StartCoroutine(RunQueue());
        }

        private IEnumerator RunQueue()
        {
            // Assign the coroutine handle before an all-expired queue can finish synchronously.
            yield return null;
            while (_queue.Count > 0)
            {
                var info = _queue.Dequeue();
                var token = Token(info);
                var fresh = _queuedAt.TryGetValue(token, out var when) && Time.realtimeSinceStartup - when <= NoticeLifetime;
                _queuedAt.Remove(token);
                if (!fresh) continue;
                BindNotice(info);
                yield return Animate(0, 1, RestPosition + Vector2.up * 28, RestPosition, .24f);
                PresentedCount++;
                yield return new WaitForSecondsRealtime(ShowSeconds);
                yield return Animate(1, 0, RestPosition, RestPosition + Vector2.up * 20, .20f);
                _panelRt.anchoredPosition = RestPosition;
                CurrentNotice = null;
                yield return null;
            }
            _routine = null;
        }

        private void BindNotice(AchievementUnlockInfo info)
        {
            CurrentNotice = info;
            var claimed = info.NoticeKind == AchievementNoticeKind.RewardGrantedToast;
            _headlineTmp.text = claimed ? "НАГРАДА ПОЛУЧЕНА" : "ДОСТИЖЕНИЕ ВЫПОЛНЕНО";
            _titleTmp.text = info.Title ?? "";
            _rewardTmp.text = string.IsNullOrEmpty(info.RewardLine)
                ? (claimed ? "Награда зачислена." : "Заберите награду в разделе «Достижения».")
                : (claimed ? "Получено: " : "Награда: ") + info.RewardLine;
            _panelRt.anchoredPosition = RestPosition;
            _panelRt.localScale = Vector3.one;
        }

        private IEnumerator Animate(float fromAlpha, float toAlpha, Vector2 from, Vector2 to, float duration)
        {
            for (var elapsed = 0f; elapsed < duration; elapsed += Time.unscaledDeltaTime)
            {
                var k = 1 - Mathf.Pow(1 - Mathf.Clamp01(elapsed / duration), 3);
                _panelGroup.alpha = Mathf.Lerp(fromAlpha, toAlpha, k);
                _panelRt.anchoredPosition = Vector2.Lerp(from, to, k);
                yield return null;
            }
            _panelGroup.alpha = toAlpha;
            _panelRt.anchoredPosition = to;
        }

        private void BuildUi()
        {
            // Never borrow scene canvases or a panel's visibility/geometry. Battle,
            // results and navigation can rebuild their own hierarchy independently.
            _internalCanvas = ExpeditionUi.Ensure<Canvas>(gameObject);
            _internalCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _internalCanvas.overrideSorting = true;
            _internalCanvas.sortingOrder = 6200;
            var scaler = ExpeditionUi.Ensure<CanvasScaler>(gameObject);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = .5f;
            var raycaster = GetComponent<GraphicRaycaster>();
            if (raycaster != null) raycaster.enabled = false;
            _safeArea = transform.Find("ToastSafeArea") as RectTransform;
            if (_safeArea == null) _safeArea = ExpeditionUi.Rect(transform, "ToastSafeArea", 0, 0, 1, 1);
            ExpeditionUi.Ensure<ExpeditionSafeArea>(_safeArea.gameObject);
            var background = _safeArea.Find("ToastPanel")?.GetComponent<Image>();
            if (background == null) background = ExpeditionUi.Panel(_safeArea, "ToastPanel", .055f, .918f, .945f, .918f, ExpeditionTheme.Teal);
            _panelRt = background.rectTransform;
            _panelRt.pivot = new Vector2(.5f, 1);
            _panelRt.sizeDelta = new Vector2(0, 176);
            _panelRt.anchoredPosition = RestPosition;
            _panelGroup = ExpeditionUi.Ensure<CanvasGroup>(_panelRt.gameObject);
            _panelGroup.alpha = 0;
            _panelGroup.interactable = _panelGroup.blocksRaycasts = false;
            var icon = _panelRt.Find("AchievementIcon")?.GetComponent<Image>();
            if (icon == null) icon = ExpeditionUi.Fill(_panelRt, "AchievementIcon", .025f, .22f, .16f, .83f, Color.white);
            icon.sprite = ExpeditionTheme.Icon("achievements");
            icon.preserveAspect = true;
            _headlineTmp = _panelRt.Find("ToastHeadline")?.GetComponent<TMP_Text>();
            if (_headlineTmp == null) _headlineTmp = ExpeditionUi.Text(_panelRt, "ToastHeadline", "", 22, .18f, .74f, .97f, .94f);
            _headlineTmp.color = ExpeditionTheme.Amber;
            _titleTmp = _panelRt.Find("ToastTitle")?.GetComponent<TMP_Text>();
            if (_titleTmp == null) _titleTmp = ExpeditionUi.Text(_panelRt, "ToastTitle", "", 32, .18f, .31f, .97f, .75f);
            _rewardTmp = _panelRt.Find("ToastRewardLine")?.GetComponent<TMP_Text>();
            if (_rewardTmp == null) _rewardTmp = ExpeditionUi.Text(_panelRt, "ToastRewardLine", "", 23, .18f, .07f, .97f, .32f);
            _rewardTmp.color = ExpeditionTheme.Muted;
            foreach (var text in new[] { _headlineTmp, _titleTmp, _rewardTmp })
            {
                text.enableAutoSizing = true;
                text.fontSizeMax = text.fontSize;
                text.fontSizeMin = text == _titleTmp ? 25 : 20;
                text.raycastTarget = false;
            }
        }
    }
}
