using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Character
{
    public static class CharacterProfileService
    {
        private const string RpcCharacterGet = "duel_character_get";
        private const string RpcCharacterItemMove = "duel_character_item_move";
        private const string RpcCharacterRecipeLearn = "duel_character_recipe_learn";
        private const string RpcCharacterItemSell = "duel_character_item_sell";
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
                var session = NakamaBootstrap.Instance.Session;
                var scope = await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.Current).ConfigureAwait(false);
                if (!ReferenceEquals(session, NakamaBootstrap.Instance.Session))
                    return new CharacterGetRpcResponse { ok = false, err = "stale_session" };
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    session, RpcCharacterGet, "{}", canceller: ct);

                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return new CharacterGetRpcResponse { ok = false, err = "empty_payload" };

                var model = JsonUtility.FromJson<CharacterGetRpcResponse>(payload);
                if (!await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.IsCurrent(scope)).ConfigureAwait(false))
                    return new CharacterGetRpcResponse { ok = false, err = "stale_session" };
                if (model?.progression != null) model.progression.localAccountScope = scope;
                return model ?? new CharacterGetRpcResponse { ok = false, err = "parse_failed" };
            }
            catch (Exception e)
            {
                return new CharacterGetRpcResponse { ok = false, err = e.Message };
            }
        }

        public static Task<CharacterGetRpcResponse> DeliverPendingLootAsync(CancellationToken ct) =>
            RpcCharacterPayloadAsync("duel_character_loot_deliver",
                "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + "}", ct);

        public static Task<CharacterGetRpcResponse> BuyInventorySlotAsync(int slotIndex, string requestId, CancellationToken ct)
        {
            if (slotIndex < 25 || slotIndex >= 50 || string.IsNullOrEmpty(requestId))
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "bad_slot_index" });
            var escaped = requestId.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return RpcCharacterPayloadAsync("duel_character_inventory_slot_buy",
                "{\"session_epoch\":" + NakamaBootstrap.GetLocalSessionEpoch() + ",\"slot_index\":" + slotIndex +
                ",\"request_id\":\"" + escaped + "\"}", ct);
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

        [Serializable] private sealed class ConsumableRequest
        {
            public int session_epoch, inv_index;
            public string expected_def_id, request_id;
            public long use_seq;
        }

        public static Task<CharacterGetRpcResponse> UseConsumableAsync(int index, string itemId, string requestId, long sequence, CancellationToken ct)
        {
            if (index < 0 || index >= 50 || !ConsumablePresentation.IsKnown(itemId) || string.IsNullOrEmpty(requestId) || sequence<1)
                return Task.FromResult(new CharacterGetRpcResponse { ok=false, err="bad_consumable_request" });
            return RpcCharacterPayloadAsync("duel_consumables_use", JsonUtility.ToJson(new ConsumableRequest {
                session_epoch=NakamaBootstrap.GetLocalSessionEpoch(), inv_index=index, expected_def_id=itemId, request_id=requestId,use_seq=sequence
            }),ct);
        }

        [Serializable]
        private sealed class SaleRequest
        {
            public int session_epoch;
            public string source;
            public int inv_index = -1;
            public int slot_index = -1;
            public string expected_def_id;
            public string request_id;
        }

        public static Task<CharacterGetRpcResponse> SellInventoryItemAsync(int inventoryIndex, string expectedDefId, string requestId, CancellationToken ct) =>
            SellItemAsync("inventory", inventoryIndex, expectedDefId, requestId, ct);

        public static Task<CharacterGetRpcResponse> SellEquipmentItemAsync(int slotIndex, string expectedDefId, string requestId, CancellationToken ct) =>
            SellItemAsync("equipment", slotIndex, expectedDefId, requestId, ct);

        private static Task<CharacterGetRpcResponse> SellItemAsync(string source, int index, string expectedDefId, string requestId, CancellationToken ct)
        {
            if (index < 0 || index >= (source == "inventory" ? 50 : 8) || string.IsNullOrEmpty(expectedDefId) || string.IsNullOrEmpty(requestId))
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "bad_sale_request" });
            return RpcCharacterPayloadAsync(RpcCharacterItemSell, JsonUtility.ToJson(new SaleRequest
            {
                session_epoch = NakamaBootstrap.GetLocalSessionEpoch(), source = source,
                inv_index = source == "inventory" ? index : -1, slot_index = source == "equipment" ? index : -1,
                expected_def_id = expectedDefId, request_id = requestId
            }), ct);
        }

#if UNITY_EDITOR
        internal static Func<string, CancellationToken, Task<CharacterGetRpcResponse>> EditorCraftStartTransportOverride;
#endif
        public static Task<CharacterGetRpcResponse> WorkshopCraftStartAsync(int slotIndex, string outputDefId, int expectedIngotN, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(outputDefId))
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "empty_output_def_id" });
            if (slotIndex < 0 || slotIndex > 7)
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "bad_slot_index" });
            if (expectedIngotN < 0 || expectedIngotN > 1000000000)
                return Task.FromResult(new CharacterGetRpcResponse { ok = false, err = "invalid_craft_quote" });
            var payload = BuildCraftStartPayload(slotIndex, outputDefId, expectedIngotN, NakamaBootstrap.GetLocalSessionEpoch());
#if UNITY_EDITOR
            if (EditorCraftStartTransportOverride != null) return EditorCraftStartTransportOverride(payload, ct);
#endif
            return RpcCharacterPayloadAsync(RpcWorkshopCraftStart, payload, ct);
        }

        internal static string BuildCraftStartPayload(int slotIndex, string outputDefId, int expectedIngotN, int epoch)
        {
            var escaped = outputDefId.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return "{\"session_epoch\":" + epoch + ",\"slot_index\":" + slotIndex + ",\"output_def_id\":\"" + escaped +
                "\",\"expected_ingot_n\":" + expectedIngotN + "}";
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

            var requestedScope = await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.Current).ConfigureAwait(false);
            await NakamaBootstrap.Instance.EnsureConnectedAsync(ct).ConfigureAwait(false);
            if (!NakamaBootstrap.Instance.IsReady || NakamaBootstrap.Instance.Client == null || NakamaBootstrap.Instance.Session == null)
                return new CharacterGetRpcResponse { ok = false, err = "nakama_not_ready" };

            try
            {
                var session = NakamaBootstrap.Instance.Session;
                var scope = await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.Current).ConfigureAwait(false);
                if (!ReferenceEquals(session, NakamaBootstrap.Instance.Session)
                    || (requestedScope != null && !string.Equals(requestedScope, scope, StringComparison.Ordinal)))
                    return new CharacterGetRpcResponse { ok = false, err = "stale_session" };
                var rpc = await NakamaBootstrap.Instance.Client.RpcAsync(
                    session, rpcId, payload, canceller: ct);

                var body = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(body))
                    return new CharacterGetRpcResponse { ok = false, err = "empty_payload" };

                var model = JsonUtility.FromJson<CharacterGetRpcResponse>(body);
                if (!await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.IsCurrent(scope)).ConfigureAwait(false))
                    return new CharacterGetRpcResponse { ok = false, err = "stale_session" };
                if (model?.progression != null) model.progression.localAccountScope = scope;
                return model ?? new CharacterGetRpcResponse { ok = false, err = "parse_failed" };
            }
            catch (Exception e)
            {
                return new CharacterGetRpcResponse { ok = false, err = e.Message };
            }
        }

        private static Task<CharacterGetRpcResponse> RpcItemMoveAsync(string payload, CancellationToken ct) =>
            RpcCharacterPayloadAsync(RpcCharacterItemMove, payload, ct);
    }
}

