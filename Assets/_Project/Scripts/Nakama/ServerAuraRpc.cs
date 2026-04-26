using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using UnityEngine;

namespace Project.Nakama
{
    [Serializable]
    public sealed class ServerAuraGetRpcResponse
    {
        public bool ok;
        public bool active;
        public string title;
        public string description;
        public long endsAtUnix;
        public float allStatsPct;
        public float critPct;
        public float hpPct;
        public float damagePct;
        public float armorPct;
        public float healingPct;
        public float xpBonusPct;
        public float mineRespawnWaitPct;
        public float durationHours;
    }

    public static class ServerAuraRpc
    {
        public const string RpcName = "duel_match3_server_aura_get";

        public static async Task<ServerAuraGetRpcResponse> GetAsync(IClient client, ISession session, CancellationToken ct)
        {
            if (client == null || session == null)
                return new ServerAuraGetRpcResponse { ok = false, active = false };

            var r = await client.RpcAsync(session, RpcName, "{}", canceller: ct).ConfigureAwait(true);
            var json = r?.Payload ?? "{}";
            try
            {
                return JsonUtility.FromJson<ServerAuraGetRpcResponse>(json);
            }
            catch
            {
                return new ServerAuraGetRpcResponse { ok = false, active = false };
            }
        }
    }
}
