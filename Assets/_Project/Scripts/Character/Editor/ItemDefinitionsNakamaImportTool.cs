using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Project.Character.Editor
{
    public static class ItemDefinitionsNakamaImportTool
    {
        private const string MenuPath = "Tools/Character/Импортировать статы ItemDefinition из duel_match3_item_defs";
        private const string DefaultJsonRelative = "Server/nakama/data/duel_match3_item_catalog.example.json";
        private const string ItemDefsRoot = "Assets/_Project/Data/Character/Items";
        private const string MainCatalogPath = ItemDefsRoot + "/MainItemCatalog.asset";

        [MenuItem(MenuPath)]
        public static void ImportFromJson()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var defaultPath = Path.Combine(projectRoot, DefaultJsonRelative);
            var startDir = File.Exists(defaultPath) ? Path.GetDirectoryName(defaultPath) : projectRoot;
            var selectedPath = EditorUtility.OpenFilePanel("Выберите JSON каталога duel_match3_item_defs", startDir, "json");
            if (string.IsNullOrEmpty(selectedPath)) return;

            string json;
            try
            {
                json = File.ReadAllText(selectedPath);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Импорт ItemDefinition", "Не удалось прочитать JSON:\n" + e.Message, "OK");
                return;
            }

            if (!TryParseServerItems(json, out var serverItems, out var parseError))
            {
                EditorUtility.DisplayDialog("Импорт ItemDefinition", "Ошибка парсинга JSON:\n" + parseError, "OK");
                return;
            }

            var missingMode = EditorUtility.DisplayDialogComplex(
                "Импорт ItemDefinition",
                "Создавать ItemDefinition для itemId, которых нет в проекте?",
                "Да, создать",
                "Только обновить существующие",
                "Отмена");
            if (missingMode == 2) return;
            var createMissing = missingMode == 0;

            var result = ApplyToItemDefinitions(serverItems, createMissing);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Импорт ItemDefinition",
                $"Готово.\n\nВсего в JSON: {serverItems.Count}\nНайдено ItemDefinition по id: {result.matched}\nОбновлено: {result.changed}\nСоздано: {result.created}\nПропущено (без изменений): {result.unchanged}\nНе найдено в проектах: {result.notFoundInProject.Count}",
                "OK");

            if (result.notFoundInProject.Count > 0)
            {
                Debug.Log("[Item Import] В JSON есть itemId без ItemDefinition:\n- " + string.Join("\n- ", result.notFoundInProject));
            }
        }

        private static (int matched, int changed, int created, int unchanged, List<string> notFoundInProject) ApplyToItemDefinitions(
            Dictionary<string, ServerItemRecord> serverItems, bool createMissing)
        {
            var unmatched = new HashSet<string>(serverItems.Keys, StringComparer.Ordinal);
            var guids = AssetDatabase.FindAssets("t:ItemDefinition");
            var existingDefsById = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);

            var matched = 0;
            var changed = 0;
            var created = 0;
            var unchanged = 0;
            var createdDefs = new List<ItemDefinition>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(path);
                if (def == null) continue;
                if (string.IsNullOrEmpty(def.ItemId)) continue;
                existingDefsById[def.ItemId] = def;
                if (!serverItems.TryGetValue(def.ItemId, out var rec)) continue;

                unmatched.Remove(def.ItemId);
                matched++;

                var localChanged = ApplyRecordToDefinition(def, rec);

                if (localChanged)
                {
                    changed++;
                }
                else
                {
                    unchanged++;
                }
            }

            if (createMissing && unmatched.Count > 0)
            {
                EnsureFolder(ItemDefsRoot);
                // HashSet нельзя менять во время foreach — копируем список id.
                var toCreate = new List<string>(unmatched);
                foreach (var id in toCreate)
                {
                    if (!unmatched.Contains(id)) continue;
                    if (!serverItems.TryGetValue(id, out var rec)) continue;
                    if (existingDefsById.ContainsKey(id)) continue;

                    var def = ScriptableObject.CreateInstance<ItemDefinition>();
                    var filename = MakeSafeAssetName(id) + ".asset";
                    var assetPath = AssetDatabase.GenerateUniqueAssetPath(ItemDefsRoot + "/" + filename);
                    AssetDatabase.CreateAsset(def, assetPath);

                    ApplyRecordToDefinition(def, rec);
                    createdDefs.Add(def);
                    existingDefsById[id] = def;
                    unmatched.Remove(id);
                    created++;
                }

                if (createdDefs.Count > 0) TryAppendToMainCatalog(createdDefs);
            }

            return (matched, changed, created, unchanged, new List<string>(unmatched));
        }

        private static bool ApplyRecordToDefinition(ItemDefinition def, ServerItemRecord rec)
        {
            if (def == null) return false;
            var so = new SerializedObject(def);
            var localChanged = false;

            localChanged |= SetString(so.FindProperty("itemId"), rec.id);
            localChanged |= SetNumber(so.FindProperty("hp"), rec.hp);
            localChanged |= SetNumber(so.FindProperty("damage"), rec.damage);
            localChanged |= SetNumber(so.FindProperty("armor"), rec.armor);
            localChanged |= SetNumber(so.FindProperty("healing"), rec.healing);
            localChanged |= SetNumber(so.FindProperty("critChance"), NormalizeCritChance(rec.critChance));

            var kindStr = string.IsNullOrWhiteSpace(rec.kind) ? "equipment" : rec.kind.Trim();
            var isEquipment = kindStr.Equals("equipment", StringComparison.OrdinalIgnoreCase);

            var kindProp = so.FindProperty("kind");
            if (kindProp != null && kindProp.propertyType == SerializedPropertyType.Enum)
            {
                var kIdx = KindStringToEnumIndex(kindStr);
                if (kIdx >= 0 && kindProp.enumValueIndex != kIdx)
                {
                    kindProp.enumValueIndex = kIdx;
                    localChanged = true;
                }
            }

            var tier = rec.tier > 0 ? rec.tier : 1;
            localChanged |= SetInt(so.FindProperty("tier"), tier);

            var maxStack = rec.maxStack;
            if (maxStack <= 0)
            {
                if (kindStr.Equals("material", StringComparison.OrdinalIgnoreCase)) maxStack = 100;
                else maxStack = 1;
            }

            localChanged |= SetInt(so.FindProperty("maxStack"), maxStack);

            var qProp = so.FindProperty("quality");
            if (qProp != null && qProp.propertyType == SerializedPropertyType.Enum)
            {
                var qIdx = QualityStringToEnumIndex(string.IsNullOrWhiteSpace(rec.quality) ? "normal" : rec.quality);
                if (qIdx >= 0 && qProp.enumValueIndex != qIdx)
                {
                    qProp.enumValueIndex = qIdx;
                    localChanged = true;
                }
            }

            if (isEquipment && TryParseSlot(rec.slot, out var slot))
            {
                localChanged |= SetInt(so.FindProperty("slot"), (int)slot);
                localChanged |= SetBool(so.FindProperty("equippable"), true);
            }
            else
            {
                localChanged |= SetBool(so.FindProperty("equippable"), false);
                if (TryParseSlot(rec.recipeSlot, out var rs))
                    localChanged |= SetInt(so.FindProperty("recipeTargetSlot"), (int)rs);
            }

            localChanged |= SetString(so.FindProperty("craftRecipeId"), rec.craftRecipeId ?? "");
            localChanged |= SetInt(so.FindProperty("salePrice"), rec.salePrice > 0 ? rec.salePrice : 100);
            if (isEquipment)
            {
                localChanged |= SetInt(so.FindProperty("craftOre"), rec.craftOre);
                localChanged |= SetInt(so.FindProperty("craftGold"), rec.craftGold);
                localChanged |= SetString(so.FindProperty("craftIngotDef"), rec.craftIngotDef ?? "");
                localChanged |= SetInt(so.FindProperty("craftIngotN"), rec.craftIngotN);
                localChanged |= SetInt(so.FindProperty("craftTesseractN"), rec.craftTesseractN);
                localChanged |= SetString(so.FindProperty("craftItemId"), rec.craftItemId ?? "");
                localChanged |= SetInt(so.FindProperty("craftMinutes"), rec.craftMinutes);
            }

            var displayName = so.FindProperty("displayName");
            if (displayName != null && string.IsNullOrWhiteSpace(displayName.stringValue))
                localChanged |= SetString(displayName, rec.id);

            var modifiers = so.FindProperty("modifiers");
            if (modifiers != null && modifiers.isArray && modifiers.arraySize != 0)
            {
                modifiers.arraySize = 0;
                localChanged = true;
            }

            if (localChanged)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
            }

            return localChanged;
        }

        private static int KindStringToEnumIndex(string kind)
        {
            switch (kind.ToLowerInvariant())
            {
                case "equipment": return 0;
                case "material": return 1;
                case "recipe": return 2;
                case "tesseract": return 3;
                default: return 0;
            }
        }

        private static int QualityStringToEnumIndex(string quality)
        {
            switch (quality.ToLowerInvariant())
            {
                case "normal": return 0;
                case "rare": return 1;
                case "epic": return 2;
                case "legendary": return 3;
                default: return 0;
            }
        }

        private static float NormalizeCritChance(float rawCritChance)
        {
            // В storage иногда присылают 40 вместо 0.4.
            if (rawCritChance > 1f) return rawCritChance / 100f;
            return rawCritChance;
        }

        private static bool TryParseSlot(string value, out EquipmentSlotId slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(value)) return false;
            return Enum.TryParse(value, true, out slot);
        }

        private static bool SetString(SerializedProperty p, string value)
        {
            if (p == null) return false;
            if (p.stringValue == value) return false;
            p.stringValue = value;
            return true;
        }

        private static bool SetNumber(SerializedProperty p, float value)
        {
            if (p == null) return false;

            switch (p.propertyType)
            {
                case SerializedPropertyType.Float:
                {
                    if (Mathf.Abs(p.floatValue - value) < 0.0001f) return false;
                    p.floatValue = value;
                    return true;
                }
                case SerializedPropertyType.Integer:
                {
                    var intValue = Mathf.RoundToInt(value);
                    if (p.intValue == intValue) return false;
                    p.intValue = intValue;
                    return true;
                }
                default:
                    Debug.LogWarning("[Item Import] Поле " + p.propertyPath + " не int/float, пропускаю.");
                    return false;
            }
        }

        private static bool SetInt(SerializedProperty p, int value)
        {
            if (p == null) return false;
            if (p.intValue == value) return false;
            p.intValue = value;
            return true;
        }

        private static bool SetBool(SerializedProperty p, bool value)
        {
            if (p == null) return false;
            if (p.boolValue == value) return false;
            p.boolValue = value;
            return true;
        }

        private static void TryAppendToMainCatalog(List<ItemDefinition> newDefs)
        {
            if (newDefs == null || newDefs.Count == 0) return;
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(MainCatalogPath);
            if (catalog == null) return;

            var so = new SerializedObject(catalog);
            var items = so.FindProperty("items");
            if (items == null || !items.isArray) return;

            var existing = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < items.arraySize; i++)
            {
                var entry = items.GetArrayElementAtIndex(i).objectReferenceValue as ItemDefinition;
                if (entry == null || string.IsNullOrEmpty(entry.ItemId)) continue;
                existing.Add(entry.ItemId);
            }

            foreach (var def in newDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.ItemId)) continue;
                if (existing.Contains(def.ItemId)) continue;
                var idx = items.arraySize;
                items.InsertArrayElementAtIndex(idx);
                items.GetArrayElementAtIndex(idx).objectReferenceValue = def;
                existing.Add(def.ItemId);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static bool TryParseServerItems(string json, out Dictionary<string, ServerItemRecord> items, out string error)
        {
            items = new Dictionary<string, ServerItemRecord>(StringComparer.Ordinal);
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON пустой.";
                return false;
            }

            var itemsKeyPos = json.IndexOf("\"items\"", StringComparison.OrdinalIgnoreCase);
            if (itemsKeyPos < 0)
            {
                error = "Не найден ключ \"items\".";
                return false;
            }

            var objStart = json.IndexOf('{', itemsKeyPos);
            if (objStart < 0)
            {
                error = "Не найдено начало объекта items.";
                return false;
            }

            if (!TryExtractBracedObject(json, objStart, out var itemsObject, out _))
            {
                error = "Не удалось вычитать объект items (скобки).";
                return false;
            }

            var cursor = 1; // после '{'
            while (cursor < itemsObject.Length - 1)
            {
                SkipWhitespaceAndCommas(itemsObject, ref cursor);
                if (cursor >= itemsObject.Length - 1) break;

                if (!TryReadJsonString(itemsObject, ref cursor, out var id))
                {
                    error = "Ошибка чтения item id в items.";
                    return false;
                }

                SkipWhitespace(itemsObject, ref cursor);
                if (cursor >= itemsObject.Length || itemsObject[cursor] != ':')
                {
                    error = $"После item id \"{id}\" ожидался символ ':'.";
                    return false;
                }
                cursor++;
                SkipWhitespace(itemsObject, ref cursor);
                if (cursor >= itemsObject.Length || itemsObject[cursor] != '{')
                {
                    error = $"После item id \"{id}\" ожидался объект.";
                    return false;
                }

                if (!TryExtractBracedObject(itemsObject, cursor, out var itemObj, out var itemObjEnd))
                {
                    error = $"Не удалось прочитать объект item \"{id}\".";
                    return false;
                }
                cursor = itemObjEnd + 1;

                var rec = new ServerItemRecord
                {
                    id = id,
                    kind = GetStringField(itemObj, "kind"),
                    quality = GetStringField(itemObj, "quality"),
                    recipeSlot = GetStringField(itemObj, "recipe_slot"),
                    craftRecipeId = GetStringField(itemObj, "craft_recipe_id"),
                    slot = GetStringField(itemObj, "slot"),
                    maxStack = Mathf.RoundToInt(GetNumberField(itemObj, "max_stack")),
                    tier = Mathf.RoundToInt(GetNumberField(itemObj, "tier")),
                    hp = GetNumberField(itemObj, "hp"),
                    damage = GetNumberField(itemObj, "damage"),
                    armor = GetNumberField(itemObj, "armor"),
                    healing = GetNumberField(itemObj, "healing"),
                    critChance = GetNumberField(itemObj, "crit_chance"),
                    craftOre = Mathf.RoundToInt(GetNumberField(itemObj, "craft_ore")),
                    craftGold = Mathf.RoundToInt(GetNumberField(itemObj, "craft_gold")),
                    craftIngotDef = GetStringField(itemObj, "craft_ingot_def"),
                    craftIngotN = Mathf.RoundToInt(GetNumberField(itemObj, "craft_ingot_n")),
                    craftTesseractN = Mathf.RoundToInt(GetNumberField(itemObj, "craft_tesseract_n")),
                    craftItemId = GetStringField(itemObj, "craft_item_id"),
                    craftMinutes = Mathf.RoundToInt(GetNumberField(itemObj, "craft_minutes")),
                    salePrice = Mathf.RoundToInt(GetNumberField(itemObj, "sale_price")),
                };

                items[id] = rec;
            }

            return true;
        }

        private static string GetStringField(string obj, string fieldName)
        {
            var m = Regex.Match(obj, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value : string.Empty;
        }

        private static float GetNumberField(string obj, string fieldName)
        {
            var m = Regex.Match(obj, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*(?<v>-?\\d+(?:\\.\\d+)?)", RegexOptions.IgnoreCase);
            if (!m.Success) return 0f;
            return float.TryParse(m.Groups["v"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0f;
        }

        private static bool TryReadJsonString(string s, ref int cursor, out string value)
        {
            value = null;
            if (cursor >= s.Length || s[cursor] != '"') return false;
            cursor++;
            var start = cursor;
            while (cursor < s.Length)
            {
                if (s[cursor] == '"' && s[cursor - 1] != '\\')
                {
                    value = s.Substring(start, cursor - start);
                    cursor++;
                    return true;
                }
                cursor++;
            }
            return false;
        }

        private static bool TryExtractBracedObject(string s, int openBraceIndex, out string objectText, out int closeBraceIndex)
        {
            objectText = null;
            closeBraceIndex = -1;
            if (openBraceIndex < 0 || openBraceIndex >= s.Length || s[openBraceIndex] != '{') return false;

            var depth = 0;
            var inString = false;
            for (var i = openBraceIndex; i < s.Length; i++)
            {
                var ch = s[i];
                if (ch == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
                if (inString) continue;

                if (ch == '{') depth++;
                else if (ch == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeBraceIndex = i;
                        objectText = s.Substring(openBraceIndex, closeBraceIndex - openBraceIndex + 1);
                        return true;
                    }
                }
            }

            return false;
        }

        private static void SkipWhitespaceAndCommas(string s, ref int cursor)
        {
            while (cursor < s.Length && (char.IsWhiteSpace(s[cursor]) || s[cursor] == ',')) cursor++;
        }

        private static void SkipWhitespace(string s, ref int cursor)
        {
            while (cursor < s.Length && char.IsWhiteSpace(s[cursor])) cursor++;
        }

        private static string MakeSafeAssetName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "ItemDefinition";
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                for (var j = 0; j < invalid.Length; j++)
                {
                    if (chars[i] != invalid[j]) continue;
                    chars[i] = '_';
                    break;
                }
            }
            return new string(chars);
        }

        private static void EnsureFolder(string fullAssetPath)
        {
            if (AssetDatabase.IsValidFolder(fullAssetPath)) return;
            var normalized = fullAssetPath.Replace("\\", "/");
            var parent = Path.GetDirectoryName(normalized)?.Replace("\\", "/");
            var name = Path.GetFileName(normalized);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name)) return;
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            if (!AssetDatabase.IsValidFolder(normalized))
                AssetDatabase.CreateFolder(parent, name);
        }

        private struct ServerItemRecord
        {
            public string id;
            public string kind;
            public string quality;
            public string recipeSlot;
            public string craftRecipeId;
            public string slot;
            public int maxStack;
            public int tier;
            public float hp;
            public float damage;
            public float armor;
            public float healing;
            public float critChance;
            public int craftOre;
            public int craftGold;
            public string craftIngotDef;
            public int craftIngotN;
            public int craftTesseractN;
            public string craftItemId;
            public int craftMinutes;
            public int salePrice;
        }
    }
}

