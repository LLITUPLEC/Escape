using TMPro;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Project.Achievements
{
    /// <summary>
    /// Единая точка выбора шрифта для UI достижений (TF2CSecondary в Assets/_Project/Fonts).
    /// </summary>
    internal static class AchievementUiFontLoader
    {
        private const string EditorFontAssetPath = "Assets/_Project/Fonts/TF2CSecondary SDF.asset";

        private static TMP_FontAsset _cachedResolved;

        internal static TMP_FontAsset Resolve(TMP_FontAsset serializedOverride)
        {
            if (serializedOverride != null)
                return serializedOverride;

            if (_cachedResolved != null)
                return _cachedResolved;

#if UNITY_EDITOR
            _cachedResolved = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(EditorFontAssetPath);
#endif
            if (_cachedResolved == null)
                _cachedResolved = Resources.Load<TMP_FontAsset>("Fonts/TF2CSecondary SDF");

            return _cachedResolved != null ? _cachedResolved : TMP_Settings.defaultFontAsset;
        }
    }
}
