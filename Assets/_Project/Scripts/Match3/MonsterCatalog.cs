using System.Collections.Generic;
using UnityEngine;

namespace Project.Match3
{
    [CreateAssetMenu(menuName = "Project/Match3/Monster Catalog", fileName = "MonsterCatalog")]
    public sealed class MonsterCatalog : ScriptableObject
    {
        [SerializeField] private List<MonsterDefinition> monsters = new();

        private Dictionary<string, MonsterDefinition> _byBotId;
        private Dictionary<int, MonsterDefinition> _byFloor;

        private void OnEnable()
        {
            Rebuild();
        }

        private void Rebuild()
        {
            _byBotId = new Dictionary<string, MonsterDefinition>();
            _byFloor = new Dictionary<int, MonsterDefinition>();
            foreach (var m in monsters)
            {
                if (m == null) continue;
                if (!string.IsNullOrWhiteSpace(m.BotId))
                    _byBotId[m.BotId] = m;
                if (m.Floor > 0)
                    _byFloor[m.Floor] = m;
            }
        }

        public MonsterDefinition GetByBotId(string botId)
        {
            if (string.IsNullOrWhiteSpace(botId)) return null;
            if (_byBotId == null) Rebuild();
            return _byBotId != null && _byBotId.TryGetValue(botId, out var m) ? m : null;
        }

        public MonsterDefinition GetByFloor(int floor)
        {
            if (floor <= 0) return null;
            if (_byFloor == null) Rebuild();
            return _byFloor != null && _byFloor.TryGetValue(floor, out var m) ? m : null;
        }
    }
}
