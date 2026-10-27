using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Character
{
    /// <summary>Server-authoritative, immutable account gender. Never trusts PlayerPrefs or token vars.</summary>
    public static class AccountGender
    {
        [Serializable]
        private sealed class Reply { public bool ok; public string gender; public bool can_choose; public string err; }
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static string _scope;
        private static bool _loaded, _female, _canChoose, _unsupported;
        public static event Action Changed;
        // Properties and notifications are accessed on the Unity main thread.
        public static bool IsFemale { get { ObserveAccount(); return _female; } }
        public static bool IsLoaded { get { ObserveAccount(); return _loaded; } }
        public static bool CanChoose { get { ObserveAccount(); return _loaded && _canChoose; } }

        private static void ObserveAccount()
        {
            var scope = LocalAccountCacheScope.Current;
            if (string.Equals(scope, _scope, StringComparison.Ordinal)) return;
            _scope = scope; _loaded = _female = _canChoose = _unsupported = false;
            Changed?.Invoke();
        }

        public static async Task RefreshAsync(CancellationToken ct, bool force = false)
        {
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var bootstrap = NakamaBootstrap.Instance;
                var session = bootstrap?.Session;
                if (bootstrap?.Client == null || session == null) return;
                var scope = await MainThreadDispatcher.RunAsync(() => { ObserveAccount(); return _scope; }).ConfigureAwait(false);
                if (string.IsNullOrEmpty(scope)) return;
                var ready = await MainThreadDispatcher.RunAsync(() => _loaded && _scope == scope).ConfigureAwait(false);
                if (ready && !force) return;
                try
                {
                    var rpc = await bootstrap.Client.RpcAsync(session, "duel_account_gender_get", "{}", canceller: ct).ConfigureAwait(false);
                    var reply = string.IsNullOrEmpty(rpc?.Payload) ? null : JsonUtility.FromJson<Reply>(rpc.Payload);
                    await MainThreadDispatcher.RunAsync(() =>
                    {
                        if (ReferenceEquals(session, bootstrap.Session) && LocalAccountCacheScope.IsCurrent(scope)) Accept(reply, false);
                    }).ConfigureAwait(false);
                }
                catch (ApiResponseException ex) when (ex.StatusCode == 404 || ex.StatusCode == 501)
                {
                    // Pre-upgrade servers have only legacy male accounts. A new
                    // female choice still requires the server registration RPC.
                    await MainThreadDispatcher.RunAsync(() =>
                    {
                        if (!ReferenceEquals(session, bootstrap.Session) || !LocalAccountCacheScope.IsCurrent(scope)) return;
                        Accept(new Reply { ok = true, gender = "male" }, true);
                    }).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception)
                {
                    // Keep pending/unloaded after network/server failures. Never
                    // enable saving a guessed male variant for a female account.
                }
            }
            finally { Gate.Release(); }
        }

        public static async Task RegisterAsync(bool female, CancellationToken ct)
        {
            await RefreshAsync(ct).ConfigureAwait(false);
            var bootstrap = NakamaBootstrap.Instance;
            var session = bootstrap?.Session;
            if (bootstrap?.Client == null || session == null) throw new InvalidOperationException("Нет активного аккаунта.");
            var scope = await MainThreadDispatcher.RunAsync(() =>
            {
                ObserveAccount();
                if (!_loaded) throw new InvalidOperationException("Не удалось загрузить пол аккаунта. Повторите подключение.");
                if (_unsupported && female) throw new InvalidOperationException("Выбор пола требует обновления сервера.");
                if (!_canChoose && _female != female) throw new InvalidOperationException("Пол аккаунта уже выбран и не меняется.");
                return _scope;
            }).ConfigureAwait(false);
            var unsupported = await MainThreadDispatcher.RunAsync(() => _unsupported).ConfigureAwait(false);
            if (unsupported) return;
            var rpc = await bootstrap.Client.RpcAsync(session, "duel_account_gender_register", female ? "{\"gender\":\"female\"}" : "{\"gender\":\"male\"}", canceller: ct).ConfigureAwait(false);
            var reply = string.IsNullOrEmpty(rpc?.Payload) ? null : JsonUtility.FromJson<Reply>(rpc.Payload);
            await MainThreadDispatcher.RunAsync(() =>
            {
                if (!ReferenceEquals(session, bootstrap.Session) || !LocalAccountCacheScope.IsCurrent(scope))
                    throw new InvalidOperationException("Аккаунт сменился. Повторите регистрацию.");
                if (reply != null && (reply.gender == "male" || reply.gender == "female")) Accept(reply, false, acceptError: true);
                if (reply == null || !reply.ok) throw new InvalidOperationException(reply?.err == "account_gender_immutable"
                    ? "Пол аккаунта уже выбран и не меняется." : "Не удалось сохранить выбор на сервере. Повторите попытку.");
                if (_female != female) throw new InvalidOperationException("Сервер не подтвердил выбор пола.");
            }).ConfigureAwait(false);
        }

        private static void Accept(Reply reply, bool unsupported, bool acceptError = false)
        {
            ObserveAccount();
            if (reply == null || (!reply.ok && !acceptError) || reply.gender != "male" && reply.gender != "female") return;
            _loaded = true; _female = reply.gender == "female"; _canChoose = reply.can_choose; _unsupported = unsupported;
            Changed?.Invoke();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _scope = null; _loaded = _female = _canChoose = _unsupported = false; Changed = null; }
    }
}
