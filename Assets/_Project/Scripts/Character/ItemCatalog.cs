using System.Collections.Generic;
using UnityEngine;

namespace Project.Character
{
    [CreateAssetMenu(menuName = "Project/Character/Item Catalog", fileName = "ItemCatalog")]
    public sealed class ItemCatalog : ScriptableObject
    {
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
    }
}
