using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using UnityEngine;

namespace Project.Leaderboard
{
    public static class LeaderboardService
    {
        private const string RpcLeaderboardGet = "duel_leaderboard_get";

        public static async Task<LeaderboardGetRpcResponse> GetAsync(
            LeaderboardPeriod period,
            LeaderboardType type,
            string viewId,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(viewId))
                viewId = LeaderboardFilterCatalog.DefaultView(type).Id;

            if (NakamaBootstrap.Instance == null)
                return new LeaderboardGetRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady
                || NakamaBootstrap.Instance.Client == null
                || NakamaBootstrap.Instance.Session == null)
            {
                return new LeaderboardGetRpcResponse { ok = false, err = "nakama_not_ready" };
            }

            try
            {
                var req = new LeaderboardGetRpcRequest
                {
                    period = LeaderboardPeriodIds.ToId(period),
                    type = LeaderboardTypeIds.ToId(type),
                    view_id = viewId,
                };
                var body = JsonUtility.ToJson(req);
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcLeaderboardGet, body, canceller: ct)
                    .ConfigureAwait(false);

                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return new LeaderboardGetRpcResponse { ok = false, err = "empty_payload" };

                var model = JsonUtility.FromJson<LeaderboardGetRpcResponse>(payload);
                return model ?? new LeaderboardGetRpcResponse { ok = false, err = "parse_failed" };
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Leaderboard] Get failed: " + e.Message);
                return new LeaderboardGetRpcResponse { ok = false, err = e.Message };
            }
        }
    }
}
