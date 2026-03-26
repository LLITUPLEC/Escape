using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3.Editor
{
    public static class Match3CheatOverlayPrefabCreator
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Match3/CheatRowsOverlayCells.prefab";

        [MenuItem("Tools/Match3/Создать префаб CheatRowsOverlayCells")]
        public static void CreatePrefab()
        {
            var root = new GameObject("CheatRowsOverlayCells", typeof(RectTransform));
            var containerRt = root.GetComponent<RectTransform>();
            containerRt.anchorMin = new Vector2(0.5f, 0.5f);
            containerRt.anchorMax = new Vector2(0.5f, 0.5f);
            containerRt.pivot = new Vector2(0.5f, 0.5f);
            containerRt.anchoredPosition = new Vector2(-12.5f, 301f);

            var glg = root.AddComponent<GridLayoutGroup>();
            glg.cellSize = new Vector2(74, 74);
            glg.spacing = new Vector2(-1, -1);
            glg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            glg.constraintCount = 6;
            glg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            glg.startAxis = GridLayoutGroup.Axis.Horizontal;
            glg.childAlignment = TextAnchor.UpperLeft;

            var dir = Path.GetDirectoryName(PrefabPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab != null)
                Selection.activeObject = prefab;
        }
    }
}

