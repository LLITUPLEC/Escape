using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Achievements
{
    /// <summary>
    /// Локальное persist счётчиков и меток выданных наград. В будущем можно синхронизировать с Nakama.
    /// </summary>
    public static class AchievementProgressStorage
    {
        private const string PrefStatsJson = "achievements.stats.v1";
        private const string PrefClaimedJson = "achievements.claimed_steps.v1";

        private static Dictionary<string, int> _stats;
        private static HashSet<string> _claimedStepTokens;

        public static void EnsureLoaded()
        {
            if (_stats != null && _claimedStepTokens != null)
                return;

            _stats = LoadDict(PrefStatsJson);
            _claimedStepTokens = LoadSet(PrefClaimedJson);
        }

        public static int GetStat(string key)
        {
            EnsureLoaded();
            return _stats.TryGetValue(key, out var v) ? v : 0;
        }

        public static void SetStat(string key, int value)
        {
            EnsureLoaded();
            _stats[key] = Mathf.Max(0, value);
            SaveDict(PrefStatsJson, _stats);
        }

        public static void AddStat(string key, int delta)
        {
            if (delta == 0) return;
            SetStat(key, GetStat(key) + delta);
        }

        public static bool IsStepClaimed(string chainId, int stepIndex)
        {
            EnsureLoaded();
            return _claimedStepTokens.Contains(StepToken(chainId, stepIndex));
        }

        public static void MarkStepClaimed(string chainId, int stepIndex)
        {
            EnsureLoaded();
            var t = StepToken(chainId, stepIndex);
            if (_claimedStepTokens.Add(t))
                SaveSet(PrefClaimedJson, _claimedStepTokens);
        }

        private static string StepToken(string chainId, int stepIndex) => chainId + ":" + stepIndex;

        private static Dictionary<string, int> LoadDict(string prefKey)
        {
            try
            {
                var json = PlayerPrefs.GetString(prefKey, string.Empty);
                if (string.IsNullOrEmpty(json))
                    return new Dictionary<string, int>(StringComparer.Ordinal);
                var wrap = JsonUtility.FromJson<StringIntDictWrapper>(json);
                var dict = new Dictionary<string, int>(StringComparer.Ordinal);
                if (wrap?.entries == null)
                    return dict;
                foreach (var e in wrap.entries)
                {
                    if (!string.IsNullOrEmpty(e.k))
                        dict[e.k] = e.v;
                }
                return dict;
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.Ordinal);
            }
        }

        private static void SaveDict(string prefKey, Dictionary<string, int> dict)
        {
            var list = new List<StringIntPair>(dict.Count);
            foreach (var kv in dict)
                list.Add(new StringIntPair { k = kv.Key, v = kv.Value });
            PlayerPrefs.SetString(prefKey, JsonUtility.ToJson(new StringIntDictWrapper { entries = list.ToArray() }));
            PlayerPrefs.Save();
        }

        private static HashSet<string> LoadSet(string prefKey)
        {
            try
            {
                var json = PlayerPrefs.GetString(prefKey, string.Empty);
                if (string.IsNullOrEmpty(json))
                    return new HashSet<string>(StringComparer.Ordinal);
                var wrap = JsonUtility.FromJson<StringArrayWrapper>(json);
                var hs = new HashSet<string>(StringComparer.Ordinal);
                if (wrap?.items != null)
                {
                    foreach (var s in wrap.items)
                    {
                        if (!string.IsNullOrEmpty(s))
                            hs.Add(s);
                    }
                }
                return hs;
            }
            catch
            {
                return new HashSet<string>(StringComparer.Ordinal);
            }
        }

        private static void SaveSet(string prefKey, HashSet<string> set)
        {
            var arr = new string[set.Count];
            var i = 0;
            foreach (var s in set)
                arr[i++] = s;
            PlayerPrefs.SetString(prefKey, JsonUtility.ToJson(new StringArrayWrapper { items = arr }));
            PlayerPrefs.Save();
        }

        [Serializable]
        private sealed class StringIntDictWrapper
        {
            public StringIntPair[] entries = Array.Empty<StringIntPair>();
        }

        [Serializable]
        private sealed class StringIntPair
        {
            public string k;
            public int v;
        }

        [Serializable]
        private sealed class StringArrayWrapper
        {
            public string[] items = Array.Empty<string>();
        }
    }

    /// <summary>Статический вход для игровых систем: счётчики и события UI.</summary>
    public static class AchievementTracker
    {
        public static event Action<AchievementUnlockInfo> StepCompleted;

        public static void AddStat(string key, int delta = 1)
        {
            if (delta == 0 || string.IsNullOrEmpty(key))
                return;
            AchievementProgressStorage.AddStat(key, delta);
            EvaluateChainsForStat(key);
        }

        public static void NotifyAbilityUsedMatch3(int actionType)
        {
            switch (actionType)
            {
                case 2:
                    AddStat(AchievementStatKeys.UsesCross);
                    break;
                case 3:
                    AddStat(AchievementStatKeys.UsesSquare);
                    break;
                case 4:
                    AddStat(AchievementStatKeys.UsesPetard);
                    break;
                case 5:
                    AddStat(AchievementStatKeys.UsesShield);
                    break;
                case 6:
                    AddStat(AchievementStatKeys.UsesFury);
                    break;
            }
        }

        public static void NotifyBlacksmithTournamentFinalWin()
        {
            AddStat(AchievementStatKeys.TournamentSmithWinFinal);
        }

        public static void NotifyTriMatchDuelWin()
        {
            AddStat(AchievementStatKeys.DuelTriWin);
        }

        public static void NotifyPetardFinisherWin()
        {
            AddStat(AchievementStatKeys.DuelPetardFinisher);
        }

        public static void NotifyDoubleFivePlusLinesSameTurn()
        {
            AddStat(AchievementStatKeys.DnnDoubleFivePlusOneTurn);
        }

        public static void NotifyWinAtOneHp()
        {
            AddStat(AchievementStatKeys.DnnWinAtOneHp);
        }

        private static void EvaluateChainsForStat(string statKey)
        {
            foreach (var chain in AchievementCatalog.Chains)
            {
                if (!string.Equals(chain.StatKey, statKey, StringComparison.Ordinal))
                    continue;
                EvaluateChain(chain);
            }
        }

        private static void EvaluateChain(AchievementChainDefinition chain)
        {
            var count = AchievementProgressStorage.GetStat(chain.StatKey);
            for (var i = 0; i < chain.Thresholds.Length; i++)
            {
                if (AchievementProgressStorage.IsStepClaimed(chain.ChainId, i))
                    continue;
                if (count < chain.Thresholds[i])
                    continue;

                AchievementProgressStorage.MarkStepClaimed(chain.ChainId, i);
                var info = new AchievementUnlockInfo
                {
                    ChainId = chain.ChainId,
                    StepIndex = i,
                    Title = chain.Descriptions[i],
                    RewardLine = chain.RewardTexts[i],
                };
                StepCompleted?.Invoke(info);
            }
        }

        /// <summary>Повторная проверка всех цепочек (например после загрузки).</summary>
        public static void ReevaluateAll()
        {
            AchievementProgressStorage.EnsureLoaded();
            foreach (var chain in AchievementCatalog.Chains)
                EvaluateChain(chain);
        }
    }
}
