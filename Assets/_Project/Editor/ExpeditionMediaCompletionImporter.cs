using System;
using System.IO;
using Project.Match3;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Production import settings and reproducible bindings for completed media.</summary>
    public sealed class ExpeditionMediaCompletionImporter : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Resources/Expedition/";
        private const string Data = "Assets/_Project/Data/Match3/Monsters/";
        private static readonly string[] Directories = { "Appearance", "Monsters", "Icons" };
        private static readonly string[] Difficulties = { "easy", "medium", "hard", "boss" };

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root + "Appearance/", StringComparison.Ordinal) &&
                !assetPath.StartsWith(Root + "Monsters/", StringComparison.Ordinal) &&
                !assetPath.StartsWith(Root + "Icons/", StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            var frame = Path.GetFileName(assetPath).StartsWith("monster-frame-", StringComparison.Ordinal);
            var small = frame || assetPath.EndsWith("-avatar.png", StringComparison.Ordinal) || assetPath.StartsWith(Root + "Icons/", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = small ? 512 : 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 80;
            importer.spriteBorder = frame ? new Vector4(160, 160, 160, 160) : Vector4.zero;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = 100;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = importer.maxTextureSize,
                format = TextureImporterFormat.ASTC_6x6,
                textureCompression = TextureImporterCompression.Compressed, compressionQuality = 80
            });
        }

        [MenuItem("Tools/Escape/Expedition/Prepare completed media")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play mode and wait for compilation before preparing media.");
            foreach (var directory in Directories)
            {
                var path = Root + directory;
                if (!Directory.Exists(path)) throw new InvalidOperationException("Media folder missing: " + path);
                foreach (var file in Directory.GetFiles(path, "*.png"))
                    AssetDatabase.ImportAsset(file.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
            }
            for (var floor = 1; floor <= 12; floor++)
            {
                var definition = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(Data + "mine_" + floor + ".asset");
                if (definition == null) throw new InvalidOperationException("Monster definition missing: mine_" + floor);
                Assign(definition, "icon", Require("Monsters/mine-" + floor));
            }
            var frames = AssetDatabase.LoadAssetAtPath<MonsterFrameCatalog>(Data + "MainMonsterFrameCatalog.asset");
            if (frames == null) throw new InvalidOperationException("Shared monster frame catalog missing.");
            foreach (var difficulty in Difficulties)
                Assign(frames, "frame" + char.ToUpperInvariant(difficulty[0]) + difficulty.Substring(1), Require("Monsters/monster-frame-" + difficulty));
            AssetDatabase.SaveAssets();
            Debug.Log(Validate());
        }

        public static string Validate()
        {
            for (var floor = 1; floor <= 12; floor++)
            {
                var definition = AssetDatabase.LoadAssetAtPath<MonsterDefinition>(Data + "mine_" + floor + ".asset");
                if (definition == null || definition.Icon == null) throw new InvalidOperationException("Missing monster sprite: " + floor);
                if (definition.Icon != Require("Monsters/mine-" + floor))
                    throw new InvalidOperationException("Monster uses a placeholder: " + floor);
            }
            var frames = AssetDatabase.LoadAssetAtPath<MonsterFrameCatalog>(Data + "MainMonsterFrameCatalog.asset");
            foreach (var difficulty in Difficulties)
            {
                var expected = Require("Monsters/monster-frame-" + difficulty);
                if (frames == null || frames.GetFrame(difficulty, difficulty == "boss") != expected || expected.border == Vector4.zero)
                    throw new InvalidOperationException("Missing dedicated sliced frame: " + difficulty);
            }
            if (Project.UI.ExpeditionArt.Icon("friends") != Require("Icons/friends"))
                throw new InvalidOperationException("Friends icon failed runtime Resources lookup.");
            return "PASS completed media: 12 dedicated mine monster sprites, 4 sliced difficulty frames, friends icon.";
        }

        private static Sprite Require(string resource)
        {
            var sprite = Resources.Load<Sprite>("Expedition/" + resource);
            if (sprite == null) throw new InvalidOperationException("Missing imported resource: " + resource);
            return sprite;
        }

        private static void Assign(UnityEngine.Object target, string property, Sprite sprite)
        {
            var so = new SerializedObject(target);
            var field = so.FindProperty(property);
            if (field == null) throw new InvalidOperationException("Missing field: " + property);
            if (field.objectReferenceValue == sprite) return;
            field.objectReferenceValue = sprite;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
