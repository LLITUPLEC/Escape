using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Character;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Match3
{
    [Serializable]
    public sealed class Match3RecoveryReply
    {
        public bool ok, active, pending_admission, terminal, started, battle_started, can_cancel;
        public bool pvp_pro, pvp_race, is_global_boss, global_boss_practice, opponent_is_bot;
        public string err, match_id, mode, bot_id, bot_user_id, bot_name, opponent_user_id, opponent_name;
        public string difficulty, affix, request_id, arena_tournament_id, arena_round, arena_kind, arena_bet_tier;
        public string event_id, attempt_id, name;
        public int floor;

        // Server bot matches share the PvE transport, including multiplayer tournaments and Race.
        public bool IsSoloBattle => is_global_boss || mode == "pve" && !pvp_race && string.IsNullOrEmpty(arena_tournament_id);
    }

    [Serializable]
    public sealed class Match3CancelReply
    {
        public bool ok, accepted, terminal, match_cancelled, refund_pending, refund_recorded, progression_pending;
        public string err, end_reason, match_id, request_id;
        public Progression progression;
    }

    /// <summary>A reply belongs to one authenticated lifetime, even when a later login uses the same user ID.</summary>
    public readonly struct Match3AccountOperation
    {
        public readonly NakamaBootstrap Bootstrap;
        public readonly string OwnerScope;
        public readonly int Generation;
        public readonly ISession Session;
        public Match3AccountOperation(NakamaBootstrap bootstrap, string scope, int generation, ISession session)
        { Bootstrap = bootstrap; OwnerScope = scope; Generation = generation; Session = session; }
        public static Match3AccountOperation Capture()
        {
            var boot = NakamaBootstrap.Instance;
            return new Match3AccountOperation(boot, LocalAccountCacheScope.Current, boot?.AuthenticationGeneration ?? 0, boot?.Session);
        }
        public bool IsSameAuthentication => Bootstrap != null && ReferenceEquals(Bootstrap, NakamaBootstrap.Instance) &&
            Generation == Bootstrap.AuthenticationGeneration && Bootstrap.GameplaySessionAllowed &&
            LocalAccountCacheScope.IsCurrent(OwnerScope) && Session != null && Session.UserId == Bootstrap.Session?.UserId;
        public bool IsCurrent => IsSameAuthentication && ReferenceEquals(Session, Bootstrap.Session);
    }

    public static class Match3RecoveryService
    {
        public const string RecoverRpc = "duel_match3_recover", CancelRpc = "duel_match3_cancel";
        private static readonly HashSet<string> TerminalMatches = new();
        private static readonly Queue<string> TerminalOrder = new();
#if UNITY_EDITOR
        internal static Func<string, string, CancellationToken, Task<string>> EditorTransportOverride;
        internal static Func<CancellationToken, Task> EditorRequireOverride;
#endif
        [Serializable] private sealed class Request { public string match_id, request_id; public int session_epoch; }
        [Serializable] private sealed class EpochRequest { public int session_epoch; }
        [Serializable] private sealed class MatchRequest { public string match_id; public int session_epoch; }
        [Serializable] private sealed class AdmissionRequest { public string request_id; public int session_epoch; }
        private static string BuildPayload(string matchId, string requestId)
        {
            var epoch = NakamaBootstrap.GetLocalSessionEpoch();
            if (string.IsNullOrEmpty(matchId) && string.IsNullOrEmpty(requestId))
                return JsonUtility.ToJson(new EpochRequest { session_epoch = epoch });
            if (string.IsNullOrEmpty(matchId)) return JsonUtility.ToJson(new AdmissionRequest { request_id = requestId, session_epoch = epoch });
            if (string.IsNullOrEmpty(requestId)) return JsonUtility.ToJson(new MatchRequest { match_id = matchId, session_epoch = epoch });
            return JsonUtility.ToJson(new Request { match_id = matchId, request_id = requestId, session_epoch = epoch });
        }
        private static string Key(string owner, int generation, string matchId) => owner + ":" + generation + ":" + matchId;
        public static void RememberTerminal(string matchId)
        {
            var current = Match3AccountOperation.Capture();
            if (!current.IsCurrent || string.IsNullOrEmpty(matchId)) return;
            var key = Key(current.OwnerScope, current.Generation, matchId);
            if (!TerminalMatches.Add(key)) return;
            TerminalOrder.Enqueue(key);
            while (TerminalOrder.Count > 64) TerminalMatches.Remove(TerminalOrder.Dequeue());
        }
        public static bool CanRestore(Match3RecoveryReply reply, string owner, int generation) =>
            reply != null && reply.ok && reply.active && !reply.terminal && !reply.global_boss_practice &&
            !reply.pending_admission && (reply.mode == "pve" || reply.mode == "pvp") &&
            reply.mode != "global_boss_practice" && !string.IsNullOrEmpty(reply.match_id) &&
            !TerminalMatches.Contains(Key(owner, generation, reply.match_id));

        public static Task<Match3RecoveryReply> RecoverAsync(string matchId, string requestId, CancellationToken ct) =>
            InvokeAsync<Match3RecoveryReply>(RecoverRpc, matchId, requestId, ct);
        public static Task<Match3CancelReply> CancelAsync(string matchId, string requestId, CancellationToken ct) =>
            InvokeAsync<Match3CancelReply>(CancelRpc, matchId, requestId, ct);
        public static Task RequireConnectionAsync(CancellationToken ct)
        {
#if UNITY_EDITOR
            if (EditorTransportOverride != null) return EditorRequireOverride?.Invoke(ct) ?? Task.CompletedTask;
#endif
            return ServerConnectionGuard.RequireAsync(ct);
        }
        private static async Task<T> InvokeAsync<T>(string rpc, string matchId, string requestId, CancellationToken ct)
        {
            var owner = Match3AccountOperation.Capture();
            if (!owner.IsCurrent) throw new OperationCanceledException("Authentication changed.", ct);
#if UNITY_EDITOR
            var transport = EditorTransportOverride;
#endif
            await RequireConnectionAsync(ct);
            // A socket/token refresh keeps this lifetime; a same-user logout/login does not.
            if (!owner.IsSameAuthentication) throw new OperationCanceledException("Authentication changed.", ct);
            ct.ThrowIfCancellationRequested();
            owner = Match3AccountOperation.Capture();
            var payload = BuildPayload(matchId, requestId);
            string json;
#if UNITY_EDITOR
            if (transport != null) json = await transport(rpc, payload, ct);
            else
#endif
            {
                if (owner.Bootstrap.Client == null) throw new InvalidOperationException("No authenticated client.");
                var result = await owner.Bootstrap.Client.RpcAsync(owner.Session, rpc, payload, canceller: ct);
                json = result?.Payload ?? "{}";
            }
            ct.ThrowIfCancellationRequested();
            await MainThreadDispatcher.RunAsync(() => { if (!owner.IsCurrent) throw new OperationCanceledException("Authentication changed.", ct); });
            return JsonUtility.FromJson<T>(json);
        }
    }
}
