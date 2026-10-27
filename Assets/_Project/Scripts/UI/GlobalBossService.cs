using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Match3;
using Project.Nakama;
using Project.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.UI
{
    /// <summary>All event progress and entry costs are authoritative. An uncertain entry reuses its intention.</summary>
    public static class GlobalBossService
    {
        public const string Token = "global_boss_teleport";
        public const string Art = "Expedition/Boss/0313/global-boss-body";
        [Serializable] public sealed class Attempt { public string attempt_id, match_id; public bool resumable; }
        [Serializable] public sealed class Ability { public string id, name, description; }
        [Serializable] public sealed class Reward { public string def_id, resource; public int amount; }
        [Serializable] public sealed class State
        {
            public bool ok, qualified; public string err, event_id, status, name, token_id;
            public long server_now, starts_at, ends_at, hp, hp_max, my_damage, eligibility_progress, eligibility_required;
            public int attempts_used, attempts_total, attempts_left, token_count, max_turns;
            public int base_damage, base_armor, base_heal; public float base_crit;
            public Attempt active_attempt; public Ability[] abilities; public Reward[] rewards_preview;
        }
        [Serializable] public sealed class Entry
        {
            public bool ok, is_global_boss, spent_token, resumed; public string err, event_id, attempt_id, match_id, bot_id, bot_name, bot_user_id;
            public int attempts_left;
        }
        [Serializable] private sealed class EnterRequest { public string request_id, event_id; public int session_epoch; }
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static string _scope, _error, _entryId, _entryEvent; private static State _state; private static bool _busy; private static double _received;
        public static event Action Changed;
        public static State Current { get { Observe(); return _state; } }
        public static bool Busy { get { Observe(); return _busy; } }
        public static string Error { get { Observe(); return _error; } }
        public static long ServerTime => _state == null ? 0 : _state.server_now + (long)Math.Max(0, Time.realtimeSinceStartupAsDouble - _received);
        private static void Observe()
        {
            var scope = LocalAccountCacheScope.Current; if (_scope == scope) return;
            _scope = scope; _state = null; _error = _entryId = _entryEvent = null; _busy = false; Changed?.Invoke();
        }
        public static Task RefreshAsync(CancellationToken ct) => Request(false, ct);
        public static Task EnterAsync(CancellationToken ct) => Request(true, ct);
        private static async Task Request(bool enter, CancellationToken ct)
        {
            var expected = await MainThreadDispatcher.RunAsync(() => { Observe(); return _scope; }).ConfigureAwait(false);
            try { await Gate.WaitAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            string owner = null; global::Nakama.ISession session = null; NakamaBootstrap boot = null;
            try
            {
                boot = NakamaBootstrap.Instance; if (boot == null) return;
                await ServerConnectionGuard.RequireAsync(ct).ConfigureAwait(false); session = boot.Session;
                var request = await MainThreadDispatcher.RunAsync(() =>
                {
                    Observe(); if (_scope == null || expected != null && expected != _scope || session == null) return null;
                    owner = _scope; _busy = true; _error = null; Changed?.Invoke();
                    if (!enter) return "{}";
                    if (_entryId == null)
                    {
                        if (string.IsNullOrEmpty(_state?.event_id)) { _error = "Обновите событие перед входом."; return null; }
                        _entryId = Guid.NewGuid().ToString("N"); _entryEvent = _state.event_id;
                    }
                    return JsonUtility.ToJson(new EnterRequest { request_id = _entryId, event_id = _entryEvent, session_epoch = NakamaBootstrap.GetLocalSessionEpoch() });
                }).ConfigureAwait(false);
                if (request == null) return;
                var result = await boot.Client.RpcAsync(session, enter ? "duel_global_boss_enter" : "duel_global_boss_get", request, canceller: ct).ConfigureAwait(false);
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!LocalAccountCacheScope.IsCurrent(owner) || !ReferenceEquals(session, boot.Session)) return;
                    if (enter)
                    {
                        var reply = JsonUtility.FromJson<Entry>(result?.Payload ?? "{}");
                        if (reply == null || !reply.ok || !reply.is_global_boss || string.IsNullOrEmpty(reply.match_id) || string.IsNullOrEmpty(reply.attempt_id) || reply.event_id != _entryEvent || string.IsNullOrEmpty(reply.bot_user_id))
                        { _error = Message(reply?.err); if (reply?.err == "weekly_attempts_exhausted" || reply?.err == "teleport_required" || reply?.err == "boss_unavailable" || reply?.err == "attempt_finished" || reply?.err == "event_changed") _entryId = _entryEvent = null; return; }
                        _entryId = _entryEvent = null; Match3LaunchContext.ArmGlobalBoss(reply.match_id, reply.event_id, reply.attempt_id, reply.bot_user_id, reply.bot_name);
                        SceneManager.LoadScene("DuelMatch3");
                    }
                    else
                    {
                        var reply = JsonUtility.FromJson<State>(result?.Payload ?? "{}");
                        if (reply == null || !reply.ok || string.IsNullOrEmpty(reply.event_id) || reply.hp_max <= 0 || reply.hp < 0 || reply.hp > reply.hp_max)
                        { _error = Message(reply?.err); return; }
                        _state = reply; _received = Time.realtimeSinceStartupAsDouble; _error = null;
                    }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception)
            {
                if (owner != null) await MainThreadDispatcher.RunAsync(() => { if (LocalAccountCacheScope.IsCurrent(owner)) _error = enter ? "Вход ещё не подтверждён. Повторите кнопку: жетон второй раз не спишется." : "Нет связи с сервером. Обновите событие."; }).ConfigureAwait(false);
            }
            finally
            {
                if (owner != null) await MainThreadDispatcher.RunAsync(() => { if (LocalAccountCacheScope.IsCurrent(owner)) { _busy = false; Changed?.Invoke(); } }).ConfigureAwait(false);
                Gate.Release();
            }
        }
        private static string Message(string error) => error switch
        {
            "attempt_limit" or "attempts_exhausted" or "weekly_attempts_exhausted" => "Все три попытки этого события использованы.",
            "not_enough_tokens" or "no_teleport_token" or "teleport_required" => "Нужен жетон перехода. Получите его на седьмой день входа или в магазине.",
            "event_finished" or "boss_defeated" or "boss_unavailable" => "Событие завершено. Обновите окно и проверьте почту.",
            "attempt_finished" => "Предыдущая попытка уже завершена. Обновите событие перед новым входом.",
            "entry_starting" => "Переход уже создаётся. Нажмите кнопку ещё раз через несколько секунд.",
            "event_changed" => "Началось новое недельное событие. Обновите окно перед новым входом.",
            "session_stale" or "session_epoch_mismatch" => "Сессия изменилась. Откройте событие заново.",
            _ => "Сервер не подтвердил действие. Обновите событие и повторите попытку."
        };
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _scope = _error = _entryId = _entryEvent = null; _state = null; _busy = false; Changed = null; }
    }
}
