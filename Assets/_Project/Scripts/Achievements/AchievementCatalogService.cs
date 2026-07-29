using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Achievements
{
    /// <summary>
    /// Загрузка каталога достижений с сервера (duel_match3_achievement_catalog_get) и синхронизация прогресса при входе.
    /// Приоритет: свежий RPC → кэш только если RPC недоступен.
    /// </summary>
    public static class AchievementCatalogService
    {
        public const string RpcCatalogGet = "duel_match3_achievement_catalog_get";
        public const string RpcAchievementSync = "duel_match3_achievement_sync";

        private const string CatalogCacheKey = "achievements.server_catalog.v2";
        private const string CatalogCacheUpdatedAtKey = "achievements.server_catalog.updated_at.v2";
        private const string CatalogCacheSourceKey = "achievements.server_catalog.source.v2";

        private static bool _refreshInFlight;
        private static long _lastAppliedUpdatedAt = -1;

        public static long LastAppliedUpdatedAt => _lastAppliedUpdatedAt;
        public static string LastCatalogSource { get; private set; } = "";

        /// <summary>Офлайн-запасной вариант: только если сервер ещё не отвечал в этой сессии.</summary>
        public static void TryApplyCachedCatalogIfNeeded()
        {
            if (AchievementCatalog.IsFromServer)
                return;
            try
            {
                var json = PlayerPrefs.GetString(CatalogCacheKey, string.Empty);
                if (string.IsNullOrWhiteSpace(json))
                    return;
                if (!TryParseAndApplyCatalog(json, out var meta))
                    return;
                _lastAppliedUpdatedAt = meta.UpdatedAt;
                LastCatalogSource = meta.Source;
                AchievementLifecycle.NotifyDataChanged();
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Achievements] cached catalog: " + e.Message);
            }
        }

        /// <summary>После подключения к Nakama: каталог + authoritative sync счётчиков/claim.</summary>
        public static Task RefreshOnLoginAsync(CancellationToken ct) =>
            RefreshCatalogAsync(ct, forceServerRefresh: true);

        /// <summary>Перед открытием панели: подтянуть свежий каталог, если есть сеть.</summary>
        public static Task RefreshBeforePanelOpenAsync(CancellationToken ct) =>
            RefreshCatalogAsync(ct, forceServerRefresh: true);

        private static async Task RefreshCatalogAsync(CancellationToken ct, bool forceServerRefresh)
        {
            if (_refreshInFlight)
                return;
            _refreshInFlight = true;
            try
            {
                var catalogChanged = await RefreshCatalogFromServerAsync(ct, forceServerRefresh).ConfigureAwait(false);
                if (!catalogChanged && !AchievementCatalog.IsFromServer)
                    await MainThreadDispatcher.RunAsync(TryApplyCachedCatalogIfNeeded).ConfigureAwait(false);

                await SyncProgressFromServerAsync(ct).ConfigureAwait(false);
                if (catalogChanged)
                    await MainThreadDispatcher.RunAsync(AchievementLifecycle.NotifyDataChanged).ConfigureAwait(false);
            }
            finally
            {
                _refreshInFlight = false;
            }
        }

        private static async Task<bool> RefreshCatalogFromServerAsync(CancellationToken ct, bool forceServerRefresh)
        {
            var bootstrap = NakamaBootstrap.Instance;
            if (bootstrap?.Client == null)
                return false;

            try
            {
                await bootstrap.EnsureConnectedAsync(ct).ConfigureAwait(false);
                if (!bootstrap.IsReady || bootstrap.Session == null)
                    return false;

                IApiRpc rpc;
                try
                {
                    var rpcBody = forceServerRefresh
                        ? "{\"force_refresh\":true}"
                        : "{}";
                    rpc = await bootstrap.Client.RpcAsync(bootstrap.Session, RpcCatalogGet, rpcBody, canceller: ct)
                        .ConfigureAwait(false);
                }
                catch (Exception e) when (e.Message != null &&
                                          e.Message.IndexOf("Refresh token", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    await bootstrap.RecoverSessionAfterRefreshFailureAsync(ct).ConfigureAwait(false);
                    if (!bootstrap.IsReady || bootstrap.Session == null)
                        return false;
                    var rpcBody = forceServerRefresh
                        ? "{\"force_refresh\":true}"
                        : "{}";
                    rpc = await bootstrap.Client.RpcAsync(bootstrap.Session, RpcCatalogGet, rpcBody, canceller: ct)
                        .ConfigureAwait(false);
                }

                var payload = rpc?.Payload;
                if (string.IsNullOrWhiteSpace(payload))
                    return false;

                // Парсинг JSON без PlayerPrefs/Unity API — Apply только на main thread.
                if (!TryParseCatalog(payload, out var meta))
                    return false;

                var changed = await MainThreadDispatcher.RunAsync(() =>
                {
                    var cachedUpdatedAt = long.TryParse(PlayerPrefs.GetString(CatalogCacheUpdatedAtKey, "-1"), out var v)
                        ? v
                        : -1L;
                    var cachedPayload = PlayerPrefs.GetString(CatalogCacheKey, "");
                    var didChange = meta.UpdatedAt != _lastAppliedUpdatedAt
                        || meta.UpdatedAt != cachedUpdatedAt
                        || !string.Equals(payload, cachedPayload, StringComparison.Ordinal);

                    AchievementCatalog.ApplyFromServer(meta.Chains);
                    _lastAppliedUpdatedAt = meta.UpdatedAt;
                    LastCatalogSource = meta.Source;
                    PlayerPrefs.SetString(CatalogCacheKey, payload);
                    PlayerPrefs.SetString(CatalogCacheUpdatedAtKey, meta.UpdatedAt.ToString());
                    PlayerPrefs.SetString(CatalogCacheSourceKey, meta.Source ?? "");
                    PlayerPrefs.Save();

                    var cross = AchievementCatalog.FindChain("obs.cross");
                    var lastThreshold = cross?.Thresholds != null && cross.Thresholds.Length > 0
                        ? cross.Thresholds[cross.Thresholds.Length - 1]
                        : -1;
                    Debug.Log("[Achievements] catalog applied"
                        + " source=" + (meta.Source ?? "?")
                        + " updated_at=" + meta.UpdatedAt
                        + " obs.cross last_delta=" + lastThreshold
                        + (didChange ? " (changed)" : " (unchanged)"));
                    return didChange;
                }).ConfigureAwait(false);

                if (string.Equals(meta.Source, "fallback", StringComparison.OrdinalIgnoreCase))
                {
                    Debug.LogWarning("[Achievements] сервер отдал fallback Lua, а не Storage."
                        + " Проверьте user_id/collection/key в duel_match3_config.lua и запись в Nakama Storage.");
                }

                return changed || !AchievementCatalog.IsFromServer;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Achievements] catalog RPC: " + e.Message);
                return false;
            }
        }

        private static async Task SyncProgressFromServerAsync(CancellationToken ct)
        {
            var bootstrap = NakamaBootstrap.Instance;
            if (bootstrap?.Client == null || bootstrap.Session == null)
                return;

            try
            {
                await bootstrap.EnsureConnectedAsync(ct).ConfigureAwait(false);
                var sessionEpoch = await MainThreadDispatcher
                    .RunAsync(NakamaBootstrap.GetLocalSessionEpoch)
                    .ConfigureAwait(false);
                var body = JsonUtility.ToJson(new SessionEpochRpcPayload
                {
                    session_epoch = sessionEpoch,
                });

                IApiRpc rpc;
                try
                {
                    rpc = await bootstrap.Client.RpcAsync(bootstrap.Session, RpcAchievementSync, body, canceller: ct)
                        .ConfigureAwait(false);
                }
                catch (Exception e) when (e.Message != null &&
                                          e.Message.IndexOf("Refresh token", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    await bootstrap.RecoverSessionAfterRefreshFailureAsync(ct).ConfigureAwait(false);
                    if (bootstrap.Session == null) return;
                    rpc = await bootstrap.Client.RpcAsync(bootstrap.Session, RpcAchievementSync, body, canceller: ct)
                        .ConfigureAwait(false);
                }

                var parsed = JsonUtility.FromJson<AchievementSyncRpcResponse>(rpc?.Payload ?? "{}");
                if (parsed == null || !parsed.ok)
                    return;

                await MainThreadDispatcher.RunAsync(() =>
                {
                    AchievementProgressStorage.MergeFromAuthoritativeSnapshot(
                        parsed.achievement_stats_flat,
                        parsed.achievement_claimed);
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Achievements] sync RPC: " + e.Message);
            }
        }

        private static bool TryParseAndApplyCatalog(string json, out CatalogApplyMeta meta)
        {
            if (!TryParseCatalog(json, out meta))
                return false;
            AchievementCatalog.ApplyFromServer(meta.Chains);
            return true;
        }

        /// <summary>Только парсинг JSON (безопасно с background thread). Apply — на main thread.</summary>
        private static bool TryParseCatalog(string json, out CatalogApplyMeta meta)
        {
            meta = default;
            var chains = ParseCatalogChains(json, out var response);
            if (chains == null || chains.Length == 0)
                return false;

            meta = new CatalogApplyMeta
            {
                Chains = chains,
                UpdatedAt = response != null ? response.updated_at : 0,
                Source = response?.catalog_source ?? "",
            };
            return true;
        }

        private static AchievementChainDefinition[] ParseCatalogChains(string json, out AchievementCatalogRpcResponse response)
        {
            response = JsonUtility.FromJson<AchievementCatalogRpcResponse>(json);
            if (response == null || !response.ok || response.chains == null || response.chains.Length == 0)
                return null;

            var built = new List<AchievementChainDefinition>(response.chains.Length);
            foreach (var ch in response.chains)
            {
                var def = BuildChainDefinition(ch);
                if (def != null)
                    built.Add(def);
            }

            return built.Count == 0 ? null : built.ToArray();
        }

        private static AchievementChainDefinition BuildChainDefinition(AchievementChainDto ch)
        {
            if (ch == null || string.IsNullOrWhiteSpace(ch.id) || string.IsNullOrWhiteSpace(ch.counter_key))
                return null;
            if (ch.steps == null || ch.steps.Length == 0)
                return null;

            var n = ch.steps.Length;
            var thresholds = new int[n];
            var descriptions = new string[n];
            var rewards = new string[n];
            for (var i = 0; i < n; i++)
            {
                var st = ch.steps[i];
                if (st == null)
                    return null;
                thresholds[i] = Mathf.Max(0, st.threshold_delta);
                descriptions[i] = st.description_ru ?? string.Empty;
                rewards[i] = st.reward_text_ru ?? string.Empty;
            }

            return AchievementCatalog.ChainFromServer(
                ch.id,
                ch.category,
                ch.counter_key,
                thresholds,
                descriptions,
                rewards);
        }

        private struct CatalogApplyMeta
        {
            public AchievementChainDefinition[] Chains;
            public long UpdatedAt;
            public string Source;
        }

        [Serializable]
        private sealed class AchievementCatalogRpcResponse
        {
            public bool ok;
            public string err;
            public string catalog_source;
            public int schema_version;
            public long updated_at;
            public AchievementChainDto[] chains;
        }

        [Serializable]
        private sealed class AchievementChainDto
        {
            public string id;
            public string category;
            public string title_ru;
            public string counter_key;
            public string threshold_mode;
            public AchievementStepDto[] steps;
        }

        [Serializable]
        private sealed class AchievementStepDto
        {
            public int threshold_delta;
            public string description_ru;
            public string reward_text_ru;
        }

        [Serializable]
        private sealed class SessionEpochRpcPayload
        {
            public int session_epoch;
        }

        [Serializable]
        private sealed class AchievementSyncRpcResponse
        {
            public bool ok;
            public string err;
            public AchievementProgressStorage.StringIntPair[] achievement_stats_flat;
            public string[] achievement_claimed;
        }
    }
}
