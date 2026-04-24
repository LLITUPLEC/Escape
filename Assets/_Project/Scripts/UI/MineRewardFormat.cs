using System;
using Project.Character;
using UnityEngine;

namespace Project.UI
{
    /// <summary>Короткие подписи наград шахты + иконки из <see cref="ItemCatalog"/> (как в мастерской/сундуке).</summary>
    public static class MineRewardFormat
    {
        public const string MainItemCatalogAssetPath = "Assets/_Project/Data/Character/Items/MainItemCatalog.asset";

        public static string IngotDefIdForDifficulty(string difficulty)
        {
            if (string.Equals(difficulty, "medium", StringComparison.OrdinalIgnoreCase))
                return "ingot_blue";
            if (string.Equals(difficulty, "hard", StringComparison.OrdinalIgnoreCase))
                return "ingot_purple";
            return "ingot_green";
        }

        public static string RecipeSlotNameRuFromRecipeItemId(string recipeItemId)
        {
            if (string.IsNullOrEmpty(recipeItemId))
                return string.Empty;
            var slotEn = GetTrailingSegmentAfterLastUnderscore(recipeItemId);
            return RecipeSlotNameRuFromEnglishSlot(slotEn);
        }

        private static string GetTrailingSegmentAfterLastUnderscore(string id)
        {
            var last = id.LastIndexOf('_');
            if (last < 0 || last >= id.Length - 1)
                return string.Empty;
            return id.Substring(last + 1);
        }

        public static string RecipeSlotNameRuFromEnglishSlot(string slot)
        {
            if (string.IsNullOrEmpty(slot))
                return string.Empty;
            switch (slot)
            {
                case "Helmet": return "Шлема";
                case "Shoulders": return "Плечей";
                case "Chest": return "Кирасы";
                case "Gloves": return "Перчаток";
                case "Legs": return "Поножей";
                case "Feet": return "Сапог";
                case "WeaponLeft": return "Оруж. (л.)";
                case "WeaponRight": return "Оруж. (пр.)";
                default: return string.Empty;
            }
        }

        public static string LegacyBlueprintShortLabel(string rewardBlueprint)
        {
            if (string.IsNullOrWhiteSpace(rewardBlueprint))
                return "Рецепт";
            switch (rewardBlueprint.Trim().ToLowerInvariant())
            {
                case "green": return "Рецепт (зел.)";
                case "blue": return "Рецепт (син.)";
                case "purple": return "Рецепт (фиол.)";
                case "gold": return "Рецепт (золот.)";
                default: return "Рецепт";
            }
        }

        public static Sprite IngotIconForDifficulty(string difficulty, ItemCatalog catalog, Sprite fallback)
        {
            if (catalog == null)
                return fallback;
            var def = catalog.Get(IngotDefIdForDifficulty(difficulty));
            if (def == null)
                return fallback;
            var s = catalog.GetDisplayIcon(def);
            return s != null ? s : fallback;
        }

        public static Sprite ItemIconOrFallback(ItemCatalog catalog, string itemId, Sprite fallback)
        {
            if (catalog == null || string.IsNullOrEmpty(itemId))
                return fallback;
            var def = catalog.Get(itemId);
            if (def == null)
                return fallback;
            var s = catalog.GetDisplayIcon(def);
            return s != null ? s : fallback;
        }
    }
}
