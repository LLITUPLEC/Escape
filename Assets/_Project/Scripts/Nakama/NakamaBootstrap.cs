using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Achievements;
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

        /// <summary>
        /// Создаёт bootstrap при открытии сцены без главного меню; конфиг подгружается из Resources в <see cref="Start"/>.
        /// </summary>
        public static void EnsureExists()
        {
            if (Instance != null) return;
            var go = new GameObject("NakamaBootstrap");
            go.AddComponent<NakamaBootstrap>();
        }

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
        /// <summary>Дубль DeviceID на диске — переживает часть «очисток кэша», в отличие от одного только PlayerPrefs.</summary>
        private const string DeviceIdFileName = "nakama_device_id.txt";
        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcSessionEpochGet = "duel_session_epoch_get";
        private const string PrefUseEmailSession = "nakama.use_email_session";
        private const string PrefAuthToken = "nakama.session.auth_token";
        private const string PrefRefreshToken = "nakama.session.refresh_token";
        public const string PrefKnownLinkedEmail = "nakama.ui.known_linked_email";
        /// <summary>После удаления аккаунта / гость без email: при запуске обязателен ввод e-mail/пароля.</summary>
        public const string PrefForceEmailSetup = "nakama.force_email_setup";
        private const string RpcAccountWipe = "duel_account_wipe";
        private const string PlayerUsernamePrefix = "Player_";
        private const int PlayerUsernameSuffixLength = 10;
        private const int PlayerUsernameMaxLength = 17;
        public const string SessionEpochLocalPrefKey = "nakama.session_epoch.local";
        private const long NotificationCodeSessionReplaced = 10001;

        /// <summary>
        /// Nakama C# client авто-рефрешит сессию, если она истекает примерно в ближайшие 5 минут.
        /// Bootstrap должен обновлять сессию с тем же запасом, иначе RPC падают с «Refresh token invalid».
        /// </summary>
        private static readonly TimeSpan SessionRefreshSkew = TimeSpan.FromMinutes(10);

        [SerializeField] private bool keepOnlineHeartbeat = true;
        [SerializeField] private float onlineHeartbeatSeconds = 5f;

        /// <summary>Старт меню вызывает EnsureConnectedAsync из нескольких async-цепочек сразу; без блокировки каждая создаёт сокет и даёт несколько «Socket connected».</summary>
        private readonly SemaphoreSlim _ensureConnectedGate = new(1, 1);

        private CancellationTokenSource _cts;
        private volatile bool _skipOnlineHeartbeat;
        private bool _sessionReplacedFlowActive;
        private string _socketBoundAuthToken;

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
                // Сразу затемняем UI, если похоже на гостя / post-wipe — иначе 3–5 с MainMenu с «—».
                var needEarlyOverlay = PlayerPrefs.GetInt(PrefForceEmailSetup, 0) != 0
                    || (PlayerPrefs.GetInt(PrefUseEmailSession, 0) == 0
                        && string.IsNullOrWhiteSpace(PlayerPrefs.GetString(PrefKnownLinkedEmail, "")));
                if (needEarlyOverlay)
                    AuthSetupGate.EnsureBlockingOverlayVisible();

                await EnsureConnectedAsync(_cts.Token);
                await MaybeShowEmailSetupGateAsync(_cts.Token).ConfigureAwait(true);
                _ = AchievementCatalogService.RefreshOnLoginAsync(_cts.Token);
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
                    !Session.HasExpired(DateTime.UtcNow.Add(SessionRefreshSkew)) &&
                    string.Equals(_socketBoundAuthToken, Session.AuthToken, StringComparison.Ordinal))
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
                if (Session == null)
                    throw new InvalidOperationException("Не удалось получить сессию Nakama.");

                var sessionMatchesSocket = Socket != null && Socket.IsConnected &&
                    string.Equals(_socketBoundAuthToken, Session.AuthToken, StringComparison.Ordinal) &&
                    !Session.HasExpired(DateTime.UtcNow.Add(SessionRefreshSkew));
                if (sessionMatchesSocket)
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
                    _socketBoundAuthToken = null;
                }

                Socket = CreateAndWireSocket();
                ct.ThrowIfCancellationRequested();
                await SyncSessionEpochFromServerAsync(ct).ConfigureAwait(false);
                await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
                _socketBoundAuthToken = Session?.AuthToken;
            }
            finally
            {
                _ensureConnectedGate.Release();
            }
        }

        /// <summary>Привязать e-mail к текущему user_id и сохранить вход по e-mail (+ привязать device id).</summary>
        public async Task LinkEmailAsync(string email, string password, CancellationToken ct)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (Session == null || Client == null)
                throw new InvalidOperationException("Нет активной сессии Nakama.");
            await Client.LinkEmailAsync(Session, email, password, canceller: ct).ConfigureAwait(false);
            // Переключаем локальную сессию на email-токены и линкуем device → тот же user_id на новых телефонах.
            await LoginWithEmailAsync(email, password, create: false, ct).ConfigureAwait(false);
        }

        /// <summary>Вход по e-mail (другой телефон или сброс). Сохраняет токены и привязывает device id текущего телефона.</summary>
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
            await MainThreadDispatcher.RunAsync(() =>
            {
                PersistEmailSessionSync(Session);
                PlayerPrefs.SetString(PrefKnownLinkedEmail, email ?? "");
                PlayerPrefs.DeleteKey(PrefForceEmailSetup);
                PlayerPrefs.Save();
            }).ConfigureAwait(false);

            await TryLinkCurrentDeviceAsync(ct).ConfigureAwait(false);
            await SyncSessionEpochFromServerAsync(ct).ConfigureAwait(false);

            Socket = CreateAndWireSocket();
            await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
            _ = AchievementCatalogService.RefreshOnLoginAsync(ct);
        }

        /// <summary>
        /// Привязать текущий device id к аккаунту сессии.
        /// После этого AuthenticateDevice на этом телефоне вернёт тот же user_id, что и email (а не «гостя»).
        /// </summary>
        private async Task TryLinkCurrentDeviceAsync(CancellationToken ct)
        {
            if (Session == null || Client == null) return;
            try
            {
                var deviceId = await MainThreadDispatcher.RunAsync(GetDeviceId).ConfigureAwait(false);
                if (string.IsNullOrEmpty(deviceId)) return;
                await Client.LinkDeviceAsync(Session, deviceId, canceller: ct).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Уже привязан / конфликт — не критично: email-сессия всё равно сохранена.
                if (config != null && config.verboseLogging)
                    Debug.LogWarning("[Nakama] LinkDevice: " + e.Message);
            }
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
            _ = AchievementCatalogService.RefreshOnLoginAsync(ct);
        }

        /// <summary>
        /// Удаляет прогресс игрока на сервере, чистит локальные prefs сессии
        /// и завершает приложение. DeviceID оставляем стабильным: после account_delete
        /// тот же device создаст новый пустой аккаунт (без плодения gen- id).
        /// Следующий запуск — обязателен e-mail.
        /// </summary>
        public async Task WipeAccountAndQuitAsync(CancellationToken ct)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (Session == null || Client == null)
                throw new InvalidOperationException("Нет активной сессии Nakama.");

            var rpc = await Client.RpcAsync(Session, RpcAccountWipe, "{}", canceller: ct).ConfigureAwait(false);
            var payload = rpc?.Payload ?? "";
            if (payload.IndexOf("\"ok\":true", StringComparison.Ordinal) < 0 &&
                payload.IndexOf("\"ok\": true", StringComparison.Ordinal) < 0)
            {
                throw new InvalidOperationException("Сервер не подтвердил удаление аккаунта: " + payload);
            }

            await MainThreadDispatcher.RunAsync(() =>
            {
                ClearAllLocalAuthPrefsSync();
                // Не генерируем случайный gen-: иначе после очистки кэша снова native hex → второй аккаунт.
                // account_delete_id уже снял device с сервера; тот же DeviceID создаст новый user.
                PlayerPrefs.SetInt(PrefForceEmailSetup, 1);
                PlayerPrefs.Save();
            }).ConfigureAwait(false);

            try
            {
                if (Socket != null && Socket.IsConnected)
                    await Socket.CloseAsync();
            }
            catch
            {
                // ignored
            }

            Session = null;
            Socket = null;

            await MainThreadDispatcher.RunAsync(() =>
            {
#if UNITY_EDITOR
                EditorApplication.isPlaying = false;
#else
                Application.Quit();
#endif
            }).ConfigureAwait(false);
        }

        /// <summary>
        /// Гейт регистрации: после wipe (флаг) или если у текущего аккаунта на сервере нет email.
        /// Повторный вход с тем же DeviceID без привязки email снова покажет окно — без нового аккаунта.
        /// </summary>
        private async Task MaybeShowEmailSetupGateAsync(CancellationToken ct)
        {
            var force = await MainThreadDispatcher.RunAsync(() => PlayerPrefs.GetInt(PrefForceEmailSetup, 0) != 0)
                .ConfigureAwait(false);

            var hasEmail = false;
            try
            {
                hasEmail = await AccountHasLinkedEmailAsync(ct).ConfigureAwait(false);
            }
            catch (Exception e) when (IsRefreshTokenFailure(e))
            {
                try
                {
                    await RecoverSessionAfterRefreshFailureAsync(ct).ConfigureAwait(false);
                    hasEmail = await AccountHasLinkedEmailAsync(ct).ConfigureAwait(false);
                }
                catch (Exception e2)
                {
                    if (config != null && config.verboseLogging)
                        Debug.LogWarning("[Nakama] GetAccount (email gate) after recover: " + e2.Message);
                    if (!force)
                    {
                        AuthSetupGate.HideBlockingOverlayIfAny();
                        return;
                    }
                }
            }
            catch (Exception e)
            {
                if (config != null && config.verboseLogging)
                    Debug.LogWarning("[Nakama] GetAccount (email gate): " + e.Message);
                if (!force)
                {
                    AuthSetupGate.HideBlockingOverlayIfAny();
                    return;
                }
            }

            if (!force && hasEmail)
            {
                AuthSetupGate.HideBlockingOverlayIfAny();
                return;
            }

            await AuthSetupGate.ShowAndWaitAsync(this, ct).ConfigureAwait(true);
        }

        private async Task<bool> AccountHasLinkedEmailAsync(CancellationToken ct)
        {
            if (Client == null || Session == null)
                return false;
            var acc = await Client.GetAccountAsync(Session, canceller: ct).ConfigureAwait(false);
            if (!string.IsNullOrWhiteSpace(acc?.Email))
                return true;
            // На всякий случай — Email иногда только в custom fields User.
            var user = acc?.User;
            if (user == null) return false;
            foreach (var p in user.GetType().GetProperties())
            {
                if (!string.Equals(p.Name, "Email", StringComparison.OrdinalIgnoreCase))
                    continue;
                var v = p.GetValue(user) as string;
                return !string.IsNullOrWhiteSpace(v);
            }
            return false;
        }

        private static void ClearAllLocalAuthPrefsSync()
        {
            ClearEmailSessionPrefsSync();
            PlayerPrefs.DeleteKey(PrefKnownLinkedEmail);
            PlayerPrefs.DeleteKey(SessionEpochLocalPrefKey);
            PlayerPrefs.Save();
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

        private async Task MaintainValidSessionAsync(CancellationToken ct)
        {
            if (Session != null && Session.HasExpired(DateTime.UtcNow.Add(SessionRefreshSkew)))
            {
                if (!string.IsNullOrEmpty(Session.RefreshToken) &&
                    !Session.HasRefreshExpired(DateTime.UtcNow.Add(SessionRefreshSkew)))
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
                        if (config != null && config.verboseLogging)
                            Debug.LogWarning($"[Nakama] Не удалось обновить сессию: {e.Message}");
                        await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
                        Session = null;
                    }
                }
                else
                {
                    // Refresh протух/отсутствует — email-токены больше не используем.
                    if (!string.IsNullOrEmpty(Session.RefreshToken) ||
                        await MainThreadDispatcher.RunAsync(() => PlayerPrefs.GetInt(PrefUseEmailSession, 0) != 0)
                            .ConfigureAwait(false))
                    {
                        await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
                    }
                    Session = null;
                }
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
                if (restored != null && !restored.HasExpired(DateTime.UtcNow.Add(SessionRefreshSkew)))
                {
                    // Auth ещё жив с запасом под AutoRefresh — можно использовать.
                    if (!string.IsNullOrEmpty(restored.RefreshToken) &&
                        !restored.HasRefreshExpired(DateTime.UtcNow.Add(SessionRefreshSkew)))
                        return restored;
                    // Refresh уже мёртв: не отдаём сессию в AutoRefresh — уйдём на device.
                    await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
                }
                else if (restored != null && !string.IsNullOrEmpty(restored.RefreshToken) &&
                         !restored.HasRefreshExpired(DateTime.UtcNow.Add(SessionRefreshSkew)))
                {
                    try
                    {
                        var refreshed = await Client.SessionRefreshAsync(restored, canceller: ct).ConfigureAwait(false);
                        await MainThreadDispatcher.RunAsync(() => PersistEmailSessionSync(refreshed)).ConfigureAwait(false);
                        return refreshed;
                    }
                    catch (Exception e)
                    {
                        if (config != null && config.verboseLogging)
                            Debug.LogWarning($"[Nakama] Не удалось восстановить сессию по refresh: {e.Message}");
                        await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
                    }
                }
                else
                {
                    // И auth, и refresh плохие — чистим, дальше device.
                    await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
                }
            }

            var deviceId = await MainThreadDispatcher.RunAsync(GetDeviceId).ConfigureAwait(false);
            var deviceSession = await AuthenticateDeviceWithGeneratedUsernameAsync(deviceId, ct).ConfigureAwait(false);
            // Device, привязанный через LinkDevice, вернёт тот же user_id; сохраняем свежие токены.
            await MainThreadDispatcher.RunAsync(() => PersistEmailSessionSync(deviceSession)).ConfigureAwait(false);
            return deviceSession;
        }

        /// <summary>
        /// После сбоя AutoRefresh («Refresh token invalid») — сброс prefs и переподключение через device.
        /// </summary>
        public async Task RecoverSessionAfterRefreshFailureAsync(CancellationToken ct)
        {
            await MainThreadDispatcher.RunAsync(ClearEmailSessionPrefsSync).ConfigureAwait(false);
            Session = null;
            if (Socket != null)
            {
                try
                {
                    if (Socket.IsConnected)
                        await Socket.CloseAsync().ConfigureAwait(false);
                }
                catch
                {
                    // ignored
                }
                Socket = null;
            }

            await EnsureConnectedAsync(ct).ConfigureAwait(false);
        }

        private static bool IsRefreshTokenFailure(Exception e)
        {
            var msg = e?.Message ?? "";
            return msg.IndexOf("Refresh token", StringComparison.OrdinalIgnoreCase) >= 0
                   || msg.IndexOf("refresh token", StringComparison.OrdinalIgnoreCase) >= 0;
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

        /// <summary>
        /// Стабильный DeviceID: PlayerPrefs → файл в persistentDataPath → native hardware id → gen- (только если native недоступен).
        /// Один и тот же телефон не должен получать новый id после очистки prefs/кэша.
        /// </summary>
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
                {
                    PersistDeviceIdEverywhere(prefKey, saved);
                    return saved;
                }
            }

            var fromFile = TryReadDeviceIdFile(profileKey);
            if (!string.IsNullOrWhiteSpace(fromFile))
            {
                PersistDeviceIdEverywhere(prefKey, fromFile);
                return fromFile;
            }

