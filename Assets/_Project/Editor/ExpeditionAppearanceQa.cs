using System;
using System.Collections.Generic;
using Project.Character;
using Project.Character.UI;
using Project.Nakama;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Validates real imported art and the production preview without changing account preferences.</summary>
    public static class ExpeditionAppearanceQa
    {
        [MenuItem("Tools/Expedition/Validate Hero Appearances")]
        public static void Validate() => Debug.Log(Run());

        public static string Run()
        {
            Require(CharacterAppearance.Themes.Count == 10, "Ten appearance themes are required.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            var assets = 0;
            for (var theme = 0; theme < CharacterAppearance.Themes.Count; theme++)
            for (var gender = 0; gender < 2; gender++)
            {
                var id = CharacterAppearance.Id(theme, gender == 1);
                Require(unique.Add(id) && CharacterAppearance.IsValidId(id), "Duplicate or invalid appearance ID: " + id);
                for (var avatar = 0; avatar < 2; avatar++)
                {
                    var sprite = CharacterAppearance.Load(id, avatar == 1);
                    Require(sprite != null, "Missing imported sprite: " + id + (avatar == 1 ? "-avatar" : "-body"));
                    var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(sprite)) as TextureImporter;
                    Require(importer != null && importer.textureType == TextureImporterType.Sprite && importer.spriteImportMode == SpriteImportMode.Single,
                        "Expected a single UI sprite: " + id);
                    Require(sprite.rect.width >= 256 && sprite.rect.height >= 256, "Appearance is too small for its preview: " + id);
                    assets++;
                }
            }
            Require(!CharacterAppearance.IsValidId("../other") && !CharacterAppearance.IsValidId("dawnwarden") && !CharacterAppearance.IsValidId(null), "Unknown IDs accepted.");
            var sample = new CharacterAppearance.Selection { body = "dawnwarden-female", avatar = "voidranger-male" };
            var restored = CharacterAppearance.Parse(JsonUtility.ToJson(sample));
            Require(restored.body == sample.body && restored.avatar == sample.body, "Old independent avatar did not migrate to the skin's paired avatar.");
            Require(typeof(CharacterAppearance).GetMethod("ApplyAvatar") == null, "Independent avatar selection API remains available.");
            var invalid = CharacterAppearance.Parse("{\"version\":1,\"body\":\"missing\",\"avatar\":\"missing\"}");
            Require(invalid.body == "" && invalid.avatar == "", "Invalid saved IDs did not recover to default.");
            Require(CharacterAppearance.Parse("{\"version\":2,\"body\":\"dawnwarden-male\"}").body == "", "Unknown schema version accepted.");
            Require(CharacterAppearance.Parse("not json").body == "", "Corrupt preference did not recover.");
            Require(CharacterAppearance.ForGender("dawnwarden-female", false) == "dawnwarden-male" &&
                CharacterAppearance.ForGender("stormforged-male", true) == "stormforged-female", "Earlier opposite-gender selections were not migrated to the account gender.");
            Require(!CharacterAppearance.IsAllowedId(CharacterAppearance.Id(0, !AccountGender.IsFemale)), "Opposite-gender appearance is allowed.");
            Require(LocalAccountCacheScope.Create("https", "server-one", 443, "user-a") != LocalAccountCacheScope.Create("https", "server-one", 443, "user-b") &&
                LocalAccountCacheScope.Create("https", "server-one", 443, "user-a") != LocalAccountCacheScope.Create("https", "server-two", 443, "user-a"), "Cosmetic cache scopes overlap.");

            var beforeBody = CharacterAppearance.BodyId;
            var beforeAvatar = CharacterAppearance.AvatarId;
            var host = new GameObject("AppearanceQaPreview", typeof(RectTransform), typeof(Canvas));
            try
            {
                var panel = host.AddComponent<CharacterAppearancePanel>();
                panel.Build((RectTransform)host.transform);
                panel.Show(); panel.SelectTheme(9);
                Require(panel.PreviewId == CharacterAppearance.Id(9) && panel.IsOpen, "Body preview did not follow the account gender.");
                Require(host.transform.Find("ExpeditionAppearancePage/MaleVersion") == null && host.transform.Find("ExpeditionAppearancePage/FemaleVersion") == null,
                    "The Hero page exposes a gender selector.");
                var preview = host.transform.Find("ExpeditionAppearancePage/AppearanceBodyPreview").GetComponent<Image>();
                Require(preview.sprite == CharacterAppearance.Load(panel.PreviewId, false), "Body preview uses the wrong illustration.");
                panel.SelectTheme(2);
                Require(panel.PreviewId == CharacterAppearance.Id(2), "Skin preview selection is incorrect.");
                Require(CharacterAppearance.AvatarId == CharacterAppearance.BodyId, "Avatar does not match the selected skin.");
                var grid = host.transform.Find("ExpeditionAppearancePage/AppearanceGridViewport/AppearanceGridContent");
                Require(grid != null && grid.childCount == 10, "The scroll grid does not contain ten skins.");
                Require(CharacterAppearance.BodyId == beforeBody && CharacterAppearance.AvatarId == beforeAvatar, "Preview changed saved cosmetics.");
                panel.Hide(); Require(!panel.IsOpen, "Appearance page did not hide.");
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }
            return "PASS: 10 themes, 20 bodies + 20 paired avatars; imported sprites, paired-only avatar migration, server/account isolation, immutable gender filtering and ten-card non-mutating skin previews.";
        }

        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
