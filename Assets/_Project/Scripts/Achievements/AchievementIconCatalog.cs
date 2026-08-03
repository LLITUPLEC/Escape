using System.Collections.Generic;
using UnityEngine;

namespace Project.Achievements
{
    /// <summary>Каталог иконок достижений: одна иконка на chain id, общая для всех шагов.</summary>
    [CreateAssetMenu(menuName = "Project/Achievements/Achievement Icon Catalog", fileName = "AchievementIconCatalog")]
    public sealed class AchievementIconCatalog : ScriptableObject
    {
        public const string MainCatalogAssetPath = "Assets/_Project/Data/Achievements/MainAchievementIconCatalog.asset";

        [Header("Fallback")]
        [Tooltip("Если у AchievementDefinition нет своей иконки.")]
        [SerializeField] private Sprite missingIcon;

        [SerializeField] private List<AchievementDefinition> achievements = new();

        private Dictionary<string, AchievementDefinition> _byId;

        private void OnEnable()
        {
            RebuildMap();
        }

        private void RebuildMap()
        {
            _byId = new Dictionary<string, AchievementDefinition>();
            if (achievements == null) return;
            foreach (var def in achievements)
            {
                if (def == null) continue;
                var id = def.AchievementId;
                if (string.IsNullOrEmpty(id)) continue;
                _byId[id] = def;
            }
        }

        public AchievementDefinition Get(string achievementId)
        {
            if (string.IsNullOrEmpty(achievementId)) return null;
            if (_byId == null) RebuildMap();
            return _byId != null && _byId.TryGetValue(achievementId, out var d) ? d : null;
        }

        public Sprite GetDisplayIcon(string achievementId)
        {
            var def = Get(achievementId);
            if (def != null && def.Icon != null)
                return def.Icon;
            return missingIcon;
        }

        public Sprite GetDisplayIcon(AchievementDefinition def)
        {
            if (def == null) return missingIcon;
            if (def.Icon != null) return def.Icon;
            return missingIcon;
        }
    }
}
