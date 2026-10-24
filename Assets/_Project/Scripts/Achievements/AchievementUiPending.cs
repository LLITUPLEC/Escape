using System;
using System.Collections.Generic;

namespace Project.Achievements
{
    /// <summary>Снимок шагов, для которых порог уже выполнен, но награда ещё не получена (кнопка «Получить» активна).</summary>
    public static class AchievementUiPending
    {
        public static readonly StringComparer TokenComparer = StringComparer.Ordinal;

        /// <summary>Сбрасывается после «Получить», чтобы следующий сезон аккаунта мог снова показать ожидание.</summary>
        private static readonly HashSet<string> AwaitClaimToastSentForStepTokens = new HashSet<string>(TokenComparer);

        public static void ClearAwaitToastTokenForClaimedStep(string chainId, int stepIndex)
        {
            if (string.IsNullOrEmpty(chainId) || stepIndex < 0)
                return;
            AwaitClaimToastSentForStepTokens.Remove($"{chainId}|{stepIndex}");
        }

        public static void ClearAllAwaitToastTokens() => AwaitClaimToastSentForStepTokens.Clear();

        public static HashSet<string> CaptureEligibleClaimStepTokens()
        {
            AchievementProgressStorage.EnsureLoaded();
            var hs = new HashSet<string>(TokenComparer);
            foreach (var chain in AchievementCatalog.Chains)
            {
                if (chain?.Thresholds == null)
                    continue;
                var n = chain.Thresholds.Length;
                for (var i = 0; i < n; i++)
                {
                    if (AchievementRewardClaim.CanClaimStep(chain, i))
                        hs.Add($"{chain.ChainId}|{i}");
                }
            }

            return hs;
        }

        /// <summary>Criterion completion is independent of the order in which rewards are claimed.</summary>
        public static HashSet<string> CaptureReachedUnclaimedStepTokens()
        {
            AchievementProgressStorage.EnsureLoaded();
            var tokens = new HashSet<string>(TokenComparer);
            foreach (var chain in AchievementCatalog.Chains)
            {
                if (chain?.Thresholds == null) continue;
                var total = AchievementProgressStorage.GetStat(chain.StatKey);
                for (var i = 0; i < chain.Thresholds.Length; i++)
                    if (total >= AchievementUiRules.CumulativeRequired(chain, i)
                        && !AchievementProgressStorage.IsStepClaimed(chain.ChainId, i))
                        tokens.Add($"{chain.ChainId}|{i}");
            }
            return tokens;
        }

        public static int CountEligibleClaimSteps()
        {
            AchievementProgressStorage.EnsureLoaded();
            var total = 0;
            foreach (var chain in AchievementCatalog.Chains)
            {
                total += CountEligibleClaimStepsInChain(chain);
            }

            return total;
        }

        public static int CountEligibleClaimSteps(AchievementTab tab)
        {
            AchievementProgressStorage.EnsureLoaded();
            var total = 0;
            foreach (var chain in AchievementCatalog.Chains)
            {
                if (chain == null || chain.Tab != tab)
                    continue;
                total += CountEligibleClaimStepsInChain(chain);
            }

            return total;
        }

        private static int CountEligibleClaimStepsInChain(AchievementChainDefinition chain)
        {
            if (chain?.Thresholds == null)
                return 0;
            var total = 0;
            var n = chain.Thresholds.Length;
            for (var i = 0; i < n; i++)
            {
                if (AchievementRewardClaim.CanClaimStep(chain, i))
                    total++;
            }

            return total;
        }

        /// <summary>After an existing counter/snapshot event, announce newly completed criteria. No network requests.</summary>
        /// <returns>Сколько новых событий ушло в <see cref="AchievementLifecycle.NotifyAwaitingClaim"/>.</returns>
        public static int RaiseNewCompletedSince(HashSet<string> eligibleBeforeMerge)
        {
            if (eligibleBeforeMerge == null)
                eligibleBeforeMerge = new HashSet<string>(TokenComparer);
            AchievementProgressStorage.EnsureLoaded();

            var raised = 0;
            foreach (var chain in AchievementCatalog.Chains)
            {
                if (chain?.Descriptions == null || chain.Thresholds == null)
                    continue;
                var total = AchievementProgressStorage.GetStat(chain.StatKey);
                var n = chain.Thresholds.Length;
                for (var i = 0; i < n; i++)
                {
                    if (AchievementProgressStorage.IsStepClaimed(chain.ChainId, i)
                        || total < AchievementUiRules.CumulativeRequired(chain, i))
                        continue;
                    var token = $"{chain.ChainId}|{i}";
                    if (eligibleBeforeMerge.Contains(token))
                        continue;
                    if (!AwaitClaimToastSentForStepTokens.Add(token))
                        continue;

                    var info = new AchievementUnlockInfo
                    {
                        ChainId = chain.ChainId,
                        StepIndex = i,
                        NoticeKind = AchievementNoticeKind.CriterionMetAwaitClaim,
                        Title = i < chain.Descriptions.Length ? chain.Descriptions[i] : chain.ChainId,
                        RewardLine = chain.RewardTexts != null && i < chain.RewardTexts.Length ? chain.RewardTexts[i] : "",
                    };

                    AchievementInGameToastHost.Ensure();
                    AchievementLifecycle.NotifyAwaitingClaim(info);
                    raised++;
                }
            }

            return raised;
        }
    }
}
