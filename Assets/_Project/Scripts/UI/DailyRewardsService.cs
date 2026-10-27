using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.UI
{
    /// <summary>Account-scoped server snapshots; no local clock or counter authorizes a reward.</summary>
    public static class DailyRewardsService
    {
        [Serializable] public sealed class Item { public string def_id; public int amount; }
        [Serializable] public sealed class Reward { public int gold, ore, matter; public Item[] items; }
        [Serializable] public sealed class Login { public int next_day, total_claimed; public bool can_claim, claimed_today; public Reward[] rewards; }
        [Serializable] public sealed class Quest { public string id, label; public int progress, target; public bool ready, claimed; public Reward reward; }
        [Serializable] public sealed class Receipt { public int day, gold, ore, matter; public Item[] delivered_items, pending_items; }
        [Serializable] public sealed class State
        {
            public bool ok, already_claimed, completion_ready, completion_claimed;
            public string err; public long day, now, reset_at;
            public Login login; public Quest[] quests; public Reward completion_reward; public Receipt receipt;
        }
        [Serializable] private class Claim { public string kind; public long day; public int session_epoch; }
        [Serializable] private sealed class QuestClaim : Claim { public string quest_id; }
        private static readonly SemaphoreSlim Gate = new SemaphoreSlim(1, 1);
        private static string _scope, _error;
        private static State _state;
        private static bool _busy;
        private static float _receivedAt;
        public static event Action Changed;
        public static State Current { get { Observe(); return _state; } }
        public static bool Busy { get { Observe(); return _busy; } }
        public static string Error { get { Observe(); return _error; } }
        public static int ReadyRewardCount { get { Observe(); return CountReadyRewards(_state, EstimatedServerTime); } }
        public static long EstimatedServerTime => _state == null ? 0 : _state.now + (long)Mathf.Max(0, Time.realtimeSinceStartup - _receivedAt);
        /// <summary>Quest rewards plus the final bonus, never login. An expired snapshot cannot keep a stale badge alive.</summary>
        public static int CountReadyRewards(State state, long serverNow)
        {
            if (!Valid(state) || serverNow >= state.reset_at) return 0;
            var count = 0; var allClaimed = true;
            foreach (var quest in state.quests)
            {
                if (quest == null) return 0;
                if (quest.ready && !quest.claimed) count++;
                allClaimed &= quest.claimed;
            }
            if (allClaimed && state.completion_ready && !state.completion_claimed) count++;
            return count;
        }
        private static void Observe()
        {
            var scope = LocalAccountCacheScope.Current;
            if (_scope == scope) return;
            _scope = scope; _state = null; _error = null; _busy = false; Changed?.Invoke();
        }
        public static Task RefreshAsync(CancellationToken ct) => RequestAsync(null, null, ct);
        public static Task ClaimAsync(string kind, string questId, CancellationToken ct) => RequestAsync(kind, questId, ct);
        private static async Task RequestAsync(string kind, string questId, CancellationToken ct)
        {
            var request = await MainThreadDispatcher.RunAsync(() => { Observe(); return (scope: _scope, day: _state?.day ?? -1); }).ConfigureAwait(false);
            var requested = request.scope;
            await Gate.WaitAsync(ct).ConfigureAwait(false);
            string context = null;
            try
            {
                var bootstrap = NakamaBootstrap.Instance;
                if (bootstrap == null) return;
                await bootstrap.EnsureConnectedAsync(ct).ConfigureAwait(false);
                var session = bootstrap.Session;
                context = await MainThreadDispatcher.RunAsync(() =>
                {
                    Observe();
                    if (_scope == null || requested != null && requested != _scope || session == null) return null;
                    _busy = true; _error = null; Changed?.Invoke(); return _scope;
                }).ConfigureAwait(false);
                if (context == null) return;
                var payload = await MainThreadDispatcher.RunAsync(() => kind == null ? "{}" : questId == null ? JsonUtility.ToJson(new Claim
                { kind = kind, day = request.day, session_epoch = NakamaBootstrap.GetLocalSessionEpoch() }) : JsonUtility.ToJson(new QuestClaim
                { kind = kind, quest_id = questId, day = request.day, session_epoch = NakamaBootstrap.GetLocalSessionEpoch() })).ConfigureAwait(false);
                var rpc = await bootstrap.Client.RpcAsync(session, kind == null ? "duel_daily_get" : "duel_daily_claim", payload, canceller: ct).ConfigureAwait(false);
                var reply = string.IsNullOrEmpty(rpc?.Payload) ? null : JsonUtility.FromJson<State>(rpc.Payload);
                var accepted = await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!LocalAccountCacheScope.IsCurrent(context) || !ReferenceEquals(session, bootstrap.Session)) return false;
                    if (!Valid(reply)) { _error = Message(reply?.err); return false; }
                    _state = reply; _receivedAt = Time.realtimeSinceStartup; _error = null; return true;
                }).ConfigureAwait(false);
                if (accepted && kind != null) await PlayerResourcesService.GetAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception)
            {
                if (context != null) await MainThreadDispatcher.RunAsync(() =>
                { if (LocalAccountCacheScope.IsCurrent(context)) _error = "Нет связи с сервером. Повторите попытку."; }).ConfigureAwait(false);
            }
            finally
            {
                if (context != null) await MainThreadDispatcher.RunAsync(() =>
                { if (LocalAccountCacheScope.IsCurrent(context)) { _busy = false; Changed?.Invoke(); } }).ConfigureAwait(false);
                Gate.Release();
            }
        }
        public static bool Valid(State reply) => reply != null && reply.ok && reply.day >= 0 && reply.reset_at > reply.now &&
            reply.login != null && reply.login.next_day >= 1 && reply.login.next_day <= 7 && reply.login.rewards?.Length == 7 && reply.quests?.Length == 3;
        private static string Message(string code) => code == "day_changed" ? "Начался новый день. Обновите награды." :
            code == "quest_incomplete" ? "Задание ещё не выполнено." : code == "quests_unclaimed" ? "Сначала получите награды трёх заданий." :
            code == "session_stale" ? "Откройте игру заново для обновления сессии." : "Не удалось получить награду. Нажмите «Обновить».";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _scope = _error = null; _state = null; _busy = false; Changed = null; }
    }
}
