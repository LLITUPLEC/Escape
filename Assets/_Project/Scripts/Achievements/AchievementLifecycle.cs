using System;
using Project.Character;

namespace Project.Achievements
{
    /// <summary>События синхронизации UI / тостов: прогресс меняется отдельно от ручного «получить награду».</summary>
    public static class AchievementLifecycle
    {
        public static event Action OnDataChanged;

        /// <summary>Выдана награда шага по кнопке «Получить» (после успешного серверного или локального подтверждения).</summary>
        public static event Action<AchievementUnlockInfo> OnRewardClaimed;

        /// <summary>Порог шага впервые достигнут — награда ещё не получена на клиенте (показываем тост во время игры).</summary>
        public static event Action<AchievementUnlockInfo> OnAwaitingClaim;

        /// <summary>Пересчитанные боевые статы персонажа (после claim достижения с сервера).</summary>
        public static event Action<StatsMap> OnCombatStatsUpdated;

        public static void NotifyDataChanged() => OnDataChanged?.Invoke();

        public static void NotifyRewardClaimed(AchievementUnlockInfo info) => OnRewardClaimed?.Invoke(info);

        public static void NotifyAwaitingClaim(AchievementUnlockInfo info) => OnAwaitingClaim?.Invoke(info);

        public static void NotifyCombatStatsUpdated(StatsMap stats)
        {
            if (stats == null) return;
            OnCombatStatsUpdated?.Invoke(stats);
        }
    }
}
