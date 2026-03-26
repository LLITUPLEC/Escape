#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Массовая миграция устаревшего UnityEngine.UI.Text -> TextMeshProUGUI в префабах и сценах,
/// с копированием настроек и перепривязкой ссылок внутри того же ассета.
///
/// Меню: Tools → UI → Миграция Text → TMP (проект)
/// </summary>
public static class UiTextToTmpMigrator
{
    [MenuItem("Tools/UI/Миграция Text → TMP (проект)")]
    public static void MigrateProject()
    {
        try
        {
            var modifiedPrefabs = MigrateAllPrefabs();
            var modifiedScenes = MigrateAllScenes();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TMP Migrator] Готово. Изменено префабов: {modifiedPrefabs}, сцен: {modifiedScenes}.");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private static int MigrateAllPrefabs()
    {
        int modified = 0;
        var prefabGuids = AssetDatabase.FindAssets("t:Prefab");
        for (int i = 0; i < prefabGuids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
            if (string.IsNullOrEmpty(path)) continue;
            // Process only editable project assets (skip Packages/, Library/, etc).
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                bool changed = MigratePrefabAtPath(path);
                if (changed) modified++;
            }
            catch (Exception e)
            {
                Debug.LogException(new Exception($"[TMP Migrator] Ошибка префаба: {path}", e));
            }
        }
        return modified;
    }

    private static bool MigratePrefabAtPath(string path)
    {
        GameObject root = null;
        try
        {
            root = PrefabUtility.LoadPrefabContents(path);
            bool changed = MigrateHierarchy(root);
            if (changed)
            {
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            return changed;
        }
        finally
        {
            if (root != null)
                PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static int MigrateAllScenes()
    {
        int modified = 0;
        var sceneGuids = AssetDatabase.FindAssets("t:Scene");
        for (int i = 0; i < sceneGuids.Length; i++)
        {
            var path = AssetDatabase.GUIDToAssetPath(sceneGuids[i]);
            if (string.IsNullOrEmpty(path)) continue;
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)) continue;

            try
            {
                bool changed = MigrateSceneAtPath(path);
                if (changed) modified++;
            }
            catch (Exception e)
            {
                Debug.LogException(new Exception($"[TMP Migrator] Ошибка сцены: {path}", e));
            }
        }
        return modified;
    }

    private static bool MigrateSceneAtPath(string path)
    {
        var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        bool changed = false;
        try
        {
            var roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
                changed |= MigrateHierarchy(roots[i]);

            if (changed)
                EditorSceneManager.SaveScene(scene);
        }
        finally
        {
            // scene stays open; that's fine in editor context
        }

        return changed;
    }

    private static bool MigrateHierarchy(GameObject root)
    {
        if (root == null) return false;

        bool changed = false;
        var texts = root.GetComponentsInChildren<Text>(true);
        if (texts == null || texts.Length == 0) return false;

        // Process deeper nodes first (safer for nested prefabs/layout).
        Array.Sort(texts, (a, b) =>
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int da = GetDepth(a.transform);
            int db = GetDepth(b.transform);
            return db.CompareTo(da);
        });

        foreach (var legacy in texts)
        {
            if (legacy == null) continue;
            if (legacy.GetComponent<TextMeshProUGUI>() != null)
                continue; // already has TMP - likely manually migrated

            var go = legacy.gameObject;
            try
            {
                // Can't add TMP while Text exists (both are Graphic). So:
                // 1) find all references to legacy Text,
                // 2) snapshot legacy settings,
                // 3) destroy legacy,
                // 4) add TMP and apply snapshot,
                // 5) rebind previously collected references.
                var refs = CollectReferencePaths(root, legacy);
                var snap = new TextSnapshot(legacy);

                Undo.DestroyObjectImmediate(legacy);
                var tmp = go.AddComponent<TextMeshProUGUI>();
                CopySnapshotToTmp(snap, tmp);

                ApplyReferencePaths(refs, tmp);
                EditorUtility.SetDirty(go);
                changed = true;
            }
            catch (Exception e)
            {
                Debug.LogException(new Exception($"[TMP Migrator] Не удалось мигрировать: {GetTransformPath(go.transform)}", e));
            }
        }

        return changed;
    }

    private static int GetDepth(Transform t)
    {
        int d = 0;
        while (t != null)
        {
            d++;
            t = t.parent;
        }
        return d;
    }

