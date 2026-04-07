using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using UnityEngine;

namespace Project.Character
{
    public static class CharacterProfileService
    {
        private const string RpcCharacterGet = "duel_character_get";

        public static async Task<CharacterGetRpcResponse> GetAsync(CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_ready" };

            try
            {
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcCharacterGet, "{}", canceller: ct);

                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return new CharacterGetRpcResponse { ok = false, err = "empty_payload" };

                var model = JsonUtility.FromJson<CharacterGetRpcResponse>(payload);
                return model ?? new CharacterGetRpcResponse { ok = false, err = "parse_failed" };
            }
            catch (Exception e)
            {
                return new CharacterGetRpcResponse { ok = false, err = e.Message };
            }
        }
    }
}

