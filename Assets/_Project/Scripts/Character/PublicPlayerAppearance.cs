using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using UnityEngine;

namespace Project.Character
{
    /// <summary>Public, server-selected cosmetic IDs. Never reconciles another account's unlocks.</summary>
    public static class PublicPlayerAppearance
    {
        [Serializable] public sealed class State
        {
            public bool ok;
            public string user_id, avatar_id, body_id;
            public Sprite Body(Sprite fallback = null) => CharacterAppearance.Load(body_id, false) ?? fallback;
            public Sprite Avatar(Sprite fallback = null) => CharacterAppearance.Load(avatar_id, true) ?? fallback;
        }
        [Serializable] private sealed class Request { public string user_id; }
        private sealed class Cached { public State state; public DateTime expires; }
        private static readonly Dictionary<string, Cached> Cache = new();
        private static string _scope;

        public static async Task<State> GetAsync(string userId, CancellationToken ct)
        {
            if (!Guid.TryParseExact(userId, "D", out _)) return null;
            var scope = LocalAccountCacheScope.Current;
            if (_scope != scope) { Cache.Clear(); _scope = scope; }
            if (Cache.TryGetValue(userId, out var cached) && cached.expires > DateTime.UtcNow) return cached.state;
            var boot = NakamaBootstrap.Instance;
            var session = boot?.Session;
            if (boot?.Client == null || session == null) return null;
            try
            {
                var rpc = await boot.Client.RpcAsync(session, "duel_public_appearance_get",
                    JsonUtility.ToJson(new Request { user_id = userId }), canceller: ct);
                if (!LocalAccountCacheScope.IsCurrent(scope) || !ReferenceEquals(session, boot.Session)) return null;
                var state = JsonUtility.FromJson<State>(rpc?.Payload ?? "{}");
                if (state == null || !state.ok || state.user_id != userId ||
                    !ValidId(state.avatar_id) || !ValidId(state.body_id)) return null;
                Cache[userId] = new Cached { state = state, expires = DateTime.UtcNow.AddSeconds(60) };
                return state;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception) { return null; }
        }
        private static bool ValidId(string id) => string.IsNullOrEmpty(id) || CharacterAppearance.IsValidId(id);
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { Cache.Clear(); _scope = null; }
    }
}
