using System.IO;
using UnityEditor;
using UnityEngine;

namespace Project.Match3.Editor
{
    public static class Match3FuryFxCatalogCreator
    {
        private const string ResourcesDir = "Assets/_Project/Resources/Match3";
        private const string AssetPath = ResourcesDir + "/FuryFxCatalog.asset";
        private const string FlameDir = "Assets/_Project/img/Flame";

        [MenuItem("Tools/Match3/Создать FuryFxCatalog (огонь Ярости)")]
        public static void CreateOrUpdate()
        {
            if (!Directory.Exists(ResourcesDir))
                Directory.CreateDirectory(ResourcesDir);

            var catalog = AssetDatabase.LoadAssetAtPath<Match3FuryFxCatalog>(AssetPath);
            if (catalog == null)
            {
                catalog = ScriptableObject.CreateInstance<Match3FuryFxCatalog>();
                AssetDatabase.CreateAsset(catalog, AssetPath);
            }

            var frames = new Sprite[7];
            for (var i = 0; i < frames.Length; i++)
                frames[i] = AssetDatabase.LoadAssetAtPath<Sprite>($"{FlameDir}/f{i + 1}.png");

            catalog.flameFrames = frames;
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = catalog;
            EditorGUIUtility.PingObject(catalog);
            Debug.Log("[Match3] FuryFxCatalog обновлён: " + AssetPath);
        }
    }
}
