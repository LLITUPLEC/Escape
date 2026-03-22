using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;

namespace Project.Nakama
{
    public sealed class NakamaBootstrap : MonoBehaviour
    {
        public static NakamaBootstrap Instance { get; private set; }

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

        private const string DeviceIdPrefKey = "nakama.device_id";
        private const string DevDeviceIdPrefPrefix = "nakama.device_id.dev.";
        private const string RpcOnlinePingAndCount = "duel_online_ping_and_count";
        [SerializeField] private bool keepOnlineHeartbeat = true;
        [SerializeField] private float onlineHeartbeatSeconds = 5f;
        private CancellationTokenSource _cts;

        private void Awake()
        {
            if (Instance != null)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Важно для локального мультиоконного теста (ParrelSync): чтобы окно без фокуса
            // продолжало симуляцию (Update/FixedUpdate) и отправку сетевых сообщений.
            Application.runInBackground = true;
            // Чуть стабильнее сетевой тик в Editor при двух окнах.
            QualitySettings.vSyncCount = 0;
            if (Application.targetFrameRate <= 0) Application.targetFrameRate = 60;
        }

        private async void Start()
        {
            _cts = new CancellationTokenSource();

            if (config == null)
            {
                config = Resources.Load<NakamaConnectionConfig>("NakamaConnectionConfig");
            }

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
                    if (IsReady && Client != null && Session != null)
                        await Client.RpcAsync(Session, RpcOnlinePingAndCount, "{}");
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

            // В Editor при Stop важно закрыть сокет, иначе его receive-loop может жить до domain reload.
            try
            {
                if (Socket != null && Socket.IsConnected)
                {
                    _ = Socket.CloseAsync();
                }
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
                {
                    _ = Socket.CloseAsync();
                }
            }
            catch
            {
                // ignored
            }
        }

        public async Task EnsureConnectedAsync(CancellationToken ct)
        {
            if (IsReady) return;

            Client = new Client(config.GetScheme(), config.host, config.port, config.serverKey);
            Client.Timeout = 10;

            var deviceId = GetDeviceId();
            Session = await Client.AuthenticateDeviceAsync(deviceId, create: true, canceller: ct);

            Socket = Client.NewSocket();

            Socket.Connected += () =>
            {
                if (config.verboseLogging) Debug.Log("[Nakama] Socket connected");
            };
            Socket.Closed += reason =>
            {
                if (config.verboseLogging) Debug.Log($"[Nakama] Socket closed: {reason}");
            };
            Socket.ReceivedError += e =>
            {
                Debug.LogError($"[Nakama] Socket error: {e?.Message}");
            };

            ct.ThrowIfCancellationRequested();
            await Socket.ConnectAsync(Session, appearOnline: true, connectTimeout: 10);
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
    }
}

