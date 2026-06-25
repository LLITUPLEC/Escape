using System;
using System.Threading;
using System.Threading.Tasks;
using Nakama;
using Project.Character;
using Project.Nakama;
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

            var client = NakamaBootstrap.Instance?.Client;
            var session = NakamaBootstrap.Instance?.Session;

            if (client != null && session != null)
            {
                try
                {
                    await NakamaBootstrap.Instance.EnsureConnectedAsync(ct);
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
                        AchievementProgressStorage.MarkStepClaimed(chainId, stepIndex);
                        AchievementUiPending.ClearAwaitToastTokenForClaimedStep(chainId, stepIndex);
                        if (resp.stats != null)
                            AchievementLifecycle.NotifyCombatStatsUpdated(resp.stats);
                        else
                            await RefreshCombatStatsFromCharacterGetAsync(ct).ConfigureAwait(false);
                        RaiseClaimed(def, stepIndex);
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

            AchievementProgressStorage.MarkStepClaimed(chainId, stepIndex);
            AchievementUiPending.ClearAwaitToastTokenForClaimedStep(chainId, stepIndex);
            RaiseClaimed(def, stepIndex);
            return true;
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

        private static async Task RefreshCombatStatsFromCharacterGetAsync(CancellationToken ct)
        {
            try
            {
                var profile = await CharacterProfileService.GetAsync(ct).ConfigureAwait(false);
                if (profile != null && profile.ok && profile.stats != null)
                    AchievementLifecycle.NotifyCombatStatsUpdated(profile.stats);
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
