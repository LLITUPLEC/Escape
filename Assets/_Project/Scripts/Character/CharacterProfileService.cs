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
        private const string RpcCharacterRecipeLearn = "duel_character_recipe_learn";
        private const string RpcWorkshopCraftStart = "duel_workshop_craft_start";
        private const string RpcWorkshopCraftClaim = "duel_workshop_craft_claim";
        private const string RpcWorkshopCraftRush = "duel_workshop_craft_rush";

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

        public static Task<CharacterGetRpcResponse> LearnRecipeAsync(int inventoryIndex, CancellationToken ct) =>
            RpcCharacterPayloadAsync(RpcCharacterRecipeLearn,
                "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"inv_index\":" + inventoryIndex + "}", ct);

        public static Task<CharacterGetRpcResponse> WorkshopCraftStartAsync(int slotIndex, string outputDefId, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(outputDefId))
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "empty_output_def_id" });
            if (slotIndex < 0 || slotIndex > 7)
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "bad_slot_index" });
            var escaped = outputDefId.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return RpcCharacterPayloadAsync(RpcWorkshopCraftStart,
                "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"slot_index\":" + slotIndex + ",\"output_def_id\":\"" + escaped + "\"}", ct);
        }

        public static Task<CharacterGetRpcResponse> WorkshopCraftClaimAsync(int slotIndex, CancellationToken ct)
        {
            if (slotIndex < 0 || slotIndex > 7)
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "bad_slot_index" });
            return RpcCharacterPayloadAsync(RpcWorkshopCraftClaim,
                "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"slot_index\":" + slotIndex + "}", ct);
        }

        public static Task<CharacterGetRpcResponse> WorkshopCraftRushAsync(int slotIndex, CancellationToken ct)
        {
            if (slotIndex < 0 || slotIndex > 7)
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "bad_slot_index" });
            return RpcCharacterPayloadAsync(RpcWorkshopCraftRush,
                "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"slot_index\":" + slotIndex + "}", ct);
        }

        private static async Task<CharacterGetRpcResponse> RpcCharacterPayloadAsync(string rpcId, string payload, CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_ready" };

            try
            {
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    NakamaBootstrap.Instance.Session, rpcId, payload, canceller: ct);

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

        private static async Task<CharacterGetRpcResponse> RpcItemMoveAsync(string payload, CancellationToken ct)
        {
            if (NakamaBootstrap.Instance == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_initialized" };

            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_ready" };

            return await RpcCharacterPayloadAsync(RpcCharacterItemMove, payload, ct).ConfigureAwait(false);
        }
    }
}

