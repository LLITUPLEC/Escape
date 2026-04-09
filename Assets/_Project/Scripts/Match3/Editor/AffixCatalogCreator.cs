using UnityEditor;
using UnityEngine;

namespace Project.Match3.Editor
{
    public static class AffixCatalogCreator
    {
        private const string AssetPath = "Assets/_Project/Resources/Match3/AffixCatalog.asset";

        private static readonly string[] Ids =
        {
            "acid",
            "energy_block",
            "regeneration",
            "fragility",
            "stone_skin",
            "mana_vampire",
            "frozen",
            "monster_rage",
            "instability",
            "overload",
            "bare_current",
            "scree",
        };

        [MenuItem("Tools/Match3/Создать или обновить AffixCatalog")]
        public static void CreateOrUpdate()
        {
            EnsureFolder("Assets/_Project/Resources");
            EnsureFolder("Assets/_Project/Resources/Match3");

            var asset = AssetDatabase.LoadAssetAtPath<AffixCatalog>(AssetPath);
            if (asset == null)
            {
                asset = ScriptableObject.CreateInstance<AffixCatalog>();
                AssetDatabase.CreateAsset(asset, AssetPath);
            }

            var so = new SerializedObject(asset);
            var arr = so.FindProperty("affixes");
            arr.arraySize = Ids.Length;
            for (var i = 0; i < Ids.Length; i++)
            {
                var item = arr.GetArrayElementAtIndex(i);
                var id = Ids[i];
                var title = string.Empty;
                var desc = string.Empty;
                AffixCatalog.TryGetBuiltin(id, out title, out desc);
                item.FindPropertyRelative("id").stringValue = id;
                item.FindPropertyRelative("title").stringValue = title;
                item.FindPropertyRelative("description").stringValue = desc;
                // icon сохраняем как есть, если уже был назначен.
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = asset;
            Debug.Log("[AffixCatalog] Готово: " + AssetPath);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = System.IO.Path.GetDirectoryName(path)?.Replace("\\", "/");
            var name = System.IO.Path.GetFileName(path);
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);
            if (!string.IsNullOrEmpty(parent) && !string.IsNullOrEmpty(name))
                AssetDatabase.CreateFolder(parent, name);
        }
    }
}
