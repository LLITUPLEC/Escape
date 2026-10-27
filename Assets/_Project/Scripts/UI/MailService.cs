using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.UI
{
    /// <summary>Account-scoped server inbox. A claim references an existing server entitlement only.</summary>
    public static class MailService
    {
        [Serializable] public sealed class Attachment { public string def_id, resource; public int amount; }
        [Serializable] public sealed class Message
        {
            public string id, title, body, event_id; public long created_at; public bool claimed; public Attachment[] attachments;
            public string kind, action, theme_id, avatar_id;
            public bool IsAppearanceUnlock => kind == "appearance_unlock" && action == "hero" && CharacterAppearance.IsTheme(theme_id);
        }
        [Serializable] public sealed class State { public bool ok; public string err; public int unread_count; public Message[] messages; }
        [Serializable] public sealed class ClaimResult
        {
            public bool ok, claimed, already_claimed, informational; public string err; public int unread_count;
            public ShopService.Item[] delivered_items, pending_items; public ShopService.Wallet resources;
        }
        [Serializable] private sealed class Claim { public int session_epoch; public string message_id; }
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static string _scope, _error, _status;
        private static State _state;
        private static bool _busy;
        private static float _nextRefresh;
        public static event Action Changed;
        public static State Current { get { Observe(); return _state; } }
        public static bool Busy { get { Observe(); return _busy; } }
        public static string Error { get { Observe(); return _error; } }
        public static string Status { get { Observe(); return _status; } }
        public static int UnreadCount => Current?.unread_count ?? 0;
        private static void Observe()
        {
            var scope = LocalAccountCacheScope.Current;
            if (scope == _scope) return;
            _scope = scope; _state = null; _busy = false; _error = _status = null; _nextRefresh = 0; Changed?.Invoke();
        }
        public static void RequestRefresh(bool force = false)
        {
            if (!Application.isPlaying) return; Observe();
            if (_scope == null || _busy || !ServerConnectionGuard.CanPlay || !force && Time.realtimeSinceStartup < _nextRefresh) return;
            _nextRefresh = Time.realtimeSinceStartup + 30; _ = RefreshAsync(CancellationToken.None);
        }
        public static Task RefreshAsync(CancellationToken ct) => RequestAsync(null, ct);
        public static Task ClaimAsync(string messageId, CancellationToken ct) => RequestAsync(messageId, ct);
        private static async Task RequestAsync(string messageId, CancellationToken ct)
        {
            var requested = await MainThreadDispatcher.RunAsync(() => { Observe(); return _scope; }).ConfigureAwait(false);
            try { await Gate.WaitAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            string owner = null;
            try
            {
                var bootstrap = NakamaBootstrap.Instance; if (bootstrap == null) return;
                if (messageId == null) await bootstrap.EnsureConnectedAsync(ct).ConfigureAwait(false);
                else await ServerConnectionGuard.RequireAsync(ct).ConfigureAwait(false);
                var session = bootstrap.Session;
                owner = await MainThreadDispatcher.RunAsync(() =>
                {
                    Observe(); if (_scope == null || requested != null && _scope != requested || session == null) return null;
                    _busy = true; _error = null; _status = null; _nextRefresh = Time.realtimeSinceStartup + 30; Changed?.Invoke(); return _scope;
                }).ConfigureAwait(false);
                if (owner == null) return;
                ClaimResult claim = null;
                if (messageId != null)
                {
                    var payload = await MainThreadDispatcher.RunAsync(() => JsonUtility.ToJson(new Claim { message_id = messageId, session_epoch = NakamaBootstrap.GetLocalSessionEpoch() })).ConfigureAwait(false);
                    var rpc = await bootstrap.Client.RpcAsync(session, "duel_mail_claim", payload, canceller: ct).ConfigureAwait(false);
                    claim = string.IsNullOrEmpty(rpc?.Payload) ? null : JsonUtility.FromJson<ClaimResult>(rpc.Payload);
                    if (claim == null || !claim.ok || !claim.claimed)
                    {
                        await MainThreadDispatcher.RunAsync(() => { if (LocalAccountCacheScope.IsCurrent(owner) && ReferenceEquals(session, bootstrap.Session)) _error = MessageForError(claim?.err); }).ConfigureAwait(false);
                        return;
                    }
                }
                var response = await bootstrap.Client.RpcAsync(session, "duel_mail_get", "{}", canceller: ct).ConfigureAwait(false);
                var reply = string.IsNullOrEmpty(response?.Payload) ? null : JsonUtility.FromJson<State>(response.Payload);
                var accepted = await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!LocalAccountCacheScope.IsCurrent(owner) || !ReferenceEquals(session, bootstrap.Session)) return false;
                    if (!Valid(reply)) { _error = "Не удалось обновить почту. Нажмите «Обновить»."; return false; }
                    _state = reply; _error = null;
                    if (claim != null) _status = claim.informational ? (claim.already_claimed ? "Письмо уже прочитано." : "Письмо прочитано.") : claim.pending_items?.Length > 0 ? "Награда получена. Часть предметов ждёт места в инвентаре." : claim.already_claimed ? "Награда уже была получена." : "Награда получена. Припасы уже у вас.";
                    return true;
                }).ConfigureAwait(false);
                if (accepted && claim != null && !claim.informational) await PlayerResourcesService.GetAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception)
            {
                if (owner != null) await MainThreadDispatcher.RunAsync(() =>
                { if (LocalAccountCacheScope.IsCurrent(owner)) _error = messageId == null ? "Нет связи с сервером. Обновите почту позже." : "Подтверждение не получено. Повторите получение: награда повторно не выдаётся и не теряется."; }).ConfigureAwait(false);
            }
            finally
            {
                if (owner != null) await MainThreadDispatcher.RunAsync(() => { if (LocalAccountCacheScope.IsCurrent(owner)) { _busy = false; Changed?.Invoke(); } }).ConfigureAwait(false);
                Gate.Release();
            }
        }
        public static bool Valid(State state) => state != null && state.ok && state.unread_count >= 0 &&
            (state.messages == null ? state.unread_count == 0 : state.messages.All(m => m != null && !string.IsNullOrEmpty(m.id) && m.id.Length <= 128 && m.created_at >= 0 &&
                (m.kind != "appearance_unlock" || m.IsAppearanceUnlock && CharacterAppearance.IsValidId(m.avatar_id) && CharacterAppearance.ThemeFromId(m.avatar_id) == m.theme_id) &&
                (m.attachments == null || m.attachments.All(a => a != null && a.amount > 0 && (!string.IsNullOrEmpty(a.def_id) && string.IsNullOrEmpty(a.resource) || string.IsNullOrEmpty(a.def_id) && (a.resource == "gold" || a.resource == "ore" || a.resource == "matter"))))) && state.messages.Count(m => !m.claimed) == state.unread_count);
        private static string MessageForError(string code) => code switch
        {
            "mail_not_found" => "Письмо уже недоступно. Обновите список.",
            "session_stale" or "session_epoch_mismatch" or "stale_session" => "Сессия изменилась. Откройте почту заново.",
            _ => "Не удалось подтвердить получение. Нажмите ту же кнопку ещё раз."
        };
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _scope = _error = _status = null; _state = null; _busy = false; _nextRefresh = 0; Changed = null; }
    }
}
