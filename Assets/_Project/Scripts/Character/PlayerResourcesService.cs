using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using UnityEngine;

namespace Project.Character
{
    public static class PlayerResourcesService
    {
        private const string RpcPlayerResourcesGet = "duel_player_resources_get";
        private const string RpcPlayerResourcesSpend = "duel_player_resources_spend";
        private const string PlayerResourcesCacheKey = "nakama.cache.player_resources_v1";

        /// <summary>Последний успешный снимок ресурсов (устраняет краткий «—» на медленном старте / APK).</summary>
        public static bool TryReadCached(out PlayerResourcesRpcResponse model)
        {
            model = null;
            var json = PlayerPrefs.GetString(PlayerResourcesCacheKey, "");
            if (string.IsNullOrWhiteSpace(json))
                return false;
            try
            {
                model = JsonUtility.FromJson<PlayerResourcesRpcResponse>(json);
                return model != null && model.ok;
            }
            catch
            {
                return false;
            }
        }

        private static void WriteCache(PlayerResourcesRpcResponse model)
        {
            if (model == null || !model.ok)
                return;
            try
            {
                PlayerPrefs.SetString(PlayerResourcesCacheKey, JsonUtility.ToJson(model));
                PlayerPrefs.Save();
            }
            catch
            {
                // ignored
            }
        }

        public static async Task<PlayerResourcesRpcResponse> GetAsync(CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new PlayerResourcesRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new PlayerResourcesRpcResponse { ok = false, err = "nakama_not_ready" };

            try
            {
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcPlayerResourcesGet, "{}", canceller: ct);

                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return new PlayerResourcesRpcResponse { ok = false, err = "empty_payload" };

                var model = JsonUtility.FromJson<PlayerResourcesRpcResponse>(payload);
                if (model != null && model.ok)
                    WriteCache(model);
                return model ?? new PlayerResourcesRpcResponse { ok = false, err = "parse_failed" };
            }
            catch (Exception e)
            {
                return new PlayerResourcesRpcResponse { ok = false, err = e.Message };
            }
        }

        public static Task<PlayerResourcesRpcResponse> SpendEnergyAsync(int amount, string reason, CancellationToken ct)
        {
            var request = new PlayerResourceSpendRpcRequest
            {
                resource = "energy",
                amount = Math.Max(0, amount),
                reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason,
                session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
            };

            return SpendAsync(JsonUtility.ToJson(request), ct);
        }

        private static async Task<PlayerResourcesRpcResponse> SpendAsync(string payload, CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new PlayerResourcesRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new PlayerResourcesRpcResponse { ok = false, err = "nakama_not_ready" };

            try
            {
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcPlayerResourcesSpend, payload, canceller: ct);

                var body = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(body))
                    return new PlayerResourcesRpcResponse { ok = false, err = "empty_payload" };

                var model = JsonUtility.FromJson<PlayerResourcesRpcResponse>(body);
                return model ?? new PlayerResourcesRpcResponse { ok = false, err = "parse_failed" };
            }
            catch (Exception e)
            {
                return new PlayerResourcesRpcResponse { ok = false, err = e.Message };
            }
        }
    }
}
