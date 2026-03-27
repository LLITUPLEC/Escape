using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.UI;
using Project.Utils;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.Nakama
{
    public sealed class NakamaBootstrap : MonoBehaviour
    {
        public static NakamaBootstrap Instance { get; private set; }

        public static int GetLocalSessionEpoch() => PlayerPrefs.GetInt(SessionEpochLocalPrefKey, 0);

        [SerializeField] private NakamaConnectionConfig config;
        public NakamaConnectionConfig Config
        {
            get => config;
            set => config = value;
        }

        public IClient Client { get; private set; }
        public ISession Session { get; private set; }
        public ISocket Socket { get; private set; }

        public bool IsReady => Socket != null && Socket.IsConnected && Session != null;

        /// <summary>Только с главного потока. Для фона используйте <see cref="UsesEmailSessionPersistenceAsync"/>.</summary>
        public bool UsesEmailSessionPersistence => PlayerPrefs.GetInt(PrefUseEmailSession, 0) != 0;

        public Task<bool> UsesEmailSessionPersistenceAsync() =>
            MainThreadDispatcher.RunAsync(() => PlayerPrefs.GetInt(PrefUseEmailSession, 0) != 0);

        private const string DeviceIdPrefKey = "nakama.device_id";
        private const string DevDeviceIdPrefPrefix = "nakama.device_id.dev.";
        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcSessionEpochGet = "duel_session_epoch_get";
        private const string PrefUseEmailSession = "nakama.use_email_session";
        private const string PrefAuthToken = "nakama.session.auth_token";
        private const string PrefRefreshToken = "nakama.session.refresh_token";
        private const string PlayerUsernamePrefix = "Player_";
        private const int PlayerUsernameSuffixLength = 10;
        private const int PlayerUsernameMaxLength = 17;
        public const string SessionEpochLocalPrefKey = "nakama.session_epoch.local";
        private const long NotificationCodeSessionReplaced = 10001;

        [SerializeField] private bool keepOnlineHeartbeat = true;
        [SerializeField] private float onlineHeartbeatSeconds = 5f;

        /// <summary>Старт меню вызывает EnsureConnectedAsync из нескольких async-цепочек сразу; без блокировки каждая создаёт сокет и даёт несколько «Socket connected».</summary>
        private readonly SemaphoreSlim _ensureConnectedGate = new(1, 1);

        private CancellationTokenSource _cts;
        private volatile bool _skipOnlineHeartbeat;
        private bool _sessionReplacedFlowActive;

        [Serializable]
        private sealed class OnlinePingRpcResponse
        {
            public bool ok;
            public int count;
            public int session_epoch;
            public string err;
        }

        [Serializable]
        private sealed class SessionEpochRpcResponse
        {
            public bool ok;
            public int session_epoch;
            public string err;
        }

        [Serializable]
        private sealed class SessionEpochNotifContent
        {
            public int session_epoch;
        }

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Application.runInBackground = true;
            QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate <= 0) Application.targetFrameRate = 60;
        }

        private async void Start()
        {
            _cts = new CancellationTokenSource();

            if (config == null)
                config = Resources.Load<NakamaConnectionConfig>("NakamaConnectionConfig");

            if (config == null)
            {
                Debug.LogError("NakamaConnectionConfig не найден. Создайте asset и положите в Resources.");
                return;
            }

            try
            {
                await EnsureConnectedAsync(_cts.Token);
                if (keepOnlineHeartbeat)
                    _ = OnlineHeartbeatLoopAsync(_cts.Token);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        private async Task OnlineHeartbeatLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_skipOnlineHeartbeat)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(2f, onlineHeartbeatSeconds)), ct);
                        continue;
                    }

                    if (IsReady && Client != null && Session != null)
                    {
                        var rpc = await Client.RpcAsync(Session, RpcOnlinePingAndCount, "{}");
                        var payload = rpc?.Payload;
                        if (!string.IsNullOrEmpty(payload))
                            await MainThreadDispatcher.RunAsync(() => TryConsumePingPayloadForSessionEpoch(payload))
                                .ConfigureAwait(false);
                    }
                }
                catch
                {
                    // Best-effort heartbeat. Errors are ignored.
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(Mathf.Max(2f, onlineHeartbeatSeconds)), ct);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private void OnDestroy()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;

            try
            {
                if (Socket != null && Socket.IsConnected)
                    _ = Socket.CloseAsync();
            }
            catch
            {
                // ignored
            }

            if (Instance == this) Instance = null;
        }

        private void OnApplicationQuit()
        {
            try
            {
                if (Socket != null && Socket.IsConnected)
                    _ = Socket.CloseAsync();
            }
            catch
            {
                // ignored
            }
        }

        /// <summary>Подключение к Nakama: единый Client, сессия (e-mail с диска или устройство), сокет.</summary>
        public async Task EnsureConnectedAsync(CancellationToken ct)
        {
            await _ensureConnectedGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (Socket != null && Socket.IsConnected && Session != null &&
                    !Session.HasExpired(DateTime.UtcNow.AddMinutes(1)))
                    return;

                if (config == null)
                {
                    config = Resources.Load<NakamaConnectionConfig>("NakamaConnectionConfig");
                    if (config == null)
                        throw new InvalidOperationException("NakamaConnectionConfig не найден в Resources.");
                }

                if (Client == null)
                {
                    Client = new Client(config.GetScheme(), config.host, config.port, config.serverKey);
                    Client.Timeout = 10;
                }

                await MaintainValidSessionAsync(ct).ConfigureAwait(false);

                if (Socket != null && Socket.IsConnected)
                    return;

                if (Socket != null)
                {
                    try
                    {
                        await Socket.CloseAsync();
                    }
                    catch
                    {
                        // ignored
                    }

                    Socket = null;
                }

                Socket = CreateAndWireSocket();
                ct.ThrowIfCancellationRequested();
                await SyncSessionEpochFromServerAsync(ct).ConfigureAwait(false);
                await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
            }
            finally
            {
                _ensureConnectedGate.Release();
            }
        }

        /// <summary>Привязать e-mail к текущему user_id (прогресс на сервере остаётся тем же).</summary>
        public async Task LinkEmailAsync(string email, string password, CancellationToken ct)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (Session == null || Client == null)
                throw new InvalidOperationException("Нет активной сессии Nakama.");
            await Client.LinkEmailAsync(Session, email, password, canceller: ct).ConfigureAwait(false);
        }

        /// <summary>Вход по e-mail (другой телефон или сброс). Сохраняет токены для следующих запусков (только с main thread).</summary>
        public async Task LoginWithEmailAsync(string email, string password, bool create, CancellationToken ct)
        {
            if (config == null)
            {
                config = Resources.Load<NakamaConnectionConfig>("NakamaConnectionConfig");
                if (config == null)
                    throw new InvalidOperationException("NakamaConnectionConfig не найден.");
            }

            if (Client == null)
            {
                Client = new Client(config.GetScheme(), config.host, config.port, config.serverKey);
                Client.Timeout = 10;
            }

            if (Socket != null)
            {
                try
                {
                    if (Socket.IsConnected)
                        await Socket.CloseAsync();
                }
                catch
                {
                    // ignored
                }

                Socket = null;
            }

            Session = await Client
                .AuthenticateEmailAsync(email, password, username: null, create: create, vars: null, canceller: ct)
                .ConfigureAwait(false);
            await MainThreadDispatcher.RunAsync(() => PersistEmailSessionSync(Session)).ConfigureAwait(false);

            await SyncSessionEpochFromServerAsync(ct).ConfigureAwait(false);

            Socket = CreateAndWireSocket();
            await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
        }

        /// <summary>Сброс только локально сохранённой сессии по e-mail. В консоли Nakama привязка почты к user_id не удаляется.</summary>
        public async Task ClearEmailPersistenceAndReconnectAsync(CancellationToken ct)
        {
            await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
            Session = null;

            if (Socket != null)
            {
                try
                {
                    if (Socket.IsConnected)
                        await Socket.CloseAsync();
                }
                catch
                {
                    // ignored
                }

                Socket = null;
            }

            if (Client == null)
            {
                if (config == null)
                    config = Resources.Load<NakamaConnectionConfig>("NakamaConnectionConfig");
                if (config == null)
                    throw new InvalidOperationException("NakamaConnectionConfig не найден.");
                Client = new Client(config.GetScheme(), config.host, config.port, config.serverKey);
                Client.Timeout = 10;
            }

            Session = await ResolveSessionAsync(ct).ConfigureAwait(false);
            Socket = CreateAndWireSocket();
            await SyncSessionEpochFromServerAsync(ct).ConfigureAwait(false);
            await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
        }

        private ISocket CreateAndWireSocket()
        {
            // WebSocketAdapter вместо WebSocketStdlibAdapter — меньше NRE в ReceiveAsync на Mono/Android.
            // useMainThread: false — без UnitySocket: его Create() делает new GameObject только с главного потока,
            // а EnsureConnectedAsync после await часто продолжается с thread pool.
            // События матча в проекте уже прокидываются в main thread через MainThreadDispatcher где нужно.
            var socket = Client.NewSocket(useMainThread: false, defaultAdapter: new WebSocketAdapter());
            socket.Connected += () =>
            {
                if (config.verboseLogging) Debug.Log("[Nakama] Socket connected");
            };
            socket.Closed += reason =>
            {
                if (config.verboseLogging) Debug.Log($"[Nakama] Socket closed: {reason}");
            };
            socket.ReceivedError += e =>
            {
                Debug.LogError($"[Nakama] Socket error: {e?.Message}");
            };
            socket.ReceivedNotification += OnSocketReceivedNotification;
            return socket;
        }

        private void OnSocketReceivedNotification(IApiNotification notification)
        {
            if (notification == null || notification.Code != NotificationCodeSessionReplaced)
                return;
            var epoch = TryParseEpochFromNotificationContent(notification.Content);
            if (epoch < 0) return;
            MainThreadDispatcher.Enqueue(() => ConsiderSessionReplacedIfEpochNewer(epoch));
        }

        private static int TryParseEpochFromNotificationContent(string content)
        {
            if (string.IsNullOrWhiteSpace(content)) return -1;
            try
            {
                var parsed = JsonUtility.FromJson<SessionEpochNotifContent>(content);
                if (parsed == null) return -1;
                return parsed.session_epoch;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>Сверяет эпоху с сервером до Connect сокета, чтобы не сработала ложная отсылка «вышли».</summary>
        private async Task SyncSessionEpochFromServerAsync(CancellationToken ct)
        {
            if (Session == null || Client == null) return;
            try
            {
                var rpc = await Client.RpcAsync(Session, RpcSessionEpochGet, "{}", canceller: ct).ConfigureAwait(false);
                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload)) return;
                var model = JsonUtility.FromJson<SessionEpochRpcResponse>(payload);
                if (model != null && model.ok)
                    await MainThreadDispatcher
                        .RunAsync(() => PersistLocalSessionEpochSync(model.session_epoch))
                        .ConfigureAwait(false);
            }
            catch
            {
                // Сервер без duel_session.lua — тихо пропускаем.
            }
        }

        private static void PersistLocalSessionEpochSync(int epoch)
        {
            PlayerPrefs.SetInt(SessionEpochLocalPrefKey, Mathf.Max(0, epoch));
            PlayerPrefs.Save();
        }

        private void TryConsumePingPayloadForSessionEpoch(string payload)
        {
            if (string.IsNullOrEmpty(payload) || _sessionReplacedFlowActive) return;
            try
            {
                var m = JsonUtility.FromJson<OnlinePingRpcResponse>(payload);
                if (m == null || !m.ok) return;
                if (!PlayerPrefs.HasKey(SessionEpochLocalPrefKey))
                {
                    PersistLocalSessionEpochSync(m.session_epoch);
                    return;
                }

                var local = PlayerPrefs.GetInt(SessionEpochLocalPrefKey, 0);
                if (m.session_epoch > local)
                {
                    BeginSessionReplacedFlow();
                    return;
                }

                PersistLocalSessionEpochSync(m.session_epoch);
            }
            catch
            {
                // ignore
            }
        }

        private void ConsiderSessionReplacedIfEpochNewer(int serverEpoch)
        {
            if (_sessionReplacedFlowActive) return;
            if (!PlayerPrefs.HasKey(SessionEpochLocalPrefKey))
            {
                PersistLocalSessionEpochSync(serverEpoch);
                return;
            }

            var local = PlayerPrefs.GetInt(SessionEpochLocalPrefKey, 0);
            if (serverEpoch > local) BeginSessionReplacedFlow();
        }

        private void BeginSessionReplacedFlow()
        {
            if (_sessionReplacedFlowActive) return;
            _sessionReplacedFlowActive = true;
            _skipOnlineHeartbeat = true;

            SessionReplacedModal.Show(
                "Под вашим аккаунтом выполнили вход на другом устройстве.\n\n" +
                "Эта сессия больше не считается активной. Чтобы снова играть под этим аккаунтом, войдите по e-mail.\n\n" +
                "Нажмите «ОК», чтобы закрыть игру.",
                () => { _ = QuitAfterSessionReplacedAsync(); });
        }

        private async Task QuitAfterSessionReplacedAsync()
        {
            try
            {
                // Best-effort: close socket before quitting.
                if (Socket != null && Socket.IsConnected)
                {
                    try
                    {
                        await Socket.CloseAsync().ConfigureAwait(false);
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                _sessionReplacedFlowActive = false;
                _skipOnlineHeartbeat = false;
            }

            await MainThreadDispatcher.RunAsync(QuitApplicationSync).ConfigureAwait(false);
        }

        private static void QuitApplicationSync()
        {
#if UNITY_EDITOR
            EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Новый гостевой device id в PlayerPrefs (не полагаемся на hardware id), иначе на телефоне снова тот же Nakama-пользователь.</summary>
        private static void RegenerateDeviceIdentitySync()
        {
#if UNITY_EDITOR
            var profileKey = GetDevelopmentProfileKey();
            var prefKey = DevDeviceIdPrefPrefix + profileKey;
#else
            var profileKey = "";
            var prefKey = DeviceIdPrefKey;
#endif
            var newId = CreateGeneratedDeviceId(profileKey);
            PlayerPrefs.SetString(prefKey, newId);
            PlayerPrefs.Save();
        }

        private async Task MaintainValidSessionAsync(CancellationToken ct)
        {
            if (Session != null && Session.HasExpired(DateTime.UtcNow.AddMinutes(1)))
            {
                if (!string.IsNullOrEmpty(Session.RefreshToken) &&
                    !Session.HasRefreshExpired(DateTime.UtcNow.AddMinutes(1)))
                {
                    try
                    {
                        var refreshed = await Client.SessionRefreshAsync(Session, canceller: ct).ConfigureAwait(false);
                        Session = refreshed;
                        var persist = await MainThreadDispatcher
                            .RunAsync(() => PlayerPrefs.GetInt(PrefUseEmailSession, 0) != 0)
                            .ConfigureAwait(false);
                        if (persist)
                            await MainThreadDispatcher.RunAsync(() => PersistEmailSessionSync(refreshed))
                                .ConfigureAwait(false);
                        return;
                    }
                    catch (Exception e)
                    {
                        if (config.verboseLogging)
                            Debug.LogWarning($"[Nakama] Не удалось обновить сессию: {e.Message}");
                        Session = null;
                    }
                }
                else
                    Session = null;
            }

            if (Session == null)
                Session = await ResolveSessionAsync(ct).ConfigureAwait(false);
        }

        private async Task<ISession> ResolveSessionAsync(CancellationToken ct)
        {
            var (useEmail, auth, refresh) = await MainThreadDispatcher.RunAsync(() =>
                (PlayerPrefs.GetInt(PrefUseEmailSession, 0) != 0,
                    PlayerPrefs.GetString(PrefAuthToken, ""),
                    PlayerPrefs.GetString(PrefRefreshToken, ""))).ConfigureAwait(false);

            if (useEmail && !string.IsNullOrEmpty(auth))
            {
                var restored = global::Nakama.Session.Restore(auth, string.IsNullOrEmpty(refresh) ? null : refresh);
                if (restored != null && !restored.HasExpired(DateTime.UtcNow.AddMinutes(1)))
                    return restored;

                if (restored != null && !string.IsNullOrEmpty(restored.RefreshToken) &&
                    !restored.HasRefreshExpired(DateTime.UtcNow.AddMinutes(1)))
                {
                    try
                    {
                        var refreshed = await Client.SessionRefreshAsync(restored, canceller: ct).ConfigureAwait(false);
                        await MainThreadDispatcher.RunAsync(() => PersistEmailSessionSync(refreshed)).ConfigureAwait(false);
                        return refreshed;
                    }
                    catch (Exception e)
                    {
                        if (config.verboseLogging)
                            Debug.LogWarning($"[Nakama] Не удалось восстановить сессию по refresh: {e.Message}");
                    }
                }

                await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
            }

            var deviceId = await MainThreadDispatcher.RunAsync(GetDeviceId).ConfigureAwait(false);
            return await AuthenticateDeviceWithGeneratedUsernameAsync(deviceId, ct).ConfigureAwait(false);
        }

        private async Task<ISession> AuthenticateDeviceWithGeneratedUsernameAsync(string deviceId, CancellationToken ct)
        {
            const int maxAttempts = 6;
            for (var attempt = 0; attempt < maxAttempts; attempt++)
            {
                var username = CreatePlayerUsername();
                try
                {
                    return await Client.AuthenticateDeviceAsync(
                        deviceId,
                        username: username,
                        create: true,
                        canceller: ct).ConfigureAwait(false);
                }
                catch (ApiResponseException ex) when (ex.StatusCode == 409 && attempt < maxAttempts - 1)
                {
                    // Rare username collision, regenerate and retry.
                }
            }

            // Final attempt: surface any server error to caller.
            return await Client.AuthenticateDeviceAsync(
                deviceId,
                username: CreatePlayerUsername(),
                create: true,
                canceller: ct).ConfigureAwait(false);
        }

        private static string CreatePlayerUsername()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var chars = new char[PlayerUsernameSuffixLength];
            for (var i = 0; i < chars.Length; i++)
                chars[i] = alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];

            var suffix = new string(chars);
            var username = PlayerUsernamePrefix + suffix;
            return username.Length <= PlayerUsernameMaxLength
                ? username
                : username.Substring(0, PlayerUsernameMaxLength);
        }

        private static string GetDeviceId()
        {
#if UNITY_EDITOR
            var profileKey = GetDevelopmentProfileKey();
            var prefKey = DevDeviceIdPrefPrefix + profileKey;
#else
            var profileKey = "";
            var prefKey = DeviceIdPrefKey;
#endif
            if (PlayerPrefs.HasKey(prefKey))
            {
                var saved = PlayerPrefs.GetString(prefKey);
                if (!string.IsNullOrWhiteSpace(saved))
                    return saved;
            }

#if !UNITY_EDITOR
            var nativeId = SanitizeDeviceId(SystemInfo.deviceUniqueIdentifier);
            if (!string.IsNullOrWhiteSpace(nativeId))
            {
                PlayerPrefs.SetString(prefKey, nativeId);
                PlayerPrefs.Save();
                return nativeId;
            }
#endif
            var generated = CreateGeneratedDeviceId(profileKey);
            PlayerPrefs.SetString(prefKey, generated);
            PlayerPrefs.Save();
            return generated;
        }

        private static string CreateGeneratedDeviceId(string profileKey)
        {
            var suffix = Guid.NewGuid().ToString("N");
            var prefix = string.IsNullOrEmpty(profileKey) ? "gen" : $"dev-{profileKey}";
            return $"{prefix}-{suffix}";
        }

        private static string SanitizeDeviceId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "";

            var sb = new StringBuilder(raw.Length);
            foreach (var c in raw)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.')
                    sb.Append(c);
                else
                    sb.Append('-');
            }

            var sanitized = sb.ToString().Trim('-');
            if (sanitized.Length > 128)
                sanitized = sanitized.Substring(0, 128);
            if (sanitized.Length < 10)
                return "";
            return sanitized;
        }

        private static string GetDevelopmentProfileKey()
        {
            var src = Application.dataPath ?? "unknown_path";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(src));
            var sb = new StringBuilder(12);
            for (var i = 0; i < 6; i++)
                sb.Append(hash[i].ToString("x2"));
            return sb.ToString();
        }

        private static void PersistEmailSessionSync(ISession s)
        {
            if (s == null || string.IsNullOrEmpty(s.AuthToken)) return;
            PlayerPrefs.SetInt(PrefUseEmailSession, 1);
            PlayerPrefs.SetString(PrefAuthToken, s.AuthToken);
            PlayerPrefs.SetString(PrefRefreshToken, s.RefreshToken ?? "");
            PlayerPrefs.Save();
        }

        private static void ClearEmailSessionPrefsSync()
        {
            PlayerPrefs.DeleteKey(PrefUseEmailSession);
            PlayerPrefs.DeleteKey(PrefAuthToken);
            PlayerPrefs.DeleteKey(PrefRefreshToken);
            PlayerPrefs.Save();
        }
    }
}
