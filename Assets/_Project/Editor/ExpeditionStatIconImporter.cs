using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    public sealed class ExpeditionStatIconImporter : AssetPostprocessor
    {
        public override int GetPostprocessOrder() => 200;

        private void OnPreprocessTexture()
        {
            if (assetPath != "Assets/_Project/Resources/Expedition/Icons/health-red.png" &&
                assetPath != "Assets/_Project/Resources/Expedition/Icons/healing-cross.png") return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 256;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 256,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 80,
                textureCompression = TextureImporterCompression.Compressed
            });
        }
    }
}
