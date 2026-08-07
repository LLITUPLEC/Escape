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

        /// <summary>
        /// Полный сброс локального прогресса/полученных шагов (wipe аккаунта).
        /// Каталог определений на сервере/кэш не трогаем.
        /// </summary>
        public static void ClearAllLocalProgress()
        {
            _stats = new Dictionary<string, int>(StringComparer.Ordinal);
            _claimedStepTokens = new HashSet<string>(StringComparer.Ordinal);
            PlayerPrefs.DeleteKey(PrefStatsJson);
            PlayerPrefs.DeleteKey(PrefClaimedJson);
            PlayerPrefs.Save();
            AchievementUiPending.ClearAllAwaitToastTokens();
            AchievementLifecycle.NotifyDataChanged();
        }

        /// <summary>
        /// Слияние с данными Nakama (achievement_sync): сервер авторитетен.
        /// Если массив claimed/stats пришёл (в т.ч. пустой) — локальные значения заменяются, а не объединяются.
        /// </summary>
        public static void MergeFromAuthoritativeSnapshot(
            IList<StringIntPair> serverStatsEntries,
            IList<string> serverClaimedTokens)
        {
            EnsureLoaded();
            var eligibleBeforeMerge = AchievementUiPending.CaptureEligibleClaimStepTokens();

            var changedClaims = false;
            var changedStats = false;
            if (serverClaimedTokens != null)
            {
                var nextClaims = new HashSet<string>(StringComparer.Ordinal);
                foreach (var tok in serverClaimedTokens)
                {
                    if (string.IsNullOrEmpty(tok)) continue;
                    nextClaims.Add(tok);
                }

                if (nextClaims.Count != _claimedStepTokens.Count || !nextClaims.SetEquals(_claimedStepTokens))
                {
                    _claimedStepTokens = nextClaims;
                    SaveSet(PrefClaimedJson, _claimedStepTokens);
                    changedClaims = true;
                }
            }

            if (serverStatsEntries != null)
            {
                var nextStats = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var e in serverStatsEntries)
                {
                    if (e == null || string.IsNullOrEmpty(e.k)) continue;
                    nextStats[e.k] = Mathf.Max(0, e.v);
                }

                if (!StatsEqual(_stats, nextStats))
                {
                    _stats = nextStats;
                    SaveDict(PrefStatsJson, _stats);
                    changedStats = true;
                }
            }

            var newAwaitClaims = AchievementUiPending.RaiseNewEligibleSince(eligibleBeforeMerge);
            if (changedClaims || changedStats || newAwaitClaims > 0)
                AchievementLifecycle.NotifyDataChanged();
        }

        private static bool StatsEqual(Dictionary<string, int> a, Dictionary<string, int> b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            if (a.Count != b.Count) return false;
            foreach (var kv in a)
            {
                if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value)
                    return false;
            }
            return true;
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
        public sealed class StringIntPair
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

    /// <summary>Статический вход для игровых систем: счётчики событий. Награды шагов выдаются только через <see cref="AchievementRewardClaim"/>.</summary>
    public static class AchievementTracker
    {
        private static int _localFivePlusStreak;

        public static void AddStat(string key, int delta = 1)
        {
            if (delta == 0 || string.IsNullOrEmpty(key))
                return;
            AchievementProgressStorage.EnsureLoaded();
            var eligibleBeforeStat = AchievementUiPending.CaptureEligibleClaimStepTokens();
            AchievementProgressStorage.AddStat(key, delta);
            AchievementUiPending.RaiseNewEligibleSince(eligibleBeforeStat);
            AchievementLifecycle.NotifyDataChanged();
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

        public static void NotifyOreTournamentFinalWin()
        {
            AddStat(AchievementStatKeys.TournamentOreWinFinal);
        }

        public static void NotifyGoldTournamentFinalWin()
        {
            AddStat(AchievementStatKeys.TournamentGoldWinFinal);
        }

        public static void NotifyTriMatchDuelWin()
        {
            AddStat(AchievementStatKeys.DuelTriWin);
        }

        public static void NotifyAbilityFinisherWin(int actionType)
        {
            // actionType: 2=крест, 3=квадрат, 4=петарда (как на сервере).
            switch (actionType)
            {
                case 2:
                    AddStat(AchievementStatKeys.DuelCrossFinisher);
                    break;
                case 3:
                    AddStat(AchievementStatKeys.DuelSquareFinisher);
                    break;
                case 4:
                    AddStat(AchievementStatKeys.DuelPetardFinisher);
                    break;
            }
        }

        public static void NotifyDoubleFivePlusLinesSameTurn()
        {
            AddStat(AchievementStatKeys.DnnDoubleFivePlusOneTurn);
        }

        public static void NotifyLineMatches(int line5Count, int line6Count)
        {
            if (line5Count > 0)
                AddStat(AchievementStatKeys.Line5, line5Count);
            if (line6Count > 0)
                AddStat(AchievementStatKeys.Line6, line6Count);
        }

        /// <summary>
        /// Локальный офлайн-трек серии подряд своих ходов (без передачи сопернику).
        /// При 3 подряд — +1 к <see cref="AchievementStatKeys.DnnThreeFivePlusStreak"/>.
        /// Сервер авторитетен в сетевом матче; сброс — <see cref="ClearFivePlusStreak"/>.
        /// </summary>
        public static void NotifyConsecutiveOwnTurn()
        {
            _localFivePlusStreak++;
            if (_localFivePlusStreak < 3)
                return;

            AddStat(AchievementStatKeys.DnnThreeFivePlusStreak);
            _localFivePlusStreak = 0;
        }

        public static void ClearFivePlusStreak()
        {
            _localFivePlusStreak = 0;
        }

        public static void NotifyWinAtOneHp()
        {
            AddStat(AchievementStatKeys.DnnWinAtOneHp);
        }

        /// <summary>Синхронизация счётчиков с сервером / перерисовка грида — без автоматической выдачи наград.</summary>
        public static void ReevaluateAll()
        {
            AchievementProgressStorage.EnsureLoaded();
            AchievementLifecycle.NotifyDataChanged();
        }
    }
}
