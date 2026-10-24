using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Only imports the separate interface art kit; existing game pieces are untouched.</summary>
    public sealed class ExpeditionUiArtImporter : AssetPostprocessor
    {
        private const string Root = "Assets/_Project/Resources/Expedition/UI/";

        public override int GetPostprocessOrder() => 100;

        /// <summary>Explicit repair/validation for assets first seen before this postprocessor compiled.</summary>
        [MenuItem("Tools/Escape/Expedition/Prepare UI art kit")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play mode and wait for compilation before preparing UI art.");
            var paths = new[]
            {
                Root + "ui-icons-atlas.png", Root + "utility-icons-atlas.png", Root + "panel-frame.png", Root + "primary-button-v2.png",
                "Assets/_Project/Resources/Expedition/Art/friends-backdrop.png",
                "Assets/_Project/Resources/Expedition/Art/achievements-backdrop.png"
            };
            foreach (var path in paths)
            {
                var ui = path.StartsWith(Root);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) throw new InvalidOperationException("Texture importer is missing: " + path);
                // A two-line GUID-only .meta can inherit Cube shape. Sprite metadata
                // alone does not convert a cubemap into a sprite-producing 2D texture.
                importer.textureType = TextureImporterType.Sprite;
                importer.textureShape = TextureImporterShape.Texture2D;
                importer.spriteImportMode = ui ? SpriteImportMode.Multiple : SpriteImportMode.Single;
                importer.SaveAndReimport();
                var sprites = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().ToArray();
                var atlas = path.EndsWith("/ui-icons-atlas.png");
                var utility = path.EndsWith("/utility-icons-atlas.png");
                var expected = atlas ? 20 : utility ? 2 : 1;
                if (sprites.Length != expected)
                    throw new InvalidOperationException("UI sprite import failed: " + path + "; expected " + expected + ", got " + sprites.Length + ".");
                if (ui && !atlas && !utility && sprites[0].border == Vector4.zero)
                    throw new InvalidOperationException("UI frame has no nine-slice border: " + path);
                var resource = path.Substring(path.IndexOf("/Resources/", StringComparison.Ordinal) + 11);
                resource = resource.Substring(0, resource.Length - 4);
                var loaded = Resources.LoadAll<Sprite>(resource).Length;
                if (loaded != expected)
                    throw new InvalidOperationException("Resources sprite lookup failed after import: " + resource + "; expected " + expected + ", got " + loaded + ".");
                Debug.Log("[Expedition] UI art imported: " + resource + " = " + sprites.Length + " subassets, " + loaded + " Resources sprites, shape=" + importer.textureShape);
            }
        }

        private void OnPreprocessTexture()
        {
            var panorama = assetPath == "Assets/_Project/Resources/Expedition/Art/friends-backdrop.png" ||
                assetPath == "Assets/_Project/Resources/Expedition/Art/achievements-backdrop.png";
            if (!assetPath.StartsWith(Root) && !panorama) return;
            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.textureShape = TextureImporterShape.Texture2D;
            if (panorama)
            {
                // These are compact list-page banners, not full-screen backgrounds.
                // Run after the general art importer so they stay at 1024 x ~341.
                importer.maxTextureSize = 1024;
                importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
                {
                    name = "Android", overridden = true, maxTextureSize = 1024,
                    format = TextureImporterFormat.ASTC_6x6, compressionQuality = 80,
                    textureCompression = TextureImporterCompression.Compressed
                });
                return;
            }
            var icons = assetPath.EndsWith("/ui-icons-atlas.png");
            var utility = assetPath.EndsWith("/utility-icons-atlas.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Multiple;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.isReadable = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.maxTextureSize = icons ? 2048 : 1024;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 80;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.textureShape = TextureImporterShape.Texture2D;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = 100;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = icons ? 2048 : 1024,
                format = TextureImporterFormat.ASTC_6x6, compressionQuality = 80,
                textureCompression = TextureImporterCompression.Compressed
            });

            SpriteRect[] rects;
            if (icons) rects = IconRects();
            else if (utility) rects = UtilityIconRects();
            else if (assetPath.EndsWith("/panel-frame.png"))
                rects = new[] { Rect("panel-frame", 20, 38, 2154, 686, 724, 96) };
            else if (assetPath.EndsWith("/primary-button.png"))
                rects = new[] { Rect("primary-button", 0, 0, 2172, 724, 724, 96) };
            else if (assetPath.EndsWith("/primary-button-v2.png"))
                rects = new[] { Rect("primary-button-v2", 40, 58, 2134, 664, 724, 96) };
            else return;

            var factories = new SpriteDataProviderFactories();
            factories.Init();
            var provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            if (provider == null) return;
            provider.InitSpriteEditorDataProvider();
            provider.SetSpriteRects(rects);
            var names = new List<SpriteNameFileIdPair>(rects.Length);
            foreach (var rect in rects) names.Add(new SpriteNameFileIdPair(rect.name, rect.spriteID));
            var nameProvider = provider.GetDataProvider<ISpriteNameFileIdDataProvider>();
            nameProvider?.SetNameFileIdPairs(names);
            provider.Apply();
        }

        private static SpriteRect[] UtilityIconRects()
        {
            // Two transparent square cells in the original 1774 x 887 PNG.
            const int height = 887;
            return new[]
            {
                Rect("trophy", 0, 0, 887, 887, height),
                Rect("settings", 887, 0, 1774, 887, height)
            };
        }

        private static SpriteRect[] IconRects()
        {
            // Coordinates are deliberately fitted to the generated silhouettes,
            // rather than a uniform grid that would clip the first row's bases.
            // Inputs use PNG top-left coordinates; Unity rects use bottom-left.
            const int h = 1122;
            return new[]
            {
                Rect("home", 18, 30, 293, 316, h),
                Rect("pickaxe", 305, 45, 558, 316, h),
                Rect("anvil", 568, 45, 841, 316, h),
                Rect("swords", 853, 45, 1105, 316, h),
                Rect("hero", 1114, 35, 1384, 317, h),
                Rect("energy", 50, 320, 270, 582, h),
                Rect("gold", 295, 325, 551, 583, h),
                Rect("ore", 569, 321, 842, 582, h),
                Rect("bag", 847, 314, 1110, 583, h),
                Rect("heart", 1137, 325, 1380, 582, h),
                Rect("shield", 40, 582, 272, 834, h),
                Rect("helmet", 299, 584, 558, 834, h),
                Rect("shoulders", 558, 588, 844, 833, h),
                Rect("chest", 847, 582, 1117, 834, h),
                Rect("gloves", 1120, 590, 1386, 834, h),
                Rect("legs", 35, 833, 272, 1092, h),
                Rect("boots", 279, 839, 580, 1092, h),
                Rect("left-hand", 583, 832, 832, 1092, h),
                Rect("right-hand", 833, 834, 1108, 1092, h),
                Rect("blueprint", 1110, 835, 1394, 1092, h)
            };
        }

        private static SpriteRect Rect(string name, int x0, int y0, int x1, int y1, int sourceHeight, int border = 0)
        {
            string id;
            using (var hash = MD5.Create())
                id = System.BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes("Expedition/UI/" + name))).Replace("-", "").ToLowerInvariant();
            return new SpriteRect
            {
                name = name, spriteID = new GUID(id),
                rect = new Rect(x0, sourceHeight - y1, x1 - x0, y1 - y0),
                alignment = SpriteAlignment.Center, pivot = new Vector2(.5f, .5f),
                border = new Vector4(border, border, border, border)
            };
        }
    }
}
