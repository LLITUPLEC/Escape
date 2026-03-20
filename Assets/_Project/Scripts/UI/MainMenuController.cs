using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.UI
{
    public sealed class MainMenuController : MonoBehaviour
    {
        [SerializeField] private Button duelButton;
        [SerializeField] private Button botsButton;
        [Header("Online Badge")]
        [SerializeField] private RectTransform onlineBadgeParent;
        [SerializeField] private GameObject onlineBadgePrefab;
        [SerializeField] private string onlineBadgeResourcePath = "OnlinePlayersBadge";
        [SerializeField] private float onlinePollSeconds = 5f;

        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcOnlineLeave = "duel_online_leave";
        private Text _onlineCountText;
        private CancellationTokenSource _onlineCts;
        private GameObject _onlineBadgeInstance;
        private RectTransform _onlineBadgeRect;
        private int _lastOnlineCount = -1;
        private Coroutine _badgePulseRoutine;

        public void Bind(Button duel, Button bots)
        {
            duelButton = duel;
            botsButton = bots;
        }

        private void Awake()
        {
            if (duelButton != null) duelButton.onClick.AddListener(OnDuelClicked);
            if (botsButton != null) botsButton.onClick.AddListener(OnBotsClicked);
            EnsureOnlineBadge();
        }

        private void OnEnable()
        {
            _onlineCts = new CancellationTokenSource();
            _ = OnlineLoopAsync(_onlineCts.Token);
        }

        private void OnDisable()
        {
            _onlineCts?.Cancel();
            _onlineCts?.Dispose();
            _onlineCts = null;
            if (_badgePulseRoutine != null)
            {
                StopCoroutine(_badgePulseRoutine);
                _badgePulseRoutine = null;
            }
            if (_onlineBadgeRect != null)
                _onlineBadgeRect.localScale = Vector3.one;
            _ = NotifyLeaveAsync();
        }

        private void OnDuelClicked()
        {
            SceneManager.LoadScene("DuelRoom");
        }

        private void OnBotsClicked()
        {
            Debug.Log("Режим 'Боты' пока заглушка.");
        }

        private void EnsureOnlineBadge()
        {
            if (_onlineCountText != null) return;

            if (onlineBadgePrefab == null && !string.IsNullOrEmpty(onlineBadgeResourcePath))
            {
                onlineBadgePrefab = Resources.Load<GameObject>(onlineBadgeResourcePath);
            }

            if (onlineBadgePrefab == null) return;

            var parent = onlineBadgeParent != null ? onlineBadgeParent : FindCanvasRoot();
            if (parent == null) return;

            if (_onlineBadgeInstance == null)
            {
                _onlineBadgeInstance = Instantiate(onlineBadgePrefab, parent, false);
                _onlineBadgeInstance.name = "OnlinePlayersBadge";
            }
            _onlineBadgeRect = _onlineBadgeInstance.transform as RectTransform;

            var allTexts = _onlineBadgeInstance.GetComponentsInChildren<Text>(true);
            foreach (var t in allTexts)
            {
                if (t != null && string.Equals(t.gameObject.name, "CountText", StringComparison.Ordinal))
                {
                    _onlineCountText = t;
                    break;
                }
            }
            if (_onlineCountText != null)
                _onlineCountText.text = "—";
        }

        private async Task OnlineLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                await RefreshOnlineCountAsync(ct);
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(1f, onlinePollSeconds)), ct);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task RefreshOnlineCountAsync(CancellationToken ct)
        {
            EnsureOnlineBadge();
            if (_onlineCountText == null) return;

            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    _onlineCountText.text = "—";
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady)
                {
                    _onlineCountText.text = "—";
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcOnlinePingAndCount, "{}");

                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    _onlineCountText.text = "—";
                    return;
                }

                var model = JsonUtility.FromJson<OnlineCountRpcResponse>(payload);
                if (model == null || !model.ok)
                {
                    _onlineCountText.text = "—";
                    return;
                }

                var count = Mathf.Max(1, model.count);
                _onlineCountText.text = count.ToString();
                if (_lastOnlineCount >= 0 && _lastOnlineCount != count)
                {
                    TriggerBadgePulse();
                }
                _lastOnlineCount = count;
            }
            catch
            {
                _onlineCountText.text = "—";
            }
        }

        private async Task NotifyLeaveAsync()
        {
            try
            {
                if (NakamaBootstrap.Instance == null || !NakamaBootstrap.Instance.IsReady) return;
                await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcOnlineLeave, "{}");
            }
            catch
            {
                // ignore
            }
        }

        private static RectTransform FindCanvasRoot()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            return canvas != null ? canvas.transform as RectTransform : null;
        }

        private void TriggerBadgePulse()
        {
            if (_onlineBadgeRect == null) return;
            if (_badgePulseRoutine != null)
                StopCoroutine(_badgePulseRoutine);
            _badgePulseRoutine = StartCoroutine(BadgePulseRoutine());
        }

        private System.Collections.IEnumerator BadgePulseRoutine()
        {
            if (_onlineBadgeRect == null) yield break;
            var startScale = _onlineBadgeRect.localScale;
            var peakScale = startScale * 1.12f;
            var up = 0f;
            const float upDur = 0.14f;
            while (up < upDur)
            {
                up += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(up / upDur);
                _onlineBadgeRect.localScale = Vector3.Lerp(startScale, peakScale, t);
                yield return null;
            }

            var down = 0f;
            const float downDur = 0.22f;
            while (down < downDur)
            {
                down += Time.unscaledDeltaTime;
                var t = Mathf.Clamp01(down / downDur);
                _onlineBadgeRect.localScale = Vector3.Lerp(peakScale, startScale, t);
                yield return null;
            }

            _onlineBadgeRect.localScale = startScale;
            _badgePulseRoutine = null;
        }

        [Serializable]
        private sealed class OnlineCountRpcResponse
        {
            public bool ok;
            public int count;
            public string err;
        }
    }
}

