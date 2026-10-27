using System;
using System.IO;
using Project.Character;
using UnityEditor;
using UnityEngine;

namespace Project.Editor
{
    /// <summary>Imports the completed equipment set and keeps catalog bindings reproducible.</summary>
    public sealed class ExpeditionItemMediaImporter : AssetPostprocessor
    {
        private const string ArtRoot = "Assets/_Project/Resources/Expedition/Items/";
        private const string DataRoot = "Assets/_Project/Data/Character/Items/";
        private static readonly string[] Qualities = { "epic", "legendary" };
        private static readonly string[] Slots = { "helmet", "shoulders", "chest", "gloves", "legs", "feet", "weapon_l", "weapon_r" };

        private void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(ArtRoot, StringComparison.Ordinal)) return;
            var importer = (TextureImporter)assetImporter;
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
            importer.maxTextureSize = 512;
            importer.textureCompression = TextureImporterCompression.Compressed;
            importer.compressionQuality = 80;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spritePixelsPerUnit = 100;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings
            {
                name = "Android", overridden = true, maxTextureSize = 512,
                format = TextureImporterFormat.ASTC_6x6,
                textureCompression = TextureImporterCompression.Compressed, compressionQuality = 80
            });
        }

        [MenuItem("Tools/Escape/Expedition/Prepare item media")]
        public static void Prepare()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Stop Play mode and wait for compilation before preparing item media.");
            foreach (var file in Directory.GetFiles(ArtRoot, "*.png"))
                AssetDatabase.ImportAsset(file.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);

            foreach (var quality in Qualities)
            foreach (var slot in Slots)
            {
                var id = "eq_t1_" + quality + "_" + slot;
                var def = AssetDatabase.LoadAssetAtPath<ItemDefinition>(DataRoot + id + ".asset");
                if (def == null) throw new InvalidOperationException("Item definition is missing: " + id);
                Assign(def, "icon", RequireSprite(id));
            }
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(DataRoot + "MainItemCatalog.asset");
            if (catalog == null) throw new InvalidOperationException("Main item catalog is missing.");
            Assign(catalog, "missingItemIcon", RequireSprite("supply-cache"));
            AssetDatabase.SaveAssets();
            Debug.Log(Validate());
        }

        public static string Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>(DataRoot + "MainItemCatalog.asset");
            if (catalog == null) throw new InvalidOperationException("Main item catalog is missing.");
            var count = 0;
            foreach (var def in catalog.EnumerateDefinitions())
            {
                if (def.Icon == null) throw new InvalidOperationException("Item has no sprite: " + def.ItemId);
                count++;
            }
            foreach (var quality in Qualities)
            foreach (var slot in Slots)
            {
                var id = "eq_t1_" + quality + "_" + slot;
                var def = catalog.Get(id);
                if (def == null || def.Icon != RequireSprite(id))
                    throw new InvalidOperationException("Equipment sprite binding differs from its dedicated art: " + id);
            }
            var fallback = new SerializedObject(catalog).FindProperty("missingItemIcon");
            if (fallback == null || fallback.objectReferenceValue != RequireSprite("supply-cache"))
                throw new InvalidOperationException("Neutral supply-cache fallback is missing.");
            return "PASS item media: " + count + " item definitions have sprites; 16 dedicated epic/legendary icons and neutral fallback are bound.";
        }

        private static Sprite RequireSprite(string id)
        {
            var path = ArtRoot + id + ".png";
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null) throw new InvalidOperationException("Item sprite did not import: " + path);
            return sprite;
        }

        private static void Assign(UnityEngine.Object target, string property, Sprite sprite)
        {
            var serialized = new SerializedObject(target);
            var field = serialized.FindProperty(property);
            if (field == null) throw new InvalidOperationException("Missing serialized field: " + property);
            if (field.objectReferenceValue == sprite) return;
            field.objectReferenceValue = sprite;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
