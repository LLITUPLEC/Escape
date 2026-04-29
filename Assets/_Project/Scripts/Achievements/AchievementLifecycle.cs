using System;

namespace Project.Achievements
{
    /// <summary>События синхронизации UI / тостов: прогресс меняется отдельно от ручного «получить награду».</summary>
    public static class AchievementLifecycle
    {
        public static event Action OnDataChanged;

        /// <summary>Выдана награда шага по кнопке «Получить» (после успешного серверного или локального подтверждения).</summary>
        public static event Action<AchievementUnlockInfo> OnRewardClaimed;

        public static void NotifyDataChanged() => OnDataChanged?.Invoke();

        public static void NotifyRewardClaimed(AchievementUnlockInfo info) => OnRewardClaimed?.Invoke(info);
    }
}
