using System.IO;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace Project.Editor
{
    public sealed class ExpeditionArtImporter : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith("Assets/_Project/Resources/Expedition/Art/")) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 2048;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 70;
            importer.spriteBorder = Vector4.zero;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.textureShape = TextureImporterShape.Texture2D;
            settings.spritePixelsPerUnit = 100;
            importer.SetTextureSettings(settings);
            if (assetPath.EndsWith("/battle-world-v4.png") || assetPath.EndsWith("/battle-world-v5.png"))
            {
                importer.alphaIsTransparency = false;
                importer.maxTextureSize = 2048;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = importer.maxTextureSize;
                android.format = TextureImporterFormat.ETC2_RGB4;
                importer.SetPlatformTextureSettings(android);
            }
            if (assetPath.EndsWith("/hero-victory-bust-v1.png"))
            {
                importer.maxTextureSize = 1024;
                var android = importer.GetPlatformTextureSettings("Android");
                android.overridden = true;
                android.maxTextureSize = 1024;
                android.format = TextureImporterFormat.ETC2_RGBA8;
                importer.SetPlatformTextureSettings(android);
            }
        }

        [InitializeOnLoadMethod]
        private static void QueueSetup() => EditorApplication.delayCall += Setup;

        [MenuItem("Tools/Escape/Expedition/Prepare art and font")]
        public static void Setup()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            PrepareSerif();
            const string art = "Assets/_Project/Resources/Expedition/Art";
            if (Directory.Exists(art))
                foreach (var file in Directory.GetFiles(art, "*.png"))
                {
                    var path = file.Replace('\\', '/');
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                    if (importer != null && (importer.textureType != TextureImporterType.Sprite || importer.mipmapEnabled ||
                        importer.textureShape != TextureImporterShape.Texture2D))
                        importer.SaveAndReimport();
                }
        }

        private static void PrepareSerif()
        {
            const string path = "Assets/_Project/Resources/Expedition/ExpeditionSerif.asset";
            var existing = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if (existing != null) { KeepPreparedGlyphs(existing); return; }
            var source = AssetDatabase.LoadAssetAtPath<Font>("Assets/_Project/Fonts/PTSerif/PT_Serif-Web-Regular.ttf");
            if (source == null) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var font = TMP_FontAsset.CreateFontAsset(source, 64, 7, GlyphRenderMode.SDFAA, 2048, 2048, AtlasPopulationMode.Dynamic, true);
            font.name = "ExpeditionSerif";
            font.isMultiAtlasTexturesEnabled = true;
            var characters = new StringBuilder();
            for (var c = 32; c < 256; c++) characters.Append((char)c);
            for (var c = 0x400; c < 0x530; c++) characters.Append((char)c);
            characters.Append("—–…•№×−→←↑↓✓★");
            font.TryAddCharacters(characters.ToString(), out _);
            AssetDatabase.CreateAsset(font, path);
            AssetDatabase.AddObjectToAsset(font.material, font);
            foreach (var atlas in font.atlasTextures)
                if (atlas != null && !AssetDatabase.Contains(atlas)) AssetDatabase.AddObjectToAsset(atlas, font);
            KeepPreparedGlyphs(font);
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssets();
        }

        private static void KeepPreparedGlyphs(TMP_FontAsset font)
        {
            // TMP's pre-build processor otherwise removes the prepared Cyrillic atlas.
            var serialized = new SerializedObject(font);
            var clear = serialized.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear == null || !clear.boolValue) return;
            clear.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(font);
            AssetDatabase.SaveAssetIfDirty(font);
        }
    }
}
