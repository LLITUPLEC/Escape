using System;
using System.Threading;
using System.Threading.Tasks;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Character
{
    public static class PlayerResourcesService
    {
        private const string RpcPlayerResourcesGet = "duel_player_resources_get";
        private const string RpcPlayerResourcesSpend = "duel_player_resources_spend";
        private const string RpcPveEnergyBuy = "duel_pve_energy_buy";
        private const string PlayerResourcesCacheKey = "nakama.cache.player_resources_v1";
        private static readonly SemaphoreSlim RequestGate = new(1, 1);
        private static long _requestSequence;
        private static long _lastAppliedSequence;
        private static string _loadedScope;
        private static PlayerResourcesRpcResponse _cached;

        /// <summary>Кэш ресурсов обновился. Событие всегда приходит с Unity main thread.</summary>
        public static event Action CachedResourcesChanged;

        /// <summary>Последний снимок только текущего сервера и текущего пользователя. Только main thread.</summary>
        public static bool TryReadCached(out PlayerResourcesRpcResponse model)
        {
            var scope = LocalAccountCacheScope.Current;
            if (!string.Equals(scope, _loadedScope, StringComparison.Ordinal))
            {
                _loadedScope = scope;
                _cached = null;
                _lastAppliedSequence = 0;
            }
            PlayerPrefs.DeleteKey(PlayerResourcesCacheKey); // Unscoped legacy data must never be imported.
            if (_cached == null && scope != null)
            {
                var json = PlayerPrefs.GetString(PlayerResourcesCacheKey + "." + scope, "");
                if (!string.IsNullOrWhiteSpace(json))
                {
                    try { _cached = JsonUtility.FromJson<PlayerResourcesRpcResponse>(json); }
                    catch (ArgumentException) { _cached = null; }
                }
            }
            model = _cached;
            return model != null && model.ok;
        }

        private static PlayerResourcesRpcResponse ApplySnapshot(string scope, long sequence, PlayerResourcesRpcResponse model)
        {
            if (!LocalAccountCacheScope.IsCurrent(scope))
                return Failed("stale_session");
            TryReadCached(out var current);
            // A confirmed progression mutation or a newer resource RPC has already won this race.
            if (sequence < _lastAppliedSequence)
                return current ?? Failed("superseded_response");
            _lastAppliedSequence = sequence;
            _cached = model;
            PlayerPrefs.SetString(PlayerResourcesCacheKey + "." + scope, JsonUtility.ToJson(model));
            PlayerPrefs.Save();
            CachedResourcesChanged?.Invoke();
            return model;
        }

        /// <summary>Подтверждённый сервером progression (продажа предмета и т.п.). Только main thread.</summary>
        public static void PatchCachedFromProgression(Progression progression)
        {
            var scope = LocalAccountCacheScope.Current;
            if (progression == null || scope == null) return;
            if (progression.localAccountScope != null && !string.Equals(progression.localAccountScope, scope, StringComparison.Ordinal)) return;
            TryReadCached(out var previous);
            var model = new PlayerResourcesRpcResponse
            {
                ok = true,
                gold = Math.Max(0L, progression.gold),
                ore = Math.Max(0L, progression.ore),
                ingots = Math.Max(0L, progression.ingots),
                matter = Math.Max(0L, progression.matter),
                keys = Math.Max(0L, progression.keys),
                energy = Math.Max(0, progression.energy),
                energy_max = Math.Max(0, progression.energy_max),
                miner_key = previous?.miner_key ?? 0,
                dark_key = previous?.dark_key ?? 0,
            };
            ApplySnapshot(scope, Interlocked.Increment(ref _requestSequence), model);
        }

        public static Task<PlayerResourcesRpcResponse> GetAsync(CancellationToken ct) =>
            RequestAsync(RpcPlayerResourcesGet, () => "{}", ct);

        public static Task<PlayerResourcesRpcResponse> SpendEnergyAsync(int amount, string reason, CancellationToken ct) =>
            RequestAsync(RpcPlayerResourcesSpend, () => JsonUtility.ToJson(new PlayerResourceSpendRpcRequest
            {
                resource = "energy",
                amount = Math.Max(0, amount),
                reason = string.IsNullOrWhiteSpace(reason) ? "unspecified" : reason,
                session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
            }), ct);

        public static Task<PlayerResourcesRpcResponse> BuyEnergyAsync(string mode, CancellationToken ct) =>
            BuyEnergyAsync(mode, count: 1, ct);

        public static Task<PlayerResourcesRpcResponse> BuyEnergyAsync(string mode, int count, CancellationToken ct)
        {
            if (!string.Equals(mode, "matter", StringComparison.Ordinal) && !string.Equals(mode, "gold", StringComparison.Ordinal))
                return Task.FromResult(Failed("bad_mode"));
            return RequestAsync(RpcPveEnergyBuy, () => JsonUtility.ToJson(new EnergyBuyRpcRequest
            {
                mode = mode,
                count = Math.Clamp(count, 1, 50),
                session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
            }), ct);
        }

        private static async Task<PlayerResourcesRpcResponse> RequestAsync(string rpcId, Func<string> buildPayload, CancellationToken ct)
        {
            var bootstrap = NakamaBootstrap.Instance;
            if (bootstrap == null) return Failed("nakama_not_initialized");
            var requestedScope = await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.Current).ConfigureAwait(false);
            await RequestGate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await bootstrap.EnsureConnectedAsync(ct).ConfigureAwait(false);
                if (!bootstrap.IsReady || bootstrap.Client == null || bootstrap.Session == null)
                    return Failed("nakama_not_ready");
                var session = bootstrap.Session;
                var scope = await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.Current).ConfigureAwait(false);
                if (scope == null || !ReferenceEquals(session, bootstrap.Session)) return Failed("stale_session");
                if (requestedScope != null && !string.Equals(requestedScope, scope, StringComparison.Ordinal)) return Failed("stale_session");
                var payload = buildPayload();
                var sequence = Interlocked.Increment(ref _requestSequence);
                var rpc = await bootstrap.Client.RpcAsync(session, rpcId, payload, canceller: ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(rpc?.Payload)) return Failed("empty_payload");
                var model = JsonUtility.FromJson<PlayerResourcesRpcResponse>(rpc.Payload);
                if (model == null) return Failed("parse_failed");
                if (!model.ok) return model;
                // PlayerPrefs and subscribers mutate Unity UI: never call them from the Nakama worker thread.
                return await MainThreadDispatcher.RunAsync(() => ApplySnapshot(scope, sequence, model)).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) { return Failed(e.Message); }
            finally { RequestGate.Release(); }
        }

        private static PlayerResourcesRpcResponse Failed(string error) => new() { ok = false, err = error };
    }
}
