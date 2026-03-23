using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Match3;
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
        [SerializeField] private Button match3Button;
        [Header("Online Badge")]
        [SerializeField] private RectTransform onlineBadgeParent;
        [SerializeField] private GameObject onlineBadgePrefab;
        [SerializeField] private string onlineBadgeResourcePath = "OnlinePlayersBadge";
        [SerializeField] private float onlinePollSeconds = 5f;
        [SerializeField] private float match3StatsPollSeconds = 5f;

        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcMatch3StatsGet = "duel_match3_stats_get";
        private Text _onlineCountText;
        private CancellationTokenSource _onlineCts;
        private GameObject _onlineBadgeInstance;
        private RectTransform _onlineBadgeRect;
        private int _lastOnlineCount = -1;
        private Coroutine _badgePulseRoutine;
        private RectTransform _match3StatsRoot;
        private Text _match3PlayedText;
        private Text _match3WinsText;
        private Text _match3LossesText;

        public void Bind(Button duel, Button bots, Button match3 = null)
        {
            duelButton   = duel;
            botsButton   = bots;
            match3Button = match3;
        }

        private void Awake()
        {
            if (duelButton   != null) duelButton.onClick.AddListener(OnDuelClicked);
            if (botsButton   != null) botsButton.onClick.AddListener(OnBotsClicked);
            if (match3Button != null) match3Button.onClick.AddListener(OnMatch3Clicked);
            EnsureOnlineBadge();
            EnsureMatch3StatsCard();
        }

        private void OnEnable()
        {
            _onlineCts = new CancellationTokenSource();
            _ = OnlineLoopAsync(_onlineCts.Token);
            _ = RefreshMatch3StatsCardAsync(_onlineCts.Token);
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
        }

        private void OnDuelClicked()
        {
            SceneManager.LoadScene("DuelRoom");
        }

        private void OnBotsClicked()
        {
            Match3LaunchContext.SetMode(Match3LaunchMode.SoloBot);
            SceneManager.LoadScene("DuelMatch3");
        }

        private void OnMatch3Clicked()
        {
            Match3LaunchContext.SetMode(Match3LaunchMode.Multiplayer);
            SceneManager.LoadScene("DuelMatch3");
        }

        private void EnsureMatch3StatsCard()
        {
            var parent = FindCanvasRoot();
            if (parent == null) return;

            if (_match3StatsRoot == null)
            {
                var cardGo = new GameObject("Match3StatsCard");
                _match3StatsRoot = cardGo.AddComponent<RectTransform>();
                _match3StatsRoot.SetParent(parent, false);
            }
            // Keep fixed card size and move it up by 125 px.
            _match3StatsRoot.anchorMin = new Vector2(0.835f, 0.50f);
            _match3StatsRoot.anchorMax = new Vector2(0.835f, 0.50f);
            _match3StatsRoot.pivot = new Vector2(0.5f, 0.5f);
            _match3StatsRoot.sizeDelta = new Vector2(320f, 420f);
            _match3StatsRoot.anchoredPosition = new Vector2(0f, 125f);

            if (_match3PlayedText != null && _match3WinsText != null && _match3LossesText != null)
                return;

            var bg = _match3StatsRoot.gameObject.AddComponent<Image>();
            bg.color = new Color(0.08f, 0.10f, 0.18f, 0.92f);
            var outline = _match3StatsRoot.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.22f, 0.74f, 1f, 0.65f);
            outline.effectDistance = new Vector2(1f, -1f);

            var title = CreateStatsText("Title", _match3StatsRoot, "Три-в-ряд", 24, new Color(0.70f, 0.95f, 1f));
            Anchor(title.rectTransform, new Vector2(0.06f, 0.73f), new Vector2(0.94f, 0.95f), TextAnchor.MiddleCenter);

            var playedLabel = CreateStatsText("PlayedLabel", _match3StatsRoot, "Сыграно:", 18, Color.white);
            Anchor(playedLabel.rectTransform, new Vector2(0.08f, 0.46f), new Vector2(0.62f, 0.68f), TextAnchor.MiddleLeft);
            _match3PlayedText = CreateStatsText("PlayedValue", _match3StatsRoot, "0", 18, new Color(1f, 0.95f, 0.58f));
            Anchor(_match3PlayedText.rectTransform, new Vector2(0.64f, 0.46f), new Vector2(0.92f, 0.68f), TextAnchor.MiddleRight);

            var winsLabel = CreateStatsText("WinsLabel", _match3StatsRoot, "Побед:", 18, Color.white);
            Anchor(winsLabel.rectTransform, new Vector2(0.08f, 0.24f), new Vector2(0.62f, 0.46f), TextAnchor.MiddleLeft);
            _match3WinsText = CreateStatsText("WinsValue", _match3StatsRoot, "0", 18, new Color(0.50f, 1f, 0.50f));
            Anchor(_match3WinsText.rectTransform, new Vector2(0.64f, 0.24f), new Vector2(0.92f, 0.46f), TextAnchor.MiddleRight);

            var lossesLabel = CreateStatsText("LossesLabel", _match3StatsRoot, "Поражений:", 18, Color.white);
            Anchor(lossesLabel.rectTransform, new Vector2(0.08f, 0.02f), new Vector2(0.62f, 0.24f), TextAnchor.MiddleLeft);
            _match3LossesText = CreateStatsText("LossesValue", _match3StatsRoot, "0", 18, new Color(1f, 0.50f, 0.50f));
            Anchor(_match3LossesText.rectTransform, new Vector2(0.64f, 0.02f), new Vector2(0.92f, 0.24f), TextAnchor.MiddleRight);
        }

        private async Task RefreshMatch3StatsCardAsync(CancellationToken ct)
        {
            EnsureMatch3StatsCard();
            if (_match3PlayedText == null || _match3WinsText == null || _match3LossesText == null) return;
            try
            {
                if (NakamaBootstrap.Instance == null)
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
                if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcMatch3StatsGet, "{}");
                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload))
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                var model = JsonUtility.FromJson<Match3StatsRpcResponse>(payload);
                if (model == null || !model.ok)
                {
                    SetMatch3StatsUnknown();
                    return;
                }

                _match3PlayedText.text = Mathf.Max(0, model.played).ToString();
                _match3WinsText.text = Mathf.Max(0, model.wins).ToString();
                _match3LossesText.text = Mathf.Max(0, model.losses).ToString();
            }
            catch
            {
                SetMatch3StatsUnknown();
            }
        }

        private void SetMatch3StatsUnknown()
        {
            if (_match3PlayedText != null) _match3PlayedText.text = "—";
            if (_match3WinsText != null) _match3WinsText.text = "—";
            if (_match3LossesText != null) _match3LossesText.text = "—";
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
            var nextStatsRefreshAt = 0f;
            while (!ct.IsCancellationRequested)
            {
                await RefreshOnlineCountAsync(ct);
                if (Time.unscaledTime >= nextStatsRefreshAt)
                {
                    await RefreshMatch3StatsCardAsync(ct);
                    nextStatsRefreshAt = Time.unscaledTime + Mathf.Max(2f, match3StatsPollSeconds);
                }
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

        private static RectTransform FindCanvasRoot()
        {
            var canvas = FindFirstObjectByType<Canvas>();
            return canvas != null ? canvas.transform as RectTransform : null;
        }

        private static Text CreateStatsText(string name, RectTransform parent, string value, int size, Color color)
        {
            var go = new GameObject(name);
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            var txt = go.AddComponent<Text>();
            txt.font = GetDefaultBuiltinFont();
            txt.fontSize = size;
            txt.color = color;
            txt.text = value;
            txt.raycastTarget = false;
            return txt;
        }

        private static Font GetDefaultBuiltinFont()
        {
            try
            {
                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                if (font != null) return font;
            }
            catch
            {
                // ignore and try legacy fallback below
            }

            try
            {
                return Resources.GetBuiltinResource<Font>("Arial.ttf");
            }
            catch
            {
                return null;
            }
        }

        private static void Anchor(RectTransform rt, Vector2 min, Vector2 max, TextAnchor align)
        {
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var txt = rt.GetComponent<Text>();
            if (txt != null) txt.alignment = align;
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

        [Serializable]
        private sealed class Match3StatsRpcResponse
        {
            public bool ok;
            public int played;
            public int wins;
            public int losses;
            public string err;
        }
    }
}

