using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Achievements;
using Project.UI;
using Project.Utils;
using Project.Nakama.Security;
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

        private static int _localSessionEpoch;
        /// <summary>RPC payloads are also built on worker threads: never touch PlayerPrefs here.</summary>
        public static int GetLocalSessionEpoch() => Volatile.Read(ref _localSessionEpoch);

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
        internal bool GameplaySessionAllowed => !_sessionReplacedFlowActive && !_skipOnlineHeartbeat;
        internal void AcceptGameplayPing(string payload) => TryConsumePingPayloadForSessionEpoch(payload);
        internal void InvalidateGameplaySocket(ISocket expected)
        {
            if (expected == null || !ReferenceEquals(Socket, expected)) return;
            Socket = null; _socketBoundAuthToken = null;
            _ = CloseStaleGameplaySocketAsync(expected);
        }
        private static async Task CloseStaleGameplaySocketAsync(ISocket socket) { try { await socket.CloseAsync(); } catch { } }

        /// <summary>Только с главного потока. Для фона используйте <see cref="UsesEmailSessionPersistenceAsync"/>.</summary>
        public bool UsesEmailSessionPersistence => NakamaCredentialStore.UsesEmail;

        public Task<bool> UsesEmailSessionPersistenceAsync() =>
            MainThreadDispatcher.RunAsync(() => NakamaCredentialStore.UsesEmail);

        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        private const string RpcSessionEpochGet = "duel_session_epoch_get";
        /// <summary>Захват single-device сессии без AuthenticateEmail (silent restore / reconnect).</summary>
        private const string RpcSessionClaim = "duel_session_claim";
        /// <summary>Ключ старых сборок: пароль больше не сохраняется; удаляем при миграции.</summary>
        private const string PrefSavedEmailPassword = "nakama.session.email_password";
        public const string PrefKnownLinkedEmail = "nakama.ui.known_linked_email";
        /// <summary>После удаления аккаунта / гость без email: при запуске обязателен ввод e-mail/пароля.</summary>
        public const string PrefForceEmailSetup = "nakama.force_email_setup";
        private const string RpcAccountWipe = "duel_account_wipe";
        private const string PlayerUsernamePrefix = "Player_";
        private const int PlayerUsernameSuffixLength = 10;
        private const int PlayerUsernameMaxLength = 17;
        public const string SessionEpochLocalPrefKey = "nakama.session_epoch.local";
        private const long NotificationCodeSessionReplaced = 10001;
        private const long NotificationCodeFriendsRaceInvite = 10020;
        private const long NotificationCodeFriendsRaceUpdate = 10021;

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
            ServerConnectionGuard.Install(this);
            Volatile.Write(ref _localSessionEpoch, PlayerPrefs.GetInt(SessionEpochLocalPrefKey, 0));
            DeleteLegacyEmailPasswordSync();
            DontDestroyOnLoad(gameObject);
            Project.Friends.FriendsRaceInviteHost.Ensure();

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
                await ApplyAuthEndpointScopeAsync().ConfigureAwait(true);
                // Сразу затемняем UI, если похоже на гостя / post-wipe — иначе 3–5 с MainMenu с «—».
                var needEarlyOverlay = PlayerPrefs.GetInt(PrefForceEmailSetup, 0) != 0
                    || (!NakamaCredentialStore.UsesEmail && !NakamaCredentialStore.HasKnownEmail);
                if (needEarlyOverlay)
                    AuthSetupGate.EnsureBlockingOverlayVisible();

                // После wipe старой сборки claimed/stats могли остаться в PlayerPrefs — чистим до sync.
                if (PlayerPrefs.GetInt(PrefForceEmailSetup, 0) != 0)
                    AchievementProgressStorage.ClearAllLocalProgress();

                await EnsureConnectedAsync(_cts.Token);
                await MaybeShowEmailSetupGateAsync(_cts.Token).ConfigureAwait(true);
                _ = AchievementCatalogService.RefreshOnLoginAsync(_cts.Token);
                // ServerConnectionGuard sends the same presence/session heartbeat over
                // the actual game WebSocket, with a bounded response timeout.
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
                if (config == null)
                {
                    config = Resources.Load<NakamaConnectionConfig>("NakamaConnectionConfig");
                    if (config == null)
                        throw new InvalidOperationException("NakamaConnectionConfig не найден в Resources.");
                }

                await ApplyAuthEndpointScopeAsync().ConfigureAwait(false);
                if (Socket != null && Socket.IsConnected && Session != null &&
                    !Session.HasExpired(DateTime.UtcNow.Add(SessionRefreshSkew)) &&
                    string.Equals(_socketBoundAuthToken, Session.AuthToken, StringComparison.Ordinal))
                {
                    await Project.Character.AccountGender.RefreshAsync(ct).ConfigureAwait(false);
                    return;
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
                {
                    await Project.Character.AccountGender.RefreshAsync(ct).ConfigureAwait(false);
                    return;
                }

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
                // Silent restore (JWT / refresh / linked device) не вызывает AuthenticateEmail —
                // без claim другое устройство с тем же аккаунтом не вытесняется.
                await ClaimEmailSessionOwnershipAsync(ct).ConfigureAwait(false);
                await SyncSessionEpochFromServerAsync(ct).ConfigureAwait(false);
                await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
                _socketBoundAuthToken = Session?.AuthToken;
                await Project.Character.AccountGender.RefreshAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _ensureConnectedGate.Release();
            }
        }

        /// <summary>Привязать e-mail к текущему user_id и сохранить вход по e-mail (+ привязать device id).</summary>
        public async Task LinkEmailAsync(string email, string password, CancellationToken ct, bool? female = null)
        {
            await EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (Session == null || Client == null)
                throw new InvalidOperationException("Нет активной сессии Nakama.");
            if (female.HasValue)
                await Project.Character.AccountGender.RegisterAsync(female.Value, ct).ConfigureAwait(false);
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

            await ApplyAuthEndpointScopeAsync().ConfigureAwait(false);
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

            var authenticated = await Client
                .AuthenticateEmailAsync(email, password, username: null, create: create, vars: null, canceller: ct)
                .ConfigureAwait(false);
            await MainThreadDispatcher.RunAsync(() =>
            {
                PersistEmailSessionSync(authenticated);
                PersistKnownLinkedEmailSync(email);
                PlayerPrefs.DeleteKey(PrefForceEmailSetup);
                PlayerPrefs.Save();
            }).ConfigureAwait(false);
            Session = authenticated;

            await TryLinkCurrentDeviceAsync(ct).ConfigureAwait(false);
            await SyncSessionEpochFromServerAsync(ct).ConfigureAwait(false);

            Socket = CreateAndWireSocket();
            await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
            _socketBoundAuthToken = Session?.AuthToken;
            await Project.Character.AccountGender.RefreshAsync(ct, force: true).ConfigureAwait(false);
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
            catch (CredentialStorageException) { throw; }
            catch (Exception e)
            {
                // Уже привязан / конфликт — не критично: email-сессия всё равно сохранена.
                if (config != null && config.verboseLogging)
                    Debug.LogWarning("[Nakama] LinkDevice: " + e.Message);
            }
        }

        /// <summary>
        /// Сброс локально сохранённого входа по e-mail (токены + почта/пароль).
        /// В консоли Nakama привязка почты к user_id не удаляется.
        /// </summary>
        public async Task ClearEmailPersistenceAndReconnectAsync(CancellationToken ct)
        {
            await MainThreadDispatcher.RunAsync(ClearAllEmailPersistenceSync).ConfigureAwait(false);
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
            _socketBoundAuthToken = Session?.AuthToken;
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
        /// Гейт регистрации: после wipe (флаг) или если на устройстве ещё не было успешного email-setup
        /// и у текущего аккаунта нет email.
        /// </summary>
        private async Task MaybeShowEmailSetupGateAsync(CancellationToken ct)
        {
            var force = await MainThreadDispatcher.RunAsync(() =>
                PlayerPrefs.GetInt(PrefForceEmailSetup, 0) != 0).ConfigureAwait(false);

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

            // У текущего аккаунта ещё нет e-mail, либо после wipe требуется новая регистрация.
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
            ClearAllEmailPersistenceSync();
            PlayerPrefs.DeleteKey(SessionEpochLocalPrefKey);
            Volatile.Write(ref _localSessionEpoch, 0);
            // Локальные claimed/stats достижений иначе переживают wipe и «горят» после нового аккаунта.
            AchievementProgressStorage.ClearAllLocalProgress();
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
            if (notification == null) return;

            if (notification.Code == NotificationCodeFriendsRaceInvite
                || notification.Code == NotificationCodeFriendsRaceUpdate)
            {
                Project.Friends.FriendsRaceInviteHost.Ensure()
                    .HandleNotification(notification.Code, notification.Content);
                return;
            }

            if (notification.Code != NotificationCodeSessionReplaced)
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

        /// <summary>
        /// Захват single-device сессии при silent-входе (сохранённые email-токены / linked device).
        /// AuthenticateEmail уже бампит эпоху на сервере — здесь покрываем Restore/Refresh без повторного логина.
        /// </summary>
        private async Task ClaimEmailSessionOwnershipAsync(CancellationToken ct)
        {
            if (Session == null || Client == null) return;

            var shouldClaim = await MainThreadDispatcher.RunAsync(() =>
                    NakamaCredentialStore.HasKnownEmail)
                .ConfigureAwait(false);
            if (!shouldClaim)
                return;

            try
            {
                var rpc = await Client.RpcAsync(Session, RpcSessionClaim, "{}", canceller: ct).ConfigureAwait(false);
                var payload = rpc?.Payload;
                if (string.IsNullOrEmpty(payload)) return;
                var model = JsonUtility.FromJson<SessionEpochRpcResponse>(payload);
                if (model != null && model.ok)
                {
                    await MainThreadDispatcher
                        .RunAsync(() => PersistLocalSessionEpochSync(model.session_epoch))
                        .ConfigureAwait(false);
                }
            }
            catch (Exception e)
            {
                // Старый сервер без RPC — не блокируем вход; kick тогда только через AuthenticateEmail.
                if (config != null && config.verboseLogging)
                    Debug.LogWarning("[Nakama] duel_session_claim: " + e.Message);
            }
        }

        private static void PersistLocalSessionEpochSync(int epoch)
        {
            var value = Mathf.Max(0, epoch);
            Volatile.Write(ref _localSessionEpoch, value);
            PlayerPrefs.SetInt(SessionEpochLocalPrefKey, value);
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

                var local = GetLocalSessionEpoch();
                if (m.session_epoch > local)
                {
                    BeginSessionReplacedFlow();
                    return;
                }

                if (m.session_epoch != local) PersistLocalSessionEpochSync(m.session_epoch);
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

            var local = GetLocalSessionEpoch();
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
                            .RunAsync(() => NakamaCredentialStore.UsesEmail)
                            .ConfigureAwait(false);
                        if (persist)
                            await MainThreadDispatcher.RunAsync(() => PersistEmailSessionSync(refreshed))
                                .ConfigureAwait(false);
                        return;
                    }
                    catch (CredentialStorageException) { throw; }
                    catch (Exception e)
                    {
                        if (config != null && config.verboseLogging)
                            Debug.LogWarning($"[Nakama] Не удалось обновить сессию: {e.Message}");
                        await MainThreadDispatcher.RunAsync(ClearEmailTokenPrefsSync).ConfigureAwait(false);
                        Session = null;
                    }
                }
                else
                {
                    // Refresh протух/отсутствует — JWT больше не используем; восстановимся через linked device.
                    if (!string.IsNullOrEmpty(Session.RefreshToken) ||
                        await MainThreadDispatcher.RunAsync(() => NakamaCredentialStore.UsesEmail)
                            .ConfigureAwait(false))
                    {
                        await MainThreadDispatcher.RunAsync(ClearEmailTokenPrefsSync).ConfigureAwait(false);
                    }
                    Session = null;
                }
            }

            if (Session == null)
                Session = await ResolveSessionAsync(ct).ConfigureAwait(false);
        }

        private async Task<ISession> ResolveSessionAsync(CancellationToken ct)
        {
            var saved = await MainThreadDispatcher.RunAsync(NakamaCredentialStore.Snapshot).ConfigureAwait(false);
            var useEmail = saved.useEmail || !string.IsNullOrWhiteSpace(saved.knownEmail);
            var knownEmail = saved.knownEmail;
            var auth = saved.authToken;
            var refresh = saved.refreshToken;
            var expectedUserId = saved.userId;

            if (useEmail && !string.IsNullOrEmpty(auth))
            {
                var restored = global::Nakama.Session.Restore(auth, string.IsNullOrEmpty(refresh) ? null : refresh);
                // Миграция старых токенов: запоминаем аккаунт до удаления expired JWT.
                if (string.IsNullOrEmpty(expectedUserId) && !string.IsNullOrEmpty(restored?.UserId))
                {
                    expectedUserId = restored.UserId;
                    await MainThreadDispatcher.RunAsync(() =>
                    {
                        NakamaCredentialStore.SetUserId(restored.UserId);
                    }).ConfigureAwait(false);
                }
                if (restored != null && !restored.HasExpired(DateTime.UtcNow.Add(SessionRefreshSkew)))
                {
                    // Auth ещё жив с запасом под AutoRefresh — можно использовать.
                    if (!string.IsNullOrEmpty(restored.RefreshToken) &&
                        !restored.HasRefreshExpired(DateTime.UtcNow.Add(SessionRefreshSkew)))
                        return restored;
                    // Refresh уже мёртв: не отдаём сессию в AutoRefresh.
                    await MainThreadDispatcher.RunAsync(ClearEmailTokenPrefsSync).ConfigureAwait(false);
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
                    catch (CredentialStorageException) { throw; }
                    catch (Exception e)
                    {
                        if (config != null && config.verboseLogging)
                            Debug.LogWarning($"[Nakama] Не удалось восстановить сессию по refresh: {e.Message}");
                        await MainThreadDispatcher.RunAsync(ClearEmailTokenPrefsSync).ConfigureAwait(false);
                    }
                }
                else
                {
                    // И auth, и refresh плохие — чистим только токены, сохраняем режим email-аккаунта.
                    await MainThreadDispatcher.RunAsync(ClearEmailTokenPrefsSync).ConfigureAwait(false);
                }
            }

            var deviceId = await MainThreadDispatcher.RunAsync(GetDeviceId).ConfigureAwait(false);
            // Для известного email-аккаунта отсутствие linked device требует ручного входа,
            // а не создания нового гостевого профиля под свежим device id.
            var deviceSession = await AuthenticateDeviceWithGeneratedUsernameAsync(deviceId, create: !useEmail, ct)
                .ConfigureAwait(false);
            // Device, привязанный через LinkDevice, вернёт тот же user_id; сохраняем свежие токены,
            // только если на устройстве уже был сохранённый email-вход (не помечаем чистого гостя).
            if (useEmail)
            {
                // LinkDevice может завершиться конфликтом с прежним guest-профилем.
                // Такой device нельзя молча принять за ранее выбранный email-аккаунт.
                var sameAccount = !string.IsNullOrEmpty(expectedUserId)
                    && string.Equals(deviceSession.UserId, expectedUserId, StringComparison.Ordinal);
                if (string.IsNullOrEmpty(expectedUserId) && !string.IsNullOrWhiteSpace(knownEmail))
                {
                    var account = await Client.GetAccountAsync(deviceSession, canceller: ct).ConfigureAwait(false);
                    sameAccount = string.Equals(account?.Email?.Trim(), knownEmail.Trim(), StringComparison.OrdinalIgnoreCase);
                }
                if (!sameAccount)
                    throw new InvalidOperationException("Не удалось восстановить прежний аккаунт через устройство. Войдите по e-mail снова.");
                await MainThreadDispatcher.RunAsync(() => PersistEmailSessionSync(deviceSession)).ConfigureAwait(false);
            }
            return deviceSession;
        }

        /// <summary>
        /// После сбоя AutoRefresh («Refresh token invalid») — сброс JWT и переподключение
        /// через ранее привязанный device id.
        /// </summary>
        public async Task RecoverSessionAfterRefreshFailureAsync(CancellationToken ct)
        {
            await MainThreadDispatcher.RunAsync(ClearEmailTokenPrefsSync).ConfigureAwait(false);
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

        private async Task<ISession> AuthenticateDeviceWithGeneratedUsernameAsync(string deviceId, bool create, CancellationToken ct)
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
                        create: create,
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
                create: create,
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
        /// Device auth is a credential: preserve legacy IDs in the vault before deleting plaintext.
        /// </summary>
        private static string GetDeviceId()
        {
#if UNITY_EDITOR
            var profileKey = GetDevelopmentProfileKey();
#else
            var profileKey = "";
#endif
            return NakamaCredentialStore.GetDeviceId(profileKey);
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
            NakamaCredentialStore.PersistSession(s.AuthToken, s.RefreshToken, s.UserId);
        }

        private static void PersistKnownLinkedEmailSync(string email)
        {
            NakamaCredentialStore.SetKnownEmail(email);
        }

        private static void DeleteLegacyEmailPasswordSync()
        {
            if (!PlayerPrefs.HasKey(PrefSavedEmailPassword)) return;
            PlayerPrefs.DeleteKey(PrefSavedEmailPassword);
            PlayerPrefs.Save();
        }

        private async Task ApplyAuthEndpointScopeAsync()
        {
            var changed = await MainThreadDispatcher.RunAsync(() =>
                PrepareAuthEndpointScopeSync(config.GetScheme(), config.host, config.port)).ConfigureAwait(false);
            if (!changed) return;

            Session = null;
            Client = null;
            _socketBoundAuthToken = null;
            if (Socket != null)
            {
                try
                {
                    if (Socket.IsConnected)
                        await Socket.CloseAsync().ConfigureAwait(false);
                }
                catch
                {
                    // Старое соединение не должно мешать входу на новый endpoint.
                }
                finally
                {
                    Socket = null;
                }
            }
        }

        private static bool PrepareAuthEndpointScopeSync(string scheme, string host, int port)
        {
            var normalizedHost = (host ?? "").Trim().TrimEnd('.').ToLowerInvariant();
            var endpoint = (scheme ?? "").Trim().ToLowerInvariant() + "://" + normalizedHost + ":"
                + port.ToString(System.Globalization.CultureInfo.InvariantCulture);
#if UNITY_EDITOR
            var profile = GetDevelopmentProfileKey();
#else
            var profile = "";
#endif
            var changed = NakamaCredentialStore.Prepare(endpoint, profile);
            if (changed)
            {
                PlayerPrefs.DeleteKey(SessionEpochLocalPrefKey);
                Volatile.Write(ref _localSessionEpoch, 0);
                PlayerPrefs.Save();
            }
            return changed;
        }

        /// <summary>Только JWT/refresh. Режим email-аккаунта сохраняем для linked device fallback.</summary>
        private static void ClearEmailTokenPrefsSync()
        {
            NakamaCredentialStore.ClearTokens();
        }

        /// <summary>Полный сброс локального email-входа (Logout / Wipe).</summary>
        private static void ClearAllEmailPersistenceSync()
        {
            NakamaCredentialStore.ClearEmail();
        }

    }
}
