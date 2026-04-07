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
        private const string RpcCharacterItemMove = "duel_character_item_move";

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

        public static Task<CharacterGetRpcResponse> MoveInvToEquipAsync(int invIndex, int slotIndex, CancellationToken ct) =>
            RpcItemMoveAsync("{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"op\":\"inv_to_equip\",\"inv_index\":" + invIndex + ",\"slot_index\":" + slotIndex + "}", ct);

        public static Task<CharacterGetRpcResponse> MoveEquipToInvAsync(int slotIndex, int invIndex, CancellationToken ct) =>
            RpcItemMoveAsync("{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"op\":\"equip_to_inv\",\"slot_index\":" + slotIndex + ",\"inv_index\":" + invIndex + "}", ct);

        public static Task<CharacterGetRpcResponse> SwapInventoryAsync(int invA, int invB, CancellationToken ct) =>
            RpcItemMoveAsync("{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"op\":\"inv_swap\",\"inv_a\":" + invA + ",\"inv_b\":" + invB + "}", ct);

        public static Task<CharacterGetRpcResponse> SwapEquipmentAsync(int slotA, int slotB, CancellationToken ct) =>
            RpcItemMoveAsync("{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"op\":\"equip_swap\",\"slot_a\":" + slotA + ",\"slot_b\":" + slotB + "}", ct);

        private static async Task<CharacterGetRpcResponse> RpcItemMoveAsync(string payload, CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_ready" };

            try
            {
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, RpcCharacterItemMove, payload, canceller: ct);

                var body = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(body))
                    return new CharacterGetRpcResponse { ok = false, err = "empty_payload" };

                var model = JsonUtility.FromJson<CharacterGetRpcResponse>(body);
                return model ?? new CharacterGetRpcResponse { ok = false, err = "parse_failed" };
            }
            catch (Exception e)
            {
                return new CharacterGetRpcResponse { ok = false, err = e.Message };
            }
        }
    }
}