#if !UNITY_EDITOR
            var nativeId = SanitizeDeviceId(SystemInfo.deviceUniqueIdentifier);
            if (!string.IsNullOrWhiteSpace(nativeId))
            {
                PersistDeviceIdEverywhere(prefKey, nativeId);
                return nativeId;
            }
#endif
            var generated = CreateGeneratedDeviceId(profileKey);
            PersistDeviceIdEverywhere(prefKey, generated);
            return generated;
        }

        private static void PersistDeviceIdEverywhere(string prefKey, string deviceId)
        {
            if (string.IsNullOrWhiteSpace(deviceId)) return;
            PlayerPrefs.SetString(prefKey, deviceId);
            PlayerPrefs.Save();
            TryWriteDeviceIdFile(deviceId);
        }

        private static string GetDeviceIdFilePath(string profileKey)
        {
            var fileName = string.IsNullOrEmpty(profileKey)
                ? DeviceIdFileName
                : $"nakama_device_id.{profileKey}.txt";
            return Path.Combine(Application.persistentDataPath, fileName);
        }

        private static string TryReadDeviceIdFile(string profileKey)
        {
            try
            {
                var path = GetDeviceIdFilePath(profileKey);
                if (!File.Exists(path)) return "";
                var raw = File.ReadAllText(path)?.Trim();
                return string.IsNullOrWhiteSpace(raw) ? "" : raw;
            }
            catch
            {
                return "";
            }
        }

        private static void TryWriteDeviceIdFile(string deviceId)
        {
            try
            {
#if UNITY_EDITOR
                var profileKey = GetDevelopmentProfileKey();
#else
                var profileKey = "";
#endif
                var path = GetDeviceIdFilePath(profileKey);
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, deviceId ?? "");
            }
            catch
            {
                // Best-effort durable backup.
            }
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
