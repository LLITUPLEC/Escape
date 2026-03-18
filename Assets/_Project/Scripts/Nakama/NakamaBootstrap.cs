using System;
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
            }
            catch (Exception e)
            {
                Debug.LogException(e);
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

            var customId = Guid.NewGuid().ToString("N"); // уникально на каждый запуск => два окна не конфликтуют
            Session = await Client.AuthenticateCustomAsync(customId, create: true, canceller: ct);

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
    }
}

