using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Project.Achievements.Editor
{
    /// <summary>
    /// Импорт AchievementDefinition из JSON каталога duel_match3_achievement_defs.
    /// Иконки не трогает — назначаются вручную в инспекторе каждого asset.
    /// </summary>
    public static class AchievementDefinitionsNakamaImportTool
    {
        private const string MenuPath = "Tools/Achievements/Импортировать AchievementDefinition из duel_match3_achievement_defs";
        private const string DefaultJsonRelative = "Server/nakama/data/achievement_catalog.example.json";
        private const string DefsRoot = "Assets/_Project/Data/Achievements";
        private const string MainCatalogPath = AchievementIconCatalog.MainCatalogAssetPath;

        [MenuItem(MenuPath)]
        public static void ImportFromJson()
        {
            var projectRoot = Directory.GetParent(Application.dataPath)?.FullName ?? Application.dataPath;
            var defaultPath = Path.Combine(projectRoot, DefaultJsonRelative);
            var startDir = File.Exists(defaultPath) ? Path.GetDirectoryName(defaultPath) : projectRoot;
            var selectedPath = EditorUtility.OpenFilePanel(
                "Выберите JSON каталога duel_match3_achievement_defs", startDir, "json");
            if (string.IsNullOrEmpty(selectedPath)) return;

            string json;
            try
            {
                json = File.ReadAllText(selectedPath);
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("Импорт AchievementDefinition", "Не удалось прочитать JSON:\n" + e.Message, "OK");
                return;
            }

            if (!TryParseServerChains(json, out var serverChains, out var parseError))
            {
                EditorUtility.DisplayDialog("Импорт AchievementDefinition", "Ошибка парсинга JSON:\n" + parseError, "OK");
                return;
            }

            var missingMode = EditorUtility.DisplayDialogComplex(
                "Импорт AchievementDefinition",
                "Создавать AchievementDefinition для id, которых нет в проекте?\n" +
                "(Иконки не перезаписываются — только id / title_ru / category.)",
                "Да, создать",
                "Только обновить существующие",
                "Отмена");
            if (missingMode == 2) return;
            var createMissing = missingMode == 0;

            var result = ApplyToDefinitions(serverChains, createMissing);
            TryWireCatalogIntoAchievementsPanel();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Импорт AchievementDefinition",
                $"Готово.\n\nВсего в JSON: {serverChains.Count}\nНайдено по id: {result.matched}\nОбновлено: {result.changed}\nСоздано: {result.created}\nБез изменений: {result.unchanged}\nНе найдено в проекте: {result.notFoundInProject.Count}\n\nДальше: открой asset’ы в {DefsRoot} и назначь Sprite в поле Icon.",
                "OK");

            if (result.notFoundInProject.Count > 0)
            {
                Debug.Log("[Achievement Import] В JSON есть id без AchievementDefinition:\n- "
                          + string.Join("\n- ", result.notFoundInProject));
            }
        }

        private static void TryWireCatalogIntoAchievementsPanel()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AchievementIconCatalog>(MainCatalogPath);
            if (catalog == null) return;

            WireCatalogOnPrefab("Assets/_Project/Prefabs/UI/Achievements/AchievementsPanel.prefab", catalog, findInChildren: false);
            WireCatalogOnPrefab("Assets/_Project/Prefabs/MainMenu/MainMenuHudOverlay.prefab", catalog, findInChildren: true);
        }

        private static void WireCatalogOnPrefab(string prefabPath, AchievementIconCatalog catalog, bool findInChildren)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
                return;

            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                var ctrl = findInChildren
                    ? root.GetComponentInChildren<AchievementsPanelController>(true)
                    : root.GetComponent<AchievementsPanelController>();
                if (ctrl == null) return;
                var so = new SerializedObject(ctrl);
                var prop = so.FindProperty("iconCatalog");
                if (prop == null || prop.objectReferenceValue == catalog) return;
                prop.objectReferenceValue = catalog;
                so.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static (int matched, int changed, int created, int unchanged, List<string> notFoundInProject)
            ApplyToDefinitions(Dictionary<string, ServerChainRecord> serverChains, bool createMissing)
        {
            var unmatched = new HashSet<string>(serverChains.Keys, StringComparer.Ordinal);
            var guids = AssetDatabase.FindAssets("t:AchievementDefinition");
            var existingById = new Dictionary<string, AchievementDefinition>(StringComparer.Ordinal);

            var matched = 0;
            var changed = 0;
            var created = 0;
            var unchanged = 0;
            var createdDefs = new List<AchievementDefinition>();

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var def = AssetDatabase.LoadAssetAtPath<AchievementDefinition>(path);
                if (def == null || string.IsNullOrEmpty(def.AchievementId)) continue;
                existingById[def.AchievementId] = def;
                if (!serverChains.TryGetValue(def.AchievementId, out var rec)) continue;

                unmatched.Remove(def.AchievementId);
                matched++;

                if (ApplyRecordToDefinition(def, rec))
                    changed++;
                else
                    unchanged++;
            }

            if (createMissing && unmatched.Count > 0)
            {
                EnsureFolder(DefsRoot);
                EnsureMainCatalogExists();
                var toCreate = new List<string>(unmatched);
                foreach (var id in toCreate)
                {
                    if (!serverChains.TryGetValue(id, out var rec)) continue;
                    if (existingById.ContainsKey(id)) continue;

                    var def = ScriptableObject.CreateInstance<AchievementDefinition>();
                    var filename = MakeSafeAssetName(id) + ".asset";
                    var assetPath = AssetDatabase.GenerateUniqueAssetPath(DefsRoot + "/" + filename);
                    AssetDatabase.CreateAsset(def, assetPath);

                    ApplyRecordToDefinition(def, rec);
                    createdDefs.Add(def);
                    existingById[id] = def;
                    unmatched.Remove(id);
                    created++;
                }

                if (createdDefs.Count > 0)
                    TryAppendToMainCatalog(createdDefs);
            }

            return (matched, changed, created, unchanged, new List<string>(unmatched));
        }

        private static bool ApplyRecordToDefinition(AchievementDefinition def, ServerChainRecord rec)
        {
            if (def == null) return false;
            var so = new SerializedObject(def);
            var localChanged = false;

            localChanged |= SetString(so.FindProperty("achievementId"), rec.id);
            localChanged |= SetString(so.FindProperty("titleRu"), rec.titleRu ?? "");
            localChanged |= SetString(so.FindProperty("category"), rec.category ?? "");

            if (localChanged)
            {
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(def);
            }

            return localChanged;
        }

        private static void EnsureMainCatalogExists()
        {
            if (AssetDatabase.LoadAssetAtPath<AchievementIconCatalog>(MainCatalogPath) != null)
                return;

            EnsureFolder(DefsRoot);
            var catalog = ScriptableObject.CreateInstance<AchievementIconCatalog>();
            AssetDatabase.CreateAsset(catalog, MainCatalogPath);
            EditorUtility.SetDirty(catalog);
        }

        private static void TryAppendToMainCatalog(List<AchievementDefinition> newDefs)
        {
            if (newDefs == null || newDefs.Count == 0) return;
            EnsureMainCatalogExists();
            var catalog = AssetDatabase.LoadAssetAtPath<AchievementIconCatalog>(MainCatalogPath);
            if (catalog == null) return;

            var so = new SerializedObject(catalog);
            var items = so.FindProperty("achievements");
            if (items == null || !items.isArray) return;

            var existing = new HashSet<string>(StringComparer.Ordinal);
            for (var i = 0; i < items.arraySize; i++)
            {
                var entry = items.GetArrayElementAtIndex(i).objectReferenceValue as AchievementDefinition;
                if (entry == null || string.IsNullOrEmpty(entry.AchievementId)) continue;
                existing.Add(entry.AchievementId);
            }

            foreach (var def in newDefs)
            {
                if (def == null || string.IsNullOrEmpty(def.AchievementId)) continue;
                if (existing.Contains(def.AchievementId)) continue;
                var idx = items.arraySize;
                items.InsertArrayElementAtIndex(idx);
                items.GetArrayElementAtIndex(idx).objectReferenceValue = def;
                existing.Add(def.AchievementId);
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static bool TryParseServerChains(string json, out Dictionary<string, ServerChainRecord> chains, out string error)
        {
            chains = new Dictionary<string, ServerChainRecord>(StringComparer.Ordinal);
            error = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                error = "JSON пустой.";
                return false;
            }

            var chainsKeyPos = json.IndexOf("\"chains\"", StringComparison.OrdinalIgnoreCase);
            if (chainsKeyPos < 0)
            {
                error = "Не найден ключ \"chains\".";
                return false;
            }

            var arrStart = json.IndexOf('[', chainsKeyPos);
            if (arrStart < 0)
            {
                error = "Не найдено начало массива chains.";
                return false;
            }

            if (!TryExtractBracketedArray(json, arrStart, out var chainsArray, out _))
            {
                error = "Не удалось вычитать массив chains (скобки).";
                return false;
            }

            var cursor = 1; // после '['
            while (cursor < chainsArray.Length - 1)
            {
                SkipWhitespaceAndCommas(chainsArray, ref cursor);
                if (cursor >= chainsArray.Length - 1) break;
                if (chainsArray[cursor] != '{')
                {
                    error = "Ожидался объект chain в массиве chains.";
                    return false;
                }

                if (!TryExtractBracedObject(chainsArray, cursor, out var chainObj, out var chainObjEnd))
                {
                    error = "Не удалось прочитать объект chain.";
                    return false;
                }
                cursor = chainObjEnd + 1;

                var id = GetStringField(chainObj, "id");
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                chains[id] = new ServerChainRecord
                {
                    id = id,
                    category = GetStringField(chainObj, "category"),
                    titleRu = GetStringField(chainObj, "title_ru"),
                };
            }

            if (chains.Count == 0)
            {
                error = "В массиве chains нет ни одного объекта с id.";
                return false;
            }

            return true;
        }

        private static string GetStringField(string obj, string fieldName)
        {
            var m = Regex.Match(obj, $"\"{Regex.Escape(fieldName)}\"\\s*:\\s*\"(?<v>[^\"]*)\"", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups["v"].Value : string.Empty;
        }

        private static bool TryExtractBracketedArray(string s, int openBracketIndex, out string arrayText, out int closeBracketIndex)
        {
            arrayText = null;
            closeBracketIndex = -1;
            if (openBracketIndex < 0 || openBracketIndex >= s.Length || s[openBracketIndex] != '[') return false;

            var depth = 0;
            var inString = false;
            for (var i = openBracketIndex; i < s.Length; i++)
            {
                var ch = s[i];
                if (ch == '"' && (i == 0 || s[i - 1] != '\\')) inString = !inString;
                if (inString) continue;

                if (ch == '[') depth++;
                else if (ch == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        closeBracketIndex = i;
                        arrayText = s.Substring(openBracketIndex, closeBracketIndex - openBracketIndex + 1);
                        return true;
                    }
                }
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

        private static bool SetString(SerializedProperty p, string value)
        {
            if (p == null) return false;
            if (p.stringValue == value) return false;
            p.stringValue = value;
            return true;
        }

        private static string MakeSafeAssetName(string value)
        {
            if (string.IsNullOrEmpty(value)) return "AchievementDefinition";
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

        private struct ServerChainRecord
        {
            public string id;
            public string category;
            public string titleRu;
        }
    }
}
