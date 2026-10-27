using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Character
{
    /// <summary>Only the current account's server snapshot opens progression buttons or requests automatic onboarding.</summary>
    public static class PlayerGuidanceService
    {
        [Serializable] public sealed class State
        {
            public bool ok, workshop_unlocked, tournaments_unlocked;
            public int schema, mine_wins;
            public string tutorial_status, err;
        }
        [Serializable] private sealed class ExitRequest { public string result; public int session_epoch; }
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static string _scope;
        private static State _current;
        private static bool _fetching;
        private static float _nextRefresh;
        public static event Action Changed;
        public static State Current { get { Observe(); return _current; } }
        public static bool WorkshopUnlocked => Current?.workshop_unlocked == true;
        public static bool TournamentsUnlocked => Current?.tournaments_unlocked == true;
        private static void Observe()
        {
            var scope = LocalAccountCacheScope.Current;
            if (_scope == scope) return;
            _scope = scope; _current = null; _nextRefresh = 0; Changed?.Invoke();
        }
        public static bool Valid(State state) => state != null && state.ok && state.schema == 1 && state.mine_wins >= 0 && state.tournaments_unlocked == (state.mine_wins >= 10) &&
            (state.tutorial_status == "pending" || state.tutorial_status == "legacy" || state.tutorial_status == "completed" || state.tutorial_status == "skipped");
        public static void RequestRefresh(bool force = false)
        {
            if (!Application.isPlaying) return;
            Observe();
            if (_scope == null || _fetching || !force && Time.realtimeSinceStartup < _nextRefresh) return;
            _fetching = true; _nextRefresh = Time.realtimeSinceStartup + 30;
            _ = Fetch();
        }
        private static async Task Fetch()
        {
            try { await RequestAsync(null, null, CancellationToken.None); }
            finally { _fetching = false; }
        }
        public static Task RecordTutorialExitAsync(string result, string ownerScope) =>
            ownerScope == null ? Task.CompletedTask : RequestAsync(result, ownerScope, CancellationToken.None);
        private static async Task RequestAsync(string result, string ownerScope, CancellationToken ct)
        {
            var requestedScope = await MainThreadDispatcher.RunAsync(() => { Observe(); return _scope; }).ConfigureAwait(false);
            if (requestedScope == null || ownerScope != null && ownerScope != requestedScope) return;
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var bootstrap = NakamaBootstrap.Instance;
                if (bootstrap == null) return;
                await bootstrap.EnsureConnectedAsync(ct).ConfigureAwait(false);
                var session = bootstrap.Session; var client = bootstrap.Client;
                if (session == null || client == null) return;
                if (!await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.IsCurrent(requestedScope)).ConfigureAwait(false)) return;
                var payload = result == null ? "{}" : await MainThreadDispatcher.RunAsync(() =>
                    JsonUtility.ToJson(new ExitRequest { result = result, session_epoch = NakamaBootstrap.GetLocalSessionEpoch() })).ConfigureAwait(false);
                var rpc = await client.RpcAsync(session, result == null ? "duel_player_guidance_get" : "duel_player_guidance_set", payload, canceller: ct).ConfigureAwait(false);
                var state = string.IsNullOrEmpty(rpc?.Payload) ? null : JsonUtility.FromJson<State>(rpc.Payload);
                await MainThreadDispatcher.RunAsync(() =>
                {
                    Observe();
                    if (!LocalAccountCacheScope.IsCurrent(requestedScope) || !ReferenceEquals(session, bootstrap.Session) || !Valid(state)) return;
                    _current = state; Changed?.Invoke();
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception) { /* Keep a known same-account snapshot; never open content after a failed read. */ }
            finally { Gate.Release(); }
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _scope = null; _current = null; _fetching = false; _nextRefresh = 0; Changed = null; }
    }
}
