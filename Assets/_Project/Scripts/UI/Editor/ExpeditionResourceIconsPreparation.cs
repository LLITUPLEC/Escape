using System;
using UnityEditor;
using UnityEngine;

namespace Project.UI.Editor
{
    /// <summary>Validates and rebinds the four original sprites; never changes their importers.</summary>
    public static class ExpeditionResourceIconsPreparation
    {
        public const string AssetPath = "Assets/_Project/Resources/Expedition/UI/resource-icons.asset";
        private const string SourceRoot = "Assets/_Project/img/resources_hud/";

        public static string Prepare()
        {
            var keys = new[] { "ore", "gold", "matter", "energy" };
            var sprites = new Sprite[keys.Length];
            for (var i = 0; i < keys.Length; i++)
            {
                var path = SourceRoot + keys[i] + ".png";
                sprites[i] = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprites[i] == null) throw new InvalidOperationException("Original resource sprite is missing: " + path);
            }

            var icons = AssetDatabase.LoadAssetAtPath<ExpeditionResourceIcons>(AssetPath);
            if (icons == null)
            {
                const string folder = "Assets/_Project/Resources/Expedition/UI";
                if (!AssetDatabase.IsValidFolder(folder)) throw new InvalidOperationException("Resource folder is missing: " + folder);
                icons = ScriptableObject.CreateInstance<ExpeditionResourceIcons>();
                AssetDatabase.CreateAsset(icons, AssetPath);
            }

            var serialized = new SerializedObject(icons);
            for (var i = 0; i < keys.Length; i++) serialized.FindProperty(keys[i]).objectReferenceValue = sprites[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(icons);
            AssetDatabase.SaveAssetIfDirty(icons);
            return "Prepared " + AssetPath + ": ore, gold, matter, energy reference the original resource sprites.";
        }
    }
}
