using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Match3;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Actual catalog, manager, header crop and scene-figure binding; no gameplay or account transport.</summary>
    public static class ExpeditionMonsterAvatarQa0318
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const string Output = ".cache/qa/release0318/avatars";
        [Serializable] private sealed class FloorCheck { public int floor; public string body, avatar; public Rect headerUv; }
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites, assetWrites;
            public string error, screenshot;
            public List<string> checks = new();
            public List<FloorCheck> floors = new();
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Monster avatar QA requires Edit mode.");
            Directory.CreateDirectory(Output);
            var report = new Report();
            var previousScene = SceneManager.GetActiveScene(); var previousRenderTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RenderTexture target = null; Texture2D pixels = null;
            try
            {
                var catalog = AssetDatabase.LoadAssetAtPath<MonsterCatalog>("Assets/_Project/Data/Match3/Monsters/MainMonsterCatalog.asset");
                var hero = Resources.Load<Sprite>("Expedition/Art/hero");
                Require(catalog != null && hero != null, "Production monster catalog and common PvP body exist", report);
                var bodies = Enumerable.Range(1, 12).ToDictionary(floor => floor, floor => catalog.GetByFloor(floor)?.Icon);
                var avatars = new HashSet<Sprite>();
                var managerRoot = new GameObject("InactiveMonsterAvatarManager"); managerRoot.SetActive(false);
                var manager = managerRoot.AddComponent<DuelMatch3Manager>(); manager.enabled = false;
                Set(manager, "presentationMonsterCatalog", catalog); Set(manager, "_isGlobalBoss", false);
                Set(manager, "_isSoloBotMode", true); Set(manager, "_expeditionHeroPortrait", hero);

                const int width = 1440, height = 1800;
                var camera = new GameObject("MonsterAvatarQaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color32(8, 20, 27, 255);
                camera.orthographic = true; camera.orthographicSize = height / 2f; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.transform.position = new Vector3(0, 0, -10);
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = new GameObject("MonsterAvatarQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                ExpeditionUi.Text(canvas.transform, "Heading", "АВАТАРКИ МОНСТРОВ · ПОРТРЕТ И ПОЛНЫЙ ОБРАЗ", 27,
                    .02f, .955f, .98f, .993f, TextAlignmentOptions.Center);
                Match3PlayerPanel finalPanel = null; ExpeditionCombatantFigure finalFigure = null;
                for (var floor = 1; floor <= 12; floor++)
                {
                    var definition = catalog.GetByFloor(floor);
                    Require(definition != null && definition.Floor == floor && definition.Icon != null && definition.Avatar != null,
                        "Floor " + floor + " has a dedicated body and avatar", report);
                    Require(definition.Avatar != definition.Icon && avatars.Add(definition.Avatar),
                        "Floor " + floor + " avatar is independent and unique", report);
                    var avatarPath = AssetDatabase.GetAssetPath(definition.Avatar);
                    var expectedPath = "Assets/_Project/Resources/Expedition/Monsters/Avatars/mine-" + floor + "-avatar-v1.png";
                    Require(avatarPath == expectedPath && definition.Avatar.rect.width == definition.Avatar.rect.height,
                        "Floor " + floor + " binds its authored square avatar asset", report);
                    Require(AssetDatabase.GetAssetPath(definition.Icon) == "Assets/_Project/Resources/Expedition/Monsters/mine-" + floor + ".png",
                        "Floor " + floor + " full-body reference is unchanged", report);

                    var column = (floor - 1) % 4; var row = (floor - 1) / 4;
                    var cell = ExpeditionUi.Panel(canvas.transform, "Floor" + floor, .008f + column * .25f, .015f + (2 - row) * .31f,
                        .242f + column * .25f, .318f + (2 - row) * .31f, new Color32(18, 38, 46, 255));
                    var panel = cell.gameObject.AddComponent<Match3PlayerPanel>();
                    ExpeditionUi.Text(cell.transform, "Label", floor + " · " + definition.DisplayName, 22, .025f, .91f, .975f, .99f, TextAlignmentOptions.Center);
                    var source = ExpeditionUi.Panel(cell.transform, "SourceBodyForHeader", .08f, .45f, .92f, .90f, Color.white);
                    source.preserveAspect = true; panel.avatarImage = source;
                    var crop = source.gameObject.AddComponent<ExpeditionBattlePortrait>(); crop.Bind(source);
                    var figureImage = ExpeditionUi.Panel(cell.transform, "FullBodySceneFigure", .12f, .025f, .88f, .435f, Color.white);
                    figureImage.preserveAspect = true;
                    var figure = figureImage.gameObject.AddComponent<ExpeditionCombatantFigure>();
                    Set(manager, "_opPanel", panel); Set(manager, "_activePveBotFloor", floor);
                    Call(manager, "ApplyExpeditionPortraits"); figure.Bind(source, false);
                    var header = source.transform.Find("ExpeditionHeadCrop")?.GetComponent<RawImage>();
                    Require(source.sprite == definition.Icon && !source.enabled && figureImage.sprite == definition.Icon,
                        "Floor " + floor + " manager preserves Image and live scene-figure body sprites", report);
                    Require(header != null && header.enabled && header.texture == definition.Avatar.texture,
                        "Floor " + floor + " visible RawImage uses the dedicated avatar texture", report);
                    var expected = AvatarUv(definition.Avatar);
                    Require(Near(header.uvRect, expected), "Floor " + floor + " header uses authored portrait UV instead of body head crop", report);
                    Call(manager, "ApplyExpeditionPortraits"); Set(figure, "_nextFrame", 0f); Call(figure, "Update");
                    Require(source.sprite == bodies[floor] && figureImage.sprite == bodies[floor] && header.texture == definition.Avatar.texture,
                        "Floor " + floor + " repeat refresh cannot replace the full body with its portrait", report);
                    report.floors.Add(new FloorCheck { floor = floor, body = AssetDatabase.GetAssetPath(source.sprite), avatar = avatarPath, headerUv = header.uvRect });
                    finalPanel = panel; finalFigure = figure;
                }
                Canvas.ForceUpdateCanvases(); LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)canvas.transform); Canvas.ForceUpdateCanvases();
                camera.Render(); RenderTexture.active = target;
                pixels = new Texture2D(width, height, TextureFormat.RGBA32, false); pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0); pixels.Apply();
                report.screenshot = Output + "/monster-avatars-contact-sheet.png";
                File.WriteAllBytes(report.screenshot, pixels.EncodeToPNG());
                Require(new FileInfo(report.screenshot).Length > 40000, "Actual Unity camera renders the twelve portrait/body pairs", report);

                // Exercise the same bound header and figure as their manager changes presentation modes.
                Set(manager, "_opPanel", finalPanel); Set(manager, "_isSoloBotMode", false); Set(manager, "_remoteAppearance", null);
                Call(manager, "ApplyExpeditionPortraits"); Set(finalFigure, "_nextFrame", 0f); Call(finalFigure, "Update");
                var finalHeader = finalPanel.avatarImage.transform.Find("ExpeditionHeadCrop").GetComponent<RawImage>();
                Require(finalPanel.avatarImage.sprite == hero && finalFigure.GetComponent<Image>().sprite == hero && finalHeader.texture == hero.texture,
                    "Switch to PvP replaces header and figure with hero art without a stale monster avatar", report);
                Set(manager, "_isSoloBotMode", true); Set(manager, "_activePveBotFloor", 1);
                Call(manager, "ApplyExpeditionPortraits"); Set(finalFigure, "_nextFrame", 0f); Call(finalFigure, "Update");
                Require(finalPanel.avatarImage.sprite == bodies[1] && finalFigure.GetComponent<Image>().sprite == bodies[1] &&
                    finalHeader.texture == catalog.GetByFloor(1).Avatar.texture && Near(finalHeader.uvRect, AvatarUv(catalog.GetByFloor(1).Avatar)),
                    "Switch back to mine selects the new floor avatar and its complete body", report);
                Require(bodies.All(pair => catalog.GetByFloor(pair.Key).Icon == pair.Value), "All twelve catalog full-body sprites remain unchanged", report);
                report.passed = true;
                return "PASS monster avatars: " + report.checks.Count + " asset/manager/header/figure checks and Unity contact sheet.";
            }
            catch (Exception error) { report.error = error.ToString(); throw; }
            finally
            {
                RenderTexture.active = previousRenderTarget;
                EditorSceneManager.CloseScene(scene, true);
                if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                File.WriteAllText(Output + "/report.json", JsonUtility.ToJson(report, true));
            }
        }
        private static Rect AvatarUv(Sprite sprite)
        {
            var region = sprite.textureRect;
            return new Rect((region.x + region.width * .04f) / sprite.texture.width,
                (region.y + region.height * .04f) / sprite.texture.height,
                region.width * .92f / sprite.texture.width, region.height * .92f / sprite.texture.height);
        }
        private static bool Near(Rect a, Rect b) => Mathf.Abs(a.x - b.x) < .00001f && Mathf.Abs(a.y - b.y) < .00001f &&
            Mathf.Abs(a.width - b.width) < .00001f && Mathf.Abs(a.height - b.height) < .00001f;
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        private static void Call(object target, string method) => target.GetType().GetMethod(method, Hidden).Invoke(target, null);
        private static void Require(bool value, string message, Report report)
        { if (!value) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
