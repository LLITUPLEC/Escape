using System;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    public sealed class ExpeditionDailyArtImporter : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Resources/Expedition/Daily/039/";
        private static readonly string[] Names = { "daily-login", "daily-quests", "elixir_assault", "elixir_stone", "elixir_gambit", "scroll_pve_bounty" };
        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root,StringComparison.Ordinal) || !assetPath.EndsWith(".png",StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite; importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single; importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true; importer.mipmapEnabled = false; importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None; importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear; importer.maxTextureSize = 512; importer.compressionQuality = 90;
            var settings = new TextureImporterSettings(); importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect; settings.spritePixelsPerUnit = 100; settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings { name="Android",overridden=true,maxTextureSize=512,format=TextureImporterFormat.ASTC_4x4,compressionQuality=90,textureCompression=TextureImporterCompression.Compressed });
        }
        public static string Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Wait for Edit mode compilation.");
            foreach(var name in Names)
            {
                AssetDatabase.ImportAsset(Root+name+".png",ImportAssetOptions.ForceUpdate);
                if(Resources.Load<Sprite>("Expedition/Daily/039/"+name)==null) throw new InvalidOperationException("Missing daily sprite: "+name);
            }
            return "PASS daily art: two Refuge button icons and four consumable sprites; native alpha preserved, runtime Resources resolves all six.";
        }
    }
}
