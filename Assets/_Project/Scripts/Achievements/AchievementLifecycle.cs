using System;

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

        public static void NotifyDataChanged() => OnDataChanged?.Invoke();

        public static void NotifyRewardClaimed(AchievementUnlockInfo info) => OnRewardClaimed?.Invoke(info);

        public static void NotifyAwaitingClaim(AchievementUnlockInfo info) => OnAwaitingClaim?.Invoke(info);
    }
}
