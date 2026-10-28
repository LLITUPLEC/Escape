using UnityEngine;

namespace Project.Character
{
    /// <summary>Presentation of the server's eight-slot, same-tier/quality equipment set rule.</summary>
    public static class EquipmentSetBonus
    {
        public const int Required = 8;
        public const float Multiplier = 1.25f;

        public readonly struct State
        {
            public readonly int Count, Tier;
            public readonly ItemQualityTier Quality;
            public bool Active => Count == Required;
            public string Key => Active ? Tier + ":" + (int)Quality : string.Empty;
            public State(int count, int tier, ItemQualityTier quality) { Count = count; Tier = tier; Quality = quality; }
        }

        public static State Evaluate(string[] equipment, ItemCatalog catalog, ItemDefinition candidate = null)
        {
            if (catalog == null) return default;
            var best = candidate != null ? Count(equipment, catalog, candidate.Tier, candidate.Quality) : default;
            if (candidate != null) return best;
            for (var i = 0; equipment != null && i < equipment.Length && i < Required; i++)
            {
                var item = catalog.Get(equipment[i]);
                if (!ValidSlot(item, i)) continue;
                var state = Count(equipment, catalog, item.Tier, item.Quality);
                if (state.Count > best.Count) best = state;
            }
            return best;
        }

        private static State Count(string[] equipment, ItemCatalog catalog, int tier, ItemQualityTier quality)
        {
            var count = 0;
            for (var i = 0; equipment != null && i < equipment.Length && i < Required; i++)
            {
                var item = catalog.Get(equipment[i]);
                if (ValidSlot(item, i) && item.Tier == tier && item.Quality == quality) count++;
            }
            return new State(count, tier, quality);
        }

        private static bool ValidSlot(ItemDefinition item, int slot) => item != null && item.Equippable &&
            item.Kind == ItemKind.Equipment && (int)item.Slot == slot;

        public static string CardText(ItemDefinition item, string[] equipment, ItemCatalog catalog)
        {
            if (item == null || !item.Equippable || item.Kind != ItemKind.Equipment) return string.Empty;
            var set = Evaluate(equipment, catalog, item);
            var color = ColorUtility.ToHtmlStringRGB(set.Active ? new Color32(142, 222, 174, 255) : new Color32(218, 190, 123, 255));
            return "<size=78%><color=#" + color + ">Комплект T" + item.Tier + " · " + QualityName(item.Quality) + " · " + set.Count + "/8" +
                (set.Active ? " — активен" : "") + "</color>\n8/8: +25% к здоровью, урону, броне и лечению от вещей. Крит без бонуса.</size>";
        }

        public static string QualityName(ItemQualityTier quality) => quality switch
        {
            ItemQualityTier.Rare => "редкий", ItemQualityTier.Epic => "эпический",
            ItemQualityTier.Legendary => "легендарный", _ => "обычный"
        };
    }
}