    private static List<(UnityEngine.Object target, string propertyPath)> CollectReferencePaths(GameObject searchRoot, Text from)
    {
        var list = new List<(UnityEngine.Object, string)>();
        if (searchRoot == null || from == null) return list;

        var all = searchRoot.GetComponentsInChildren<Component>(true);
        foreach (var c in all)
        {
            if (c == null) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            if (!it.NextVisible(true)) continue;
            do
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference) continue;
                if (it.objectReferenceValue == from)
                    list.Add((c, it.propertyPath));
            }
            while (it.NextVisible(true));
        }
        return list;
    }

    private static void ApplyReferencePaths(List<(UnityEngine.Object target, string propertyPath)> refs, TMP_Text to)
    {
        if (refs == null || to == null) return;
        for (int i = 0; i < refs.Count; i++)
        {
            var (target, propertyPath) = refs[i];
            if (target == null || string.IsNullOrEmpty(propertyPath)) continue;
            var so = new SerializedObject(target);
            var p = so.FindProperty(propertyPath);
            if (p == null || p.propertyType != SerializedPropertyType.ObjectReference) continue;
            p.objectReferenceValue = to;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }

    private readonly struct TextSnapshot
    {
        public readonly string text;
        public readonly Color color;
        public readonly int fontSize;
        public readonly bool raycastTarget;
        public readonly bool richText;
        public readonly bool bestFit;
        public readonly int bestFitMin;
        public readonly int bestFitMax;
        public readonly TextAnchor alignment;
        public readonly HorizontalWrapMode hOverflow;
        public readonly VerticalWrapMode vOverflow;
        public readonly float lineSpacing;
        public readonly FontStyle fontStyle;

        public TextSnapshot(Text src)
        {
            text = src.text;
            color = src.color;
            fontSize = src.fontSize;
            raycastTarget = src.raycastTarget;
            richText = src.supportRichText;
            bestFit = src.resizeTextForBestFit;
            bestFitMin = src.resizeTextMinSize;
            bestFitMax = src.resizeTextMaxSize;
            alignment = src.alignment;
            hOverflow = src.horizontalOverflow;
            vOverflow = src.verticalOverflow;
            lineSpacing = src.lineSpacing;
            fontStyle = src.fontStyle;
        }
    }

    private static void CopySnapshotToTmp(TextSnapshot src, TextMeshProUGUI dst)
    {
        if (dst == null) return;

        dst.text = src.text;
        dst.color = src.color;
        dst.raycastTarget = src.raycastTarget;
        dst.richText = src.richText;
        dst.fontSize = src.fontSize;

        dst.enableAutoSizing = src.bestFit;
        if (dst.enableAutoSizing)
        {
            dst.fontSizeMin = src.bestFitMin;
            dst.fontSizeMax = src.bestFitMax;
        }

        dst.alignment = MapAlignment(src.alignment);
        dst.overflowMode = MapOverflow(src.hOverflow, src.vOverflow);
        dst.lineSpacing = Mathf.Max(0f, (src.lineSpacing - 1f) * 10f);
        dst.fontStyle = MapFontStyle(src.fontStyle);
        dst.font = TMP_Settings.defaultFontAsset;
    }

    private static string GetTransformPath(Transform t)
    {
        if (t == null) return "<null>";
        var stack = new Stack<string>();
        while (t != null)
        {
            stack.Push(t.name);
            t = t.parent;
        }
        return string.Join("/", stack);
    }

    private static TextAlignmentOptions MapAlignment(TextAnchor a)
    {
        return a switch
        {
            TextAnchor.UpperLeft => TextAlignmentOptions.TopLeft,
            TextAnchor.UpperCenter => TextAlignmentOptions.Top,
            TextAnchor.UpperRight => TextAlignmentOptions.TopRight,
            TextAnchor.MiddleLeft => TextAlignmentOptions.Left,
            TextAnchor.MiddleCenter => TextAlignmentOptions.Center,
            TextAnchor.MiddleRight => TextAlignmentOptions.Right,
            TextAnchor.LowerLeft => TextAlignmentOptions.BottomLeft,
            TextAnchor.LowerCenter => TextAlignmentOptions.Bottom,
            TextAnchor.LowerRight => TextAlignmentOptions.BottomRight,
            _ => TextAlignmentOptions.Center,
        };
    }

    private static TextOverflowModes MapOverflow(HorizontalWrapMode h, VerticalWrapMode v)
    {
        // TMP has different set of modes; pick the closest.
        if (v == VerticalWrapMode.Truncate)
            return TextOverflowModes.Truncate;

        // Overflow
        if (h == HorizontalWrapMode.Overflow && v == VerticalWrapMode.Overflow)
            return TextOverflowModes.Overflow;

        // Wrap (default)
        return TextOverflowModes.Overflow;
    }

    private static FontStyles MapFontStyle(FontStyle s)
    {
        return s switch
        {
            FontStyle.Bold => FontStyles.Bold,
            FontStyle.Italic => FontStyles.Italic,
            FontStyle.BoldAndItalic => FontStyles.Bold | FontStyles.Italic,
            _ => FontStyles.Normal,
        };
    }
}
#endif

