#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>
    /// Однократное принудительное переимпортирование TMP Fallback — снимает предупреждение
    /// NativeFormatImporter inconsistent result на части версий Unity после обновления.
    /// </summary>
    [InitializeOnLoad]
    internal static class TmpFallbackReimportOnce
    {
        private const string PrefKey = "Escape.TmpFallbackReimportV2";
        private const string AssetPath = "Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF - Fallback.asset";

        static TmpFallbackReimportOnce()
        {
            EditorApplication.delayCall += RunOnce;
        }

        private static void RunOnce()
        {
            if (EditorPrefs.GetBool(PrefKey, false))
                return;
            if (AssetDatabase.LoadAssetAtPath<Object>(AssetPath) == null)
                return;
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            EditorPrefs.SetBool(PrefKey, true);
        }
    }
}
#endif
