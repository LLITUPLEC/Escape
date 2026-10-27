using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Preserves generated alpha and slices native 4x4 VFX sheets without editing source pixels.</summary>
    public sealed class ExpeditionAbilityFxImporter : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Resources/Expedition/Effects/038/";
        private static readonly string[] Names = { "fury-frame-atlas", "explosion-atlas", "shield", "petard" };

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Root, StringComparison.Ordinal) || !assetPath.EndsWith(".png", StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
            var name = Path.GetFileNameWithoutExtension(assetPath);
            var atlas = name.EndsWith("-atlas", StringComparison.Ordinal);
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            importer.spriteImportMode = atlas ? SpriteImportMode.Multiple : SpriteImportMode.Single;
            importer.mipmapEnabled = false;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = atlas ? 2048 : 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 90;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = 100;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = importer.maxTextureSize,
                format = TextureImporterFormat.ASTC_4x4, compressionQuality = 90,
                textureCompression = TextureImporterCompression.Compressed
            });
            if (!atlas) return;
            importer.GetSourceTextureWidthAndHeight(out var width, out var height);
            var rects = new SpriteRect[16];
            for (var index = 0; index < rects.Length; index++)
            {
                var col = index % 4; var row = index / 4;
                var x0 = col * width / 4; var x1 = (col + 1) * width / 4;
                var y0 = row * height / 4; var y1 = (row + 1) * height / 4;
                var frameName = name + "_" + index.ToString("00");
                string id;
                using (var hash = MD5.Create()) id = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes("Expedition/Effects/038/" + frameName))).Replace("-", "").ToLowerInvariant();
                rects[index] = new SpriteRect { name = frameName, spriteID = new GUID(id),
                    rect = new Rect(x0, height - y1, x1 - x0, y1 - y0), alignment = SpriteAlignment.Center, pivot = new Vector2(.5f, .5f) };
            }
            var factories = new SpriteDataProviderFactories(); factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) throw new InvalidOperationException("Missing sprite data provider: " + assetPath);
            provider.InitSpriteEditorDataProvider(); provider.SetSpriteRects(rects);
            provider.GetDataProvider<ISpriteNameFileIdDataProvider>()?.SetNameFileIdPairs(rects.Select(r => new SpriteNameFileIdPair(r.name, r.spriteID)).ToList());
            provider.Apply();
        }

        [MenuItem("Tools/Escape/Expedition/Prepare ability effects 038")]
        private static void PrepareMenu() => Debug.Log(Prepare());

        public static string Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Stop Play and wait for compilation.");
            foreach (var name in Names) AssetDatabase.ImportAsset(Root + name + ".png", ImportAssetOptions.ForceUpdate);
            return Validate();
        }

        public static string Validate()
        {
            foreach (var name in Names)
            {
                var sprites = Resources.LoadAll<Sprite>("Expedition/Effects/038/" + name).OrderBy(s => s.name, StringComparer.Ordinal).ToArray();
                var expected = name.EndsWith("-atlas", StringComparison.Ordinal) ? 16 : 1;
                if (sprites.Length != expected) throw new InvalidOperationException(name + ": expected " + expected + " sprites, found " + sprites.Length);
                if (expected == 16)
                    for (var i = 0; i < 16; i++)
                        if (sprites[i].name != name + "_" + i.ToString("00") || sprites[i].vertices.Length != 4)
                            throw new InvalidOperationException("Unexpected frame name or mesh: " + sprites[i].name);
            }
            return "PASS ability art: 16 fire-frame sprites, 16 explosion sprites, shield and petard; native alpha retained, FullRect meshes, stable top-left chronological slicing.";
        }
    }
}
