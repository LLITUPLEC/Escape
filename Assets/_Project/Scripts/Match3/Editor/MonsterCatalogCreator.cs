using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Project.Match3.Editor
{
    public static class MonsterCatalogCreator
    {
        private const string RootDir = "Assets/_Project/Data/Match3/Monsters";
        private const string CatalogPath = RootDir + "/MainMonsterCatalog.asset";
        private const string FrameCatalogPath = RootDir + "/MainMonsterFrameCatalog.asset";

        [MenuItem("Tools/Match3/Создать каталог монстров")]
        public static void CreateCatalog()
        {
            EnsureDir(RootDir);

            var defs = new List<MonsterDefinition>();
            for (var floor = 1; floor <= 12; floor++)
            {
                var defPath = $"{RootDir}/mine_{floor}.asset";
                var def = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(defPath);
                if (def == null)
                {
                    def = ScriptableObject.CreateInstance<MonsterDefinition>();
                    var so = new SerializedObject(def);
                    so.FindProperty("botId").stringValue = "mine_" + floor;
                    so.FindProperty("displayName").stringValue = floor == 4 || floor == 8 || floor == 12
                        ? $"Страж шахты {floor}"
                        : $"Шахтный монстр {floor}";
                    so.FindProperty("floor").intValue = floor;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    AssetDatabase.CreateAsset(def, defPath);
                }
                defs.Add(def);
            }

            var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>(CatalogPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<MonsterCatalog>();
                AssetDatabase.CreateAsset(catalog, CatalogPath);
            }

            var catSo = new SerializedObject(catalog);
            var monstersProp = catSo.FindProperty("monsters");
            monstersProp.arraySize = defs.Count;
            for (var i = 0; i < defs.Count; i++)
                monstersProp.GetArrayElementAtIndex(i).objectReferenceValue = defs[i];
            catSo.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);

            var frameCatalog = AssetDatabase.LoadAssetAtPath<MonsterFrameCatalog>(FrameCatalogPath);
            if (frameCatalog == null)
            {
                frameCatalog = ScriptableObject.CreateInstance<MonsterFrameCatalog>();
                AssetDatabase.CreateAsset(frameCatalog, FrameCatalogPath);
            }
            EditorUtility.SetDirty(frameCatalog);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = catalog;
        }

        private static void EnsureDir(string path)
        {
            if (Directory.Exists(path)) return;
            Directory.CreateDirectory(path);
        }
    }
}
