using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Project.Character.Editor
{
    /// <summary>
    /// Назначает спрайты из Assets/_Project/img/items сериализуемому полю icon у ItemDefinition
    /// и placeholder в MainItemCatalog (для предметов без отдельного арта).
    /// </summary>
    public static class ItemIconsAssignFromItemsFolder
    {
        private const string MenuPath = "Tools/Character/Назначить иконки из img/items (зелёные + рецепты)";
        private const string ItemsDir = "Assets/_Project/img/items";
        private const string StubRelative = ItemsDir + "/Chest_green.png";
        private const string MainCatalogPath = "Assets/_Project/Data/Character/Items/MainItemCatalog.asset";

        private static readonly (string itemId, string file)[] GreenEquip =
        {
            ("eq_t1_normal_helmet", "Helmet_green.png"),
            ("eq_t1_normal_shoulders", "Shoulders_green.png"),
            ("eq_t1_normal_chest", "Chest_green.png"),
            ("eq_t1_normal_gloves", "Gloves_green.png"),
            ("eq_t1_normal_legs", "Legs_green.png"),
            ("eq_t1_normal_feet", "Feet_green.png"),
            ("eq_t1_normal_weapon_l", "WeaponLeft_green.png"),
            ("eq_t1_normal_weapon_r", "WeaponRight_green.png"),
        };

        private static readonly (string itemId, string file)[] Recipes =
        {
            ("recipe_green", "blprint_green.png"),
            ("recipe_blue", "blprint_blue.png"),
            ("recipe_purple", "blprint_purple.png"),
            ("recipe_gold", "blueprint_gold.png"),
        };

        [MenuItem(MenuPath)]
        public static void Assign()
        {
            var stub = LoadSprite(StubRelative);
            if (stub == null)
            {
                EditorUtility.DisplayDialog("Иконки предметов", "Не найден заглушечный спрайт:\n" + StubRelative, "OK");
                return;
            }

            var map = new Dictionary<string, Sprite>();
            void AddMap((string itemId, string file)[] rows)
            {
                foreach (var (itemId, file) in rows)
                {
                    var path = ItemsDir + "/" + file;
                    var sp = LoadSprite(path);
                    if (sp == null)
                    {
                        Debug.LogWarning("[ItemIcons] Нет файла: " + path);
                        continue;
                    }

                    map[itemId] = sp;
                }
            }

            AddMap(GreenEquip);
            AddMap(Recipes);

            var guids = AssetDatabase.FindAssets("t:ItemDefinition");
            var touched = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def == null || string.IsNullOrEmpty(def.ItemId)) continue;

                if (map.TryGetValue(def.ItemId, out var sprite))
                {
                    AssignIcon(def, sprite);
                    touched++;
                    continue;
                }

                AssignIcon(def, stub);
                touched++;
            }

            AssignCatalogPlaceholder(stub);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Иконки предметов",
                "Готово. Уникальные спрайты: зелёная экипировка T1 и 4 рецепта; остальные ItemDefinition получили заглушку Chest_green.\n" +
                "Проверьте поле missingItemIcon в MainItemCatalog (назначено на заглушку).",
                "OK");
            Debug.Log("[ItemIcons] Обновлено определений: " + touched);
        }

        private static Sprite LoadSprite(string assetPath)
        {
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void AssignIcon(ItemDefinition def, Sprite sprite)
        {
            var so = new SerializedObject(def);
            var icon = so.FindProperty("icon");
            if (icon == null || icon.objectReferenceValue == sprite)
                return;

            icon.objectReferenceValue = sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(def);
        }

        private static void AssignCatalogPlaceholder(Sprite stub)
        {
            var cat = AssetDatabase.LoadAssetAtPath<ItemCatalog>(MainCatalogPath);
            if (cat == null) return;
            var so = new SerializedObject(cat);
            var p = so.FindProperty("missingItemIcon");
            if (p != null && p.objectReferenceValue != stub)
            {
                p.objectReferenceValue = stub;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(cat);
            }
        }
    }
}
