using UnityEngine;

namespace Project.Achievements
{
    /// <summary>
    /// Локальный визуальный деф достижения (иконка и подпись).
    /// Идентификатор = id цепочки из Storage duel_match3_achievement_defs (не step).
    /// </summary>
    [CreateAssetMenu(menuName = "Project/Achievements/Achievement Definition", fileName = "AchievementDefinition")]
    public sealed class AchievementDefinition : ScriptableObject
    {
        [Header("Identity (сервер: chains[].id)")]
        [SerializeField] private string achievementId = "obs.cross";

        [Header("Display")]
        [SerializeField] private string titleRu = "";
        [SerializeField] private string category = "obsession";

        [Header("Visual")]
        [SerializeField] private Sprite icon;

        public string AchievementId => achievementId;
        public string TitleRu => titleRu;
        public string Category => category;
        public Sprite Icon => icon;
    }
}
