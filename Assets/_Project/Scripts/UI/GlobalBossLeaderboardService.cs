using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.UI
{
    /// <summary>One public weekly damage row per account, with all attempts summed by the server.</summary>
    public static class GlobalBossLeaderboardService
    {
        [Serializable] public sealed class Entry
        {
            public int rank,attempts_used;public string user_id,display_name,avatar_id;public long damage;public bool is_self;
        }
        [Serializable] public sealed class Page
        {
            public bool ok,updating;public string err,event_id,next_cursor,prev_cursor;public Entry[] entries;public Entry self_entry;
        }
        [Serializable] private sealed class Request {public string event_id,cursor;public int limit=20;}
        private static readonly SemaphoreSlim Gate=new(1,1);
        private static string _scope,_error;private static Page _page;private static bool _busy;
        public static event Action Changed;
        public static Page Current {get{Observe();return _page;}}
        public static bool Busy {get{Observe();return _busy;}}
        public static string Error {get{Observe();return _error;}}
        private static void Observe()
        {
            var scope=LocalAccountCacheScope.Current;if(scope==_scope)return;
            _scope=scope;_page=null;_busy=false;_error=null;Changed?.Invoke();
        }
        public static async Task RefreshAsync(string eventId,string cursor,CancellationToken ct)
        {
            if(string.IsNullOrEmpty(eventId)||eventId.Length>128||cursor?.Length>4096)return;
            if(!Gate.Wait(0))return;
            string owner = null;
            var ownsRequest = false;
            try
            {
                ct.ThrowIfCancellationRequested();
                var allowed = await MainThreadDispatcher.RunAsync(() =>
                {
                    Observe();
                    if (_scope == null || ct.IsCancellationRequested) return false;
                    owner = _scope;
                    ownsRequest = true;
                    _busy = true;
                    if (_page != null && _page.event_id != eventId) { _page = null; _error = null; }
                    Changed?.Invoke();
                    return true;
                }).ConfigureAwait(false);
                if (!allowed) return;
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(GlobalBossService.ReadTimeoutMilliseconds);
                var token = deadline.Token;
                var boot = NakamaBootstrap.Instance;
                var session = await GlobalBossService.GetReadSessionAsync(boot, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.IsCurrent(owner)).ConfigureAwait(false)) return;
                var request = JsonUtility.ToJson(new Request { event_id = eventId, cursor = cursor });
                var rpc = await boot.Client.RpcAsync(session, "duel_global_boss_leaderboard_get", request, canceller: token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var page = JsonUtility.FromJson<Page>(rpc?.Payload ?? "{}");
                await MainThreadDispatcher.RunAsync(()=>
                {
                    if (token.IsCancellationRequested || !LocalAccountCacheScope.IsCurrent(owner) ||
                        !ReferenceEquals(boot, NakamaBootstrap.Instance) || !ReferenceEquals(session, boot.Session) || !boot.GameplaySessionAllowed) return;
                    if(!Valid(page,eventId)||page.self_entry!=null&&page.self_entry.user_id!=session.UserId){_error=page?.err=="event_changed"?"Началось новое событие. Нажмите «Обновить».":"Не удалось загрузить летопись. Нажмите «Обновить».";return;}
                    _page=page;_error=null;
                }).ConfigureAwait(false);
            }
            catch(OperationCanceledException)when(ct.IsCancellationRequested){}
            catch(Exception)
            {
                if (ownsRequest) await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!ct.IsCancellationRequested && LocalAccountCacheScope.IsCurrent(owner))
                        _error = "Не удалось загрузить летопись. Нажмите «Обновить».";
                }).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (ownsRequest) await MainThreadDispatcher.RunAsync(() =>
                    {
                        if (!LocalAccountCacheScope.IsCurrent(owner)) return;
                        _busy = false;
                        Changed?.Invoke();
                    }).ConfigureAwait(false);
                }
                finally { Gate.Release(); }
            }
        }
        public static bool Valid(Page page,string eventId)=>page!=null&&page.ok&&page.event_id==eventId&&(page.entries==null||page.entries.Length<=50&&page.entries.All(ValidEntry))&&(page.self_entry==null||ValidFields(page.self_entry)&&page.self_entry.is_self&&page.self_entry.rank>=0)&&(page.next_cursor?.Length??0)<=4096&&(page.prev_cursor?.Length??0)<=4096;
        private static bool ValidEntry(Entry e)=>ValidFields(e)&&e.rank>0;
        private static bool ValidFields(Entry e)=>e!=null&&!string.IsNullOrEmpty(e.user_id)&&e.user_id.Length<=128&&e.damage>=0&&e.attempts_used>=0&&e.attempts_used<=3&&(e.display_name?.Length??0)<=128&&(e.avatar_id?.Length??0)<=128;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset(){_scope=_error=null;_page=null;_busy=false;Changed=null;}
    }
}
