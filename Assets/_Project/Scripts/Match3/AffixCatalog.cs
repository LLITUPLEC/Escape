using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Match3
{
    [CreateAssetMenu(menuName = "Project/Match3/Affix Catalog", fileName = "AffixCatalog")]
    public sealed class AffixCatalog : ScriptableObject
    {
        [Serializable]
        public sealed class AffixEntry
        {
            public string id;
            public string title;
            [TextArea(2, 6)] public string description;
            public Sprite icon;
        }

        [SerializeField] private List<AffixEntry> affixes = new();
        private Dictionary<string, AffixEntry> _byId;

        private static readonly Dictionary<string, (string title, string description)> Builtins = new()
        {
            ["acid"] = ("Кислотный", "В конце каждого твоего хода теряешь 3% изначального ХП."),
            ["energy_block"] = ("Энергоблок", "Бомбы не наносят урон и дают +5 к урону за каждую уничтоженную бомбу (для игрока и монстра)."),
            ["regeneration"] = ("Регенерация", "Монстр лечит 3% ХП каждый свой ход."),
            ["fragility"] = ("Хрупкость", "У монстра вдвое меньше ХП, но +50 маны и +35% крита."),
            ["stone_skin"] = ("Каменная кожа", "Монстр получает +15 брони каждый третий свой ход."),
            ["mana_vampire"] = ("Мана-вампир", "Каждая уничтоженная капсула даёт противнику 2 маны (для игрока и монстра)."),
            ["frozen"] = ("Ледяной", "Способности стоят на 10 маны дороже."),
            ["monster_rage"] = ("Ярость монстра", "Каждая уничтоженная монстром бомба даёт ему +3 урона до конца боя."),
            ["instability"] = ("Нестабильность", "В начале каждого твоего хода камни на поле перемешиваются."),
            ["overload"] = ("Перегрузка", "Получение маны ограничено 1 за каждый объект, который даёт ману."),
            ["bare_current"] = ("Голый ток", "Получение маны невозможно (включая начальную), но урон наносится вдвойне."),
            ["scree"] = ("Осыпь", "Время хода уменьшено в 3 раза (10 секунд)."),
        };

        private void OnEnable() => Rebuild();

        private void Rebuild()
        {
            _byId = new Dictionary<string, AffixEntry>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < affixes.Count; i++)
            {
                var a = affixes[i];
                if (a == null) continue;
                var key = Normalize(a.id);
                if (key.Length == 0) continue;
                _byId[key] = a;
            }
        }

        public bool TryGet(string affixId, out string title, out string description, out Sprite icon)
        {
            var key = Normalize(affixId);
            if (_byId == null) Rebuild();
            if (key.Length > 0 && _byId != null && _byId.TryGetValue(key, out var entry) && entry != null)
            {
                title = entry.title ?? string.Empty;
                description = entry.description ?? string.Empty;
                icon = entry.icon;
                if (title.Length > 0 || description.Length > 0 || icon != null)
                    return true;
            }

            if (TryGetBuiltin(key, out title, out description))
            {
                icon = null;
                return true;
            }

            title = string.Empty;
            description = string.Empty;
            icon = null;
            return false;
        }

        public static bool TryGetBuiltin(string affixId, out string title, out string description)
        {
            var key = Normalize(affixId);
            if (key.Length > 0 && Builtins.TryGetValue(key, out var data))
            {
                title = data.title;
                description = data.description;
                return true;
            }
            title = string.Empty;
            description = string.Empty;
            return false;
        }

        public static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }
}
