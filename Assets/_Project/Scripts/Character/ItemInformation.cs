using System.Collections.Generic;

namespace Project.Character
{
    /// <summary>Purpose and crafting instructions for inventory items that do not have combat stats.</summary>
    public static class ItemInformation
    {
        public static ItemDefinition RecipeResult(ItemDefinition recipe, ItemCatalog catalog)
        {
            if (recipe == null || recipe.Kind != ItemKind.Recipe || catalog == null) return null;
            foreach (var item in catalog.EnumerateDefinitions())
                if (item.Kind == ItemKind.Equipment && item.CraftRecipeId == recipe.ItemId) return item;
            return null;
        }

        public static string Category(ItemDefinition item)
        {
            if (item == null) return string.Empty;
            if (item.ItemId == "global_boss_teleport") return "Пропуск · глобальный босс";
            switch (item.Kind)
            {
                case ItemKind.Material: return "Материал для мастерской";
                case ItemKind.Tesseract: return "Редкий материал · легендарная ковка";
                case ItemKind.Recipe: return "Рецепт · " + QualityName(item.Quality);
                case ItemKind.Consumable: return "Расходник · используется один предмет";
                default: return "Снаряжение · " + QualityName(item.Quality);
            }
        }

        public static string Usage(ItemDefinition item, ItemCatalog catalog, bool recipeLearned)
        {
            if (item == null || item.Equippable) return string.Empty;
            if (item.ItemId == "global_boss_teleport")
                return "Откройте раздел глобального босса и выберите вход в бой. Один телепорт расходуется при начале попытки.\n\nТелепорт хранится в инвентаре; отдельная активация в разделе героя не требуется.";
            if (item.Kind == ItemKind.Recipe)
            {
                var target = RecipeResult(item, catalog);
                var lines = new List<string>();
                if (target != null) lines.Add("Открывает изготовление: «" + target.DisplayName + "».");
                lines.Add(recipeLearned
                    ? "Рецепт уже изучен. Изготовление доступно в мастерской при наличии материалов."
                    : "Нажмите «Изучить», чтобы навсегда открыть этот чертёж в мастерской. При изучении расходуется один рецепт.");
                if (target != null)
                {
                    var costs = CraftCosts(target, catalog);
                    if (!string.IsNullOrEmpty(costs)) lines.Add("Для изготовления: " + costs + ".");
                    if (target.CraftMinutes > 0) lines.Add("Время изготовления: " + Duration(target.CraftMinutes) + ".");
                }
                return string.Join("\n\n", lines);
            }
            if (item.Kind == ItemKind.Material)
            {
                var uses = new List<string>();
                if (catalog != null)
                    foreach (var target in catalog.EnumerateDefinitions())
                        if (target.Kind == ItemKind.Equipment && target.CraftIngotDef == item.ItemId && target.CraftIngotN > 0)
                        {
                            var quality = QualityName(target.Quality);
                            if (!uses.Contains(quality)) uses.Add(quality);
                        }
                return uses.Count > 0
                    ? "Используется при изготовлении снаряжения в мастерской. Качество изделий: " + string.Join(", ", uses) + ".\n\nНужное количество указано в выбранном чертеже. Материал расходуется при запуске изготовления."
                    : "Материал для изготовления в мастерской. Требуемое количество указано в чертеже; материал расходуется при запуске изготовления.";
            }
            if (item.Kind == ItemKind.Tesseract)
                return "Используется в мастерской при изготовлении легендарного снаряжения.\n\nДля ковки также нужны изученный легендарный рецепт, эпический предмет того же слота, руда и золото. Точное количество ресурсов указано в чертеже.";
            return string.Empty;
        }

        public static string QualityName(ItemQualityTier quality)
        {
            switch (quality)
            {
                case ItemQualityTier.Rare: return "Редкое";
                case ItemQualityTier.Epic: return "Эпическое";
                case ItemQualityTier.Legendary: return "Легендарное";
                default: return "Обычное";
            }
        }

        private static string CraftCosts(ItemDefinition target, ItemCatalog catalog)
        {
            var costs = new List<string>();
            if (target.CraftOre > 0) costs.Add("руда ×" + target.CraftOre);
            if (target.CraftGold > 0) costs.Add("золото ×" + target.CraftGold);
            if (target.CraftIngotN > 0) costs.Add(Name(catalog, target.CraftIngotDef, "слиток") + " ×" + target.CraftIngotN);
            if (target.CraftTesseractN > 0) costs.Add("тессеракт ×" + target.CraftTesseractN);
            if (!string.IsNullOrEmpty(target.CraftItemId)) costs.Add("«" + Name(catalog, target.CraftItemId, "предмет предыдущего качества") + "» (будет поглощён)");
            return string.Join(", ", costs);
        }

        private static string Name(ItemCatalog catalog, string id, string fallback) => catalog != null && catalog.Get(id) != null ? catalog.Get(id).DisplayName : fallback;

        private static string Duration(int minutes)
        {
            var hours = minutes / 60;
            var rest = minutes % 60;
            return hours > 0 ? hours + " ч" + (rest > 0 ? " " + rest + " мин" : string.Empty) : minutes + " мин";
        }
    }
}
