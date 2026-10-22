using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Character;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.Achievements
{
    /// <summary>Ручное получение награды шага: RPC duel_match3_achievement_claim_step + локальный Mark при успехе.</summary>
    public static class AchievementRewardClaim
    {
        public const string RpcClaimStep = "duel_match3_achievement_claim_step";

        /// <returns>true если шаг доступен для кнопки «Получить».</returns>
        public static bool CanClaimStep(AchievementChainDefinition chain, int stepIndex)
        {
            if (chain == null) return false;
            if (stepIndex < 0 || stepIndex >= chain.Thresholds.Length) return false;
            for (var j = 0; j < stepIndex; j++)
            {
                if (!AchievementProgressStorage.IsStepClaimed(chain.ChainId, j))
                    return false;
            }
            var stat = AchievementProgressStorage.GetStat(chain.StatKey);
            if (AchievementUiRules.IsSlotLocked(chain, stepIndex, stat)) return false;
            if (stat < AchievementUiRules.CumulativeRequired(chain, stepIndex)) return false;
            return !AchievementProgressStorage.IsStepClaimed(chain.ChainId, stepIndex);
        }

        public static async Task<bool> TryClaimStepAsync(string chainId, int stepIndex, CancellationToken ct)
        {
            if (string.IsNullOrEmpty(chainId)) return false;
            var def = AchievementCatalog.FindChain(chainId);
            if (def == null || !CanClaimStep(def, stepIndex))
                return false;

            var bootstrap = NakamaBootstrap.Instance;
            if (bootstrap != null)
            {
                try
                {
                    var requestedScope = LocalAccountCacheScope.Current;
                    await bootstrap.EnsureConnectedAsync(ct);
                    var client = bootstrap.Client;
                    var session = bootstrap.Session;
                    var scope = await MainThreadDispatcher.RunAsync(() => LocalAccountCacheScope.Current);
                    if (client == null || session == null || scope == null || !ReferenceEquals(session, bootstrap.Session)
                        || (requestedScope != null && !string.Equals(requestedScope, scope, StringComparison.Ordinal))) return false;
                    var rpcBody = JsonUtility.ToJson(new ClaimStepRpcPayload
                    {
                        chain_id = chainId,
                        step_index = stepIndex,
                        session_epoch = NakamaBootstrap.GetLocalSessionEpoch(),
                    });
                    var rpc = await client.RpcAsync(session, RpcClaimStep, rpcBody, canceller: ct);
                    var resp = JsonUtility.FromJson<ClaimStepRpcResponse>(rpc.Payload ?? "{}");
                    if (resp != null && resp.ok == true)
                    {
                        var applied = await MainThreadDispatcher.RunAsync(() =>
                        {
                            if (!LocalAccountCacheScope.IsCurrent(scope)) return false;
                            AchievementProgressStorage.MarkStepClaimed(chainId, stepIndex);
                            AchievementUiPending.ClearAwaitToastTokenForClaimedStep(chainId, stepIndex);
                            if (resp.stats != null) AchievementLifecycle.NotifyCombatStatsUpdated(resp.stats);
                            RaiseClaimed(def, stepIndex);
                            return true;
                        });
                        if (!applied) return false;
                        if (resp.stats == null)
                            await RefreshCombatStatsFromCharacterGetAsync(scope, ct).ConfigureAwait(false);
                        return true;
                    }
                }
                catch (ApiResponseException ex)
                {
                    Debug.LogWarning("[Achievements] claim RPC: " + ex.Message);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning("[Achievements] claim: " + ex.Message);
                }
                return false;
            }

            // Rewards are authoritative: a disconnected client cannot claim and persist a local reward.
            return false;
        }

        private static void RaiseClaimed(AchievementChainDefinition def, int stepIndex)
        {
            var info = new AchievementUnlockInfo
            {
                ChainId = def.ChainId,
                StepIndex = stepIndex,
                NoticeKind = AchievementNoticeKind.RewardGrantedToast,
                Title = def.Descriptions != null && stepIndex < def.Descriptions.Length ? def.Descriptions[stepIndex] : def.ChainId,
                RewardLine = def.RewardTexts != null && stepIndex < def.RewardTexts.Length ? def.RewardTexts[stepIndex] : "",
            };
            AchievementLifecycle.NotifyRewardClaimed(info);
            AchievementLifecycle.NotifyDataChanged();
        }

        private static async Task RefreshCombatStatsFromCharacterGetAsync(string scope, CancellationToken ct)
        {
            try
            {
                var profile = await CharacterProfileService.GetAsync(ct).ConfigureAwait(false);
                if (profile != null && profile.ok && profile.stats != null)
                    await MainThreadDispatcher.RunAsync(() =>
                    {
                        if (LocalAccountCacheScope.IsCurrent(scope)) AchievementLifecycle.NotifyCombatStatsUpdated(profile.stats);
                    }).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[Achievements] refresh stats after claim: " + ex.Message);
            }
        }

        [Serializable]
        private sealed class ClaimStepRpcPayload
        {
            public string chain_id;
            public int step_index;
            public int session_epoch;
        }

        [Serializable]
        private sealed class ClaimStepRpcResponse
        {
            public bool ok;
            public string err;
            public string chain_id;
            public int step_index;
            public StatsMap stats;
        }
    }
}
