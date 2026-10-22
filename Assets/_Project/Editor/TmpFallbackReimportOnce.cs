#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using TMPro;

namespace Project.Editor
{
    /// <summary>
    /// Сохраняет изменённые TMP sub-assets перед целевым переимпортом.
    /// Не изменяет таблицы глифов/атласы и не запускается во время Play Mode или импорта.
    /// </summary>
    [InitializeOnLoad]
    internal static class TmpFallbackReimportOnce
    {
        private const string MenuPath = "Tools/Escape/Repair TMP Fallback Import";
        private const string AssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";
        private static string PrefKey => "Escape.TmpFallbackReimportV3." + Hash128.Compute(Application.dataPath);
        private static bool EditorIsIdle => !EditorApplication.isPlayingOrWillChangePlaymode
            && !EditorApplication.isCompiling && !EditorApplication.isUpdating;

        static TmpFallbackReimportOnce()
        {
            EditorApplication.delayCall += RunOnce;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredEditMode)
                    EditorApplication.delayCall += RunOnce;
            };
        }

        private static void RunOnce()
        {
            if (EditorPrefs.GetBool(PrefKey, false))
                return;
            if (!EditorIsIdle)
                return;
            if (SaveAndReimport()) EditorPrefs.SetBool(PrefKey, true);
        }

        [MenuItem(MenuPath)]
        private static void RepairManually()
        {
            if (SaveAndReimport()) EditorPrefs.SetBool(PrefKey, true);
        }

        [MenuItem(MenuPath, true)]
        private static bool CanRepairManually() => EditorIsIdle;

        private static bool SaveAndReimport()
        {
            if (!EditorIsIdle) return false;
            var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(AssetPath);
            if (font == null) return false;
            // Dynamic fonts can have dirty atlas sub-assets. Persist those before asking the importer
            // to read the same file, otherwise the generated import result can differ from memory.
            AssetDatabase.SaveAssetIfDirty(font);
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            Debug.Log("[TMP] Saved and reimported fallback font: " + AssetPath);
            return true;
        }
    }
}
#endif
