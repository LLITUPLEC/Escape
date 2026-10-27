using System;
using System.Linq;
using Project.Character;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Validates shipped item descriptions against the actual crafting catalog without account or RPC actions.</summary>
    public static class ExpeditionItemInformationQa
    {
        public static void ValidateBatch() => Debug.Log(Run());

        public static string Run()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
            Require(catalog != null, "Main item catalog is missing.");
            var items = catalog.EnumerateDefinitions().ToArray();
            var recipes = 0;
            var equipment = 0;
            foreach (var item in items)
            {
                Require(!string.IsNullOrWhiteSpace(item.DisplayName) && item.DisplayName != item.ItemId && !item.DisplayName.Contains("_"), "Placeholder item name: " + item.ItemId);
                Require(!string.IsNullOrWhiteSpace(item.Description), "Item description is empty: " + item.ItemId);
                Require(item.Icon != null, "Item icon is missing: " + item.ItemId);
                if (item.Kind == ItemKind.Recipe)
                {
                    recipes++;
                    var target = ItemInformation.RecipeResult(item, catalog);
                    Require(target != null && target.Quality == item.Quality && target.Slot == item.RecipeTargetSlot, "Recipe target does not match the crafting catalog: " + item.ItemId);
                    Require(item.DisplayName.Contains(target.DisplayName), "Recipe name differs from its crafted item: " + item.ItemId);
                    var unknown = ItemInformation.Usage(item, catalog, false);
                    var learned = ItemInformation.Usage(item, catalog, true);
                    Require(unknown.Contains("Нажмите «Изучить»") && unknown.Contains(target.DisplayName), "Unlearned recipe lacks learning instructions: " + item.ItemId);
                    Require(learned.Contains("уже изучен") && !learned.Contains("Нажмите «Изучить»"), "Learned recipe still prompts learning: " + item.ItemId);
                    Require(unknown.Contains("руда ×" + target.CraftOre) && unknown.Contains("золото ×" + target.CraftGold), "Recipe costs are stale: " + item.ItemId);
                    if (!string.IsNullOrEmpty(target.CraftItemId)) Require(unknown.Contains(catalog.Get(target.CraftItemId).DisplayName), "Consumed equipment is not identified: " + item.ItemId);
                }
                if (item.Kind == ItemKind.Equipment && (item.Quality == ItemQualityTier.Epic || item.Quality == ItemQualityTier.Legendary))
                {
                    equipment++;
                    Require(item.Icon == Resources.Load<Sprite>("Expedition/Items/" + item.ItemId), "Dedicated equipment icon is not bound: " + item.ItemId);
                }
                if (!item.Equippable && item.Kind != ItemKind.Consumable)
                {
                    var usage = ItemInformation.Usage(item, catalog, false);
                    Require(!string.IsNullOrWhiteSpace(usage) && !usage.Contains("Нет характеристик"), "Non-equipment lacks purpose: " + item.ItemId);
                }
            }
            Require(recipes == 32 && equipment == 16, "Expected all 32 recipes and 16 epic/legendary equipment entries.");
            foreach (var id in new[] { "ingot_green", "ingot_blue", "ingot_purple" })
                Require(ItemInformation.Usage(catalog.Get(id), catalog, false).Contains("Качество изделий:"), "Ingot has no crafting consumers: " + id);
            Require(ItemInformation.Usage(catalog.Get("global_boss_teleport"), catalog, false).Contains("глобального босса") &&
                !ItemInformation.Usage(catalog.Get("global_boss_teleport"), catalog, false).Contains("мастерской"), "Boss pass is incorrectly described as a crafting material.");
            return "PASS item information: " + items.Length + " named/described items with sprites; 32 recipe targets, live costs and learned states; 16 dedicated epic/legendary icons; material, tesseract and boss-pass purposes.";
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
