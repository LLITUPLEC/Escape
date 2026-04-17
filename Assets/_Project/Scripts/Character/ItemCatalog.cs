using System.Collections.Generic;
using UnityEngine;

namespace Project.Character
{
    [CreateAssetMenu(menuName = "Project/Character/Item Catalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
        [Header("Fallback")]
        [Tooltip("Если у ItemDefinition нет своей иконки (синие/фиолет/легенда до отдельных артов).")]
        [SerializeField] private Sprite missingItemIcon;

        [SerializeField] private List<ItemDefinition> items = new();

        private Dictionary<string, ItemDefinition> _byId;

        private void OnEnable()
        {
            RebuildMap();
        }

        private void RebuildMap()
        {
            _byId = new Dictionary<string, ItemDefinition>();
            foreach (var def in items)
            {
                if (def == null) continue;
                var id = def.ItemId;
                if (string.IsNullOrEmpty(id)) continue;
                _byId[id] = def;
            }
        }

        public ItemDefinition Get(string itemId)
        {
            if (string.IsNullOrEmpty(itemId)) return null;
            if (_byId == null) RebuildMap();
            return _byId != null && _byId.TryGetValue(itemId, out var d) ? d : null;
        }

        /// <summary>Перебор для UI мастерской / фильтров без дублирования списков.</summary>
        public IEnumerable<ItemDefinition> EnumerateDefinitions()
        {
            if (items == null) yield break;
            for (var i = 0; i < items.Count; i++)
            {
                if (items[i] != null) yield return items[i];
            }
        }

        public Sprite GetDisplayIcon(ItemDefinition def)
        {
            if (def == null) return null;
            if (def.Icon != null) return def.Icon;
            return missingItemIcon;
        }
    }
}
