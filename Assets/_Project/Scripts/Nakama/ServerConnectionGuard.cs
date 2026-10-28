using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Nakama
{
    /// <summary>One acknowledged WebSocket heartbeat owns connection status, matchmaking access and combat input.</summary>
    public sealed class ServerConnectionGuard : MonoBehaviour
    {
        public const string UnavailableMessage = "Нет связи с сервером. Проверьте интернет — игра временно недоступна.";
        private static ServerConnectionGuard _instance;
        private readonly ServerConnectionLease _lease = new();
        private readonly SemaphoreSlim _probeGate = new(1, 1);
        private CancellationTokenSource _lifetime;
        private NakamaBootstrap _bootstrap;
        private float _nextAttempt;
        private bool _checking, _wasOnline, _lastOnline;
        private bool _observedFailure;
        private Canvas _canvas;
        private RectTransform _banner;
        private TMP_Text _message;
        public static event Action<bool> Changed;
        public static bool CanPlay => _instance != null && _instance.Available;
        private bool Available => _bootstrap != null && _bootstrap.GameplaySessionAllowed &&
            Application.internetReachability != NetworkReachability.NotReachable &&
            _lease.Allows(_bootstrap.Socket, _bootstrap.IsReady, Time.realtimeSinceStartupAsDouble);
        [Serializable] private sealed class Ping { public bool ok; public int session_epoch; }

        public static void Install(NakamaBootstrap bootstrap)
        {
            if (_instance != null) return;
            var guard = bootstrap.gameObject.AddComponent<ServerConnectionGuard>();
            guard._bootstrap = bootstrap;
        }
        private void Awake() { _instance = this; _lifetime = new CancellationTokenSource(); }
        private void OnDestroy()
        {
            _lifetime?.Cancel(); _lifetime?.Dispose();
            if (_canvas != null) Destroy(_canvas.gameObject);
            if (_instance == this) { _instance = null; Changed?.Invoke(false); }
        }
        public static void ShowUnavailable()
        {
            if (_instance == null) return;
            _instance._nextAttempt = 0;
            _instance.UpdateBanner(true);
            Project.Audio.ExpeditionAudio.Play(Project.Audio.ExpeditionCue.UiBlocked);
        }
        public static async Task RequireAsync(CancellationToken ct)
        {
            var bootstrap = NakamaBootstrap.Instance;
            var guard = _instance;
            var owner = LocalAccountCacheScope.Current;
            if (bootstrap == null || guard == null) throw new InvalidOperationException(UnavailableMessage);
            try
            {
                await bootstrap.EnsureConnectedAsync(ct);
                await guard.ProbeAsync(ct);
                if (!CanPlay || !ReferenceEquals(bootstrap, NakamaBootstrap.Instance) || !ReferenceEquals(guard, _instance) ||
                    owner != null && !LocalAccountCacheScope.IsCurrent(owner)) throw new InvalidOperationException(UnavailableMessage);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                ShowUnavailable();
                throw new InvalidOperationException(UnavailableMessage);
            }
        }
        private void Update()
        {
            if (_bootstrap == null) return;
            var online = Available;
            if (!online && (_bootstrap.Socket == null || !_bootstrap.IsReady)) _lease.Revoke();
            if (online != _lastOnline) { _lastOnline = online; Changed?.Invoke(online); }
            if (online) _wasOnline = true;
            UpdateBanner(false);
            if (!_checking && Time.realtimeSinceStartup >= _nextAttempt && _bootstrap.Config != null && _bootstrap.GameplaySessionAllowed)
            {
                _nextAttempt = Time.realtimeSinceStartup + 4;
                _ = ReconnectAndProbeAsync();
            }
        }
        private async Task ReconnectAndProbeAsync()
        {
            _checking = true;
            try
            {
                if (Application.internetReachability == NetworkReachability.NotReachable)
                {
                    _observedFailure = true;
                    _lease.Revoke();
                    _bootstrap.InvalidateGameplaySocket(_bootstrap.Socket);
                    return;
                }
                if (!_bootstrap.IsReady) await _bootstrap.EnsureConnectedAsync(_lifetime.Token);
                await ProbeAsync(_lifetime.Token);
                if (_bootstrap.InitialSetupFailed) _ = Observe(_bootstrap.CompleteInitialSetupAsync(_lifetime.Token));
            }
            catch (OperationCanceledException) { }
            catch { _observedFailure = true; _lease.Revoke(); }
            finally { _checking = false; _nextAttempt = Time.realtimeSinceStartup + 4; }
        }
        private async Task ProbeAsync(CancellationToken ct)
        {
            await _probeGate.WaitAsync(ct);
            ISocket socket = null;
            Task<IApiRpc> request = null;
            try
            {
                socket = _bootstrap.Socket;
                var session = _bootstrap.Session;
                if (socket == null || !socket.IsConnected || session == null) throw new InvalidOperationException(UnavailableMessage);
                if (_lease.Fresh(socket, true, Time.realtimeSinceStartupAsDouble)) return;
                request = socket.RpcAsync("duel_online_ping_and_count", "{}");
                var winner = await Task.WhenAny(request, Task.Delay(3000, ct));
                ct.ThrowIfCancellationRequested();
                if (winner != request) throw new TimeoutException("Server WebSocket heartbeat timed out.");
                var response = await request;
                var ping = JsonUtility.FromJson<Ping>(response?.Payload ?? "{}");
                if (ping == null || !ping.ok || !ReferenceEquals(socket, _bootstrap.Socket) || !ReferenceEquals(session, _bootstrap.Session))
                    throw new InvalidOperationException(UnavailableMessage);
                _bootstrap.AcceptGameplayPing(response.Payload);
                if (!_bootstrap.GameplaySessionAllowed) throw new InvalidOperationException(UnavailableMessage);
                _lease.Confirm(socket, Time.realtimeSinceStartupAsDouble);
                _observedFailure = false;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { if (request != null) _ = Observe(request); throw; }
            catch
            {
                _observedFailure = true;
                _lease.Revoke();
                if (request != null) _ = Observe(request);
                _bootstrap.InvalidateGameplaySocket(socket);
                throw;
            }
            finally { _probeGate.Release(); }
        }
        private static async Task Observe(Task task) { try { await task; } catch { } }
        private void UpdateBanner(bool force)
        {
            var tutorial = SceneManager.GetActiveScene().name == "Tutorial";
            var visible = ShouldShowNotice(Available, force, _observedFailure, _wasOnline, tutorial);
            if (!visible && _banner == null) return;
            if (_banner == null)
            {
                var root = new GameObject("ServerConnectionNotice", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
                DontDestroyOnLoad(root);
                _canvas = root.GetComponent<Canvas>(); _canvas.renderMode = RenderMode.ScreenSpaceOverlay; _canvas.sortingOrder = 30000;
                var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
                var safe = ExpeditionUi.Rect(root.transform, "SafeArea", 0, 0, 1, 1); safe.gameObject.AddComponent<ExpeditionSafeArea>();
                _banner = ExpeditionUi.Panel(safe, "ConnectionBanner", .055f, .855f, .945f, .927f).rectTransform;
                _banner.GetComponent<Image>().raycastTarget = false;
                _message = ExpeditionUi.Text(_banner, "ConnectionStatus", "", 26, .035f, .08f, .965f, .92f, TextAlignmentOptions.Center);
                _message.raycastTarget = false; _message.enableAutoSizing = true; _message.fontSizeMin = 22; _message.fontSizeMax = 26;
            }
            _banner.gameObject.SetActive(visible);
            if (visible)
            {
                _message.text = _wasOnline ? "Связь с сервером потеряна.\nДействия приостановлены. Переподключаемся…" : "Нет связи с сервером.\nПодключаемся… PvP и PvE временно недоступны.";
                _message.color = _wasOnline ? new Color32(255, 151, 128, 255) : ExpeditionTheme.Amber;
            }
        }
        private static bool ShouldShowNotice(bool online, bool force, bool failed, bool wasOnline, bool tutorial) =>
            !tutorial && (force || !online && (failed || wasOnline));
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _instance = null; Changed = null; }
    }
}
