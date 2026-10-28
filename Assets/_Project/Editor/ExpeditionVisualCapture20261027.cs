using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
using Project.Match3;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Native 1080x1920 renders of production widgets in an isolated edit-mode fixture. No account/network lifecycle.</summary>
    public static class ExpeditionVisualCapture20261027
    {
        private const string Output = ".cache/qa/mine-refresh/ui";
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites;
            public int width = 1080, height = 1920;
            public List<string> checks = new(), screenshots = new();
        }

        public static void RunBatch()
        {
            Directory.CreateDirectory(Output);
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            var report = new Report();
            foreach (var path in new[] { "Assets/_Project/Resources/Expedition/Icons/health-red.png", "Assets/_Project/Resources/Expedition/Icons/healing-cross.png" })
            {
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Check(importer.maxTextureSize == 256 && importer.GetPlatformTextureSettings("Android").maxTextureSize == 256 &&
                    importer.GetPlatformTextureSettings("Android").format == TextureImporterFormat.ASTC_6x6, "Stat icons must import at256 ASTC6x6 after generic importer.");
            }
            report.checks.Add(ExpeditionItemInformationQa.Run());
            report.checks.Add(ExpeditionVisualConsistencyQa.Run());
            report.checks.Add(ExpeditionBattleEntryQa.Run());
            ExpeditionArenaBracketQa.RunAvatarsBatch();
            report.checks.Add("Tournament avatar/future-slot production helper passed.");
            var monsters = AssetDatabase.LoadAssetAtPath<MonsterCatalog>("Assets/_Project/Data/Match3/Monsters/MainMonsterCatalog.asset");
            for (var floor = 1; floor <= 12; floor++)
            {
                var monster = monsters.GetByFloor(floor);
                Check(monster != null && monster.Icon == Resources.Load<Sprite>("Expedition/Monsters/mine-" + floor), "New monster sprite binding: " + floor);
                Check(ExpeditionCombatantGrounding.TryGet(monster.Icon, out _), "New monster grounding metadata: " + floor);
            }
            var items = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
            Check(items.Get("tesseract").Icon == Resources.Load<Sprite>("Expedition/Items/tesseract"), "Tesseract inventory binding differs from canonical art.");
            Check(!File.Exists("Assets/_Project/img/items/tesser.png") && !File.Exists("Assets/_Project/img/resources_hud/tesseract.png"), "Legacy tesseract texture remains.");
            foreach (var sceneName in new[] { "MineScene", "DuelMatch3", "ArenaMenu" })
                Check(File.ReadAllText("Assets/_Project/Scenes/" + sceneName + ".unity").Contains("guid: 5bf4d0b3d4221c3448e19cb7dcb46915"), "HUD/reward tesseract reference did not migrate: " + sceneName);
            report.checks.Add("All12 new monster paths and grounding metadata; canonical tesseract shared by inventory and scene HUD/reward references; obsolete textures absent.");
            report.checks.Add(ExpeditionCombatantGroundingQa.RunTo(".cache/qa/mine-refresh/grounding"));
            Capture(report, monsters, items);
            report.passed = true;
            File.WriteAllText(Output + "/native-report.json", JsonUtility.ToJson(report, true));
            Debug.Log("PASS native visuals: " + JsonUtility.ToJson(report));
        }

        private static void Capture(Report report, MonsterCatalog monsters, ItemCatalog items)
        {
            var original = SceneManager.GetActiveScene();
            var previousTexture = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            RenderTexture texture = null;
            Camera captureCamera = null;
            try
            {
                var camera = new GameObject("VisualQaCamera", typeof(Camera)).GetComponent<Camera>();
                captureCamera = camera;
                camera.orthographic = true; camera.orthographicSize = 960;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel;
                camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.transform.position = new Vector3(0, 0, -10); camera.cullingMask = 1 << 31;
                texture = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32); texture.Create(); camera.targetTexture = texture;
                var canvas = new GameObject("VisualQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                Hero(canvas, camera, texture, scene, report, items);
                Clear(canvas.transform);
                Battle(canvas, camera, texture, scene, report, monsters);
                Clear(canvas.transform);
                Mine(canvas, camera, texture, report, monsters);
            }
            finally
            {
                RenderTexture.active = previousTexture;
                if (captureCamera != null) captureCamera.targetTexture = null;
                if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }

        private static void Hero(Canvas canvas, Camera camera, RenderTexture texture, Scene scene, Report report, ItemCatalog items)
        {
            var host = ExpeditionUi.Rect(canvas.transform, "HeroPage", 0, .07f, 1, .94f);
            var clone = Clone("CharacterUI/CharacterHudOverlay", host, scene);
            foreach (var controller in clone.GetComponentsInChildren<CharacterScreenController>(true)) UnityEngine.Object.DestroyImmediate(controller);
            foreach (var controller in clone.GetComponentsInChildren<CharacterSheetDragController>(true)) UnityEngine.Object.DestroyImmediate(controller);
            foreach (var controller in clone.GetComponentsInChildren<CharacterNicknameEditor>(true)) UnityEngine.Object.DestroyImmediate(controller);
            var view = clone.GetComponentInChildren<CharacterScreenView>(true);
            view.EnsureBuilt();
            var layout = (CharacterExpeditionLayout)typeof(CharacterScreenView).GetMethod("ApplyExpeditionLayout", Hidden).Invoke(view, new object[] { null, null, null, null });
            view.AttachToShell(host); view.gameObject.SetActive(true); view.SetVisible(true);
            foreach (var group in view.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
            foreach (var nested in view.GetComponentsInChildren<Canvas>(true)) { nested.overrideSorting = false; nested.worldCamera = camera; }
            var equipment = new[] { "helmet", "shoulders", "chest", "gloves", "legs", "feet", "weapon_l", "weapon_r" }.Select(slot => "eq_t1_epic_" + slot).ToArray();
            var inventory = new string[25]; var counts = new int[25];
            for (var i = 0; i < 8; i++) { inventory[i] = equipment[i].Replace("epic", "legendary"); counts[i] = 1; }
            inventory[8] = "tesseract"; counts[8] = 2;
            view.ApplyCharacterResponse(new CharacterGetRpcResponse
            { ok = true, inventory_capacity = 25, equipment_def_ids = equipment, inventory_def_ids = inventory, inventory_counts = counts,
                stats = new StatsMap { hp = 8240, damage = 925, armor = 384, healing = 168, crit_chance = .125f } }, items);
            foreach (var binding in view.GetComponentsInChildren<CharacterAppearanceImage>(true)) binding.enabled = false;
            var hero = ExpeditionUi.Find(view.transform, "ExpeditionHero")?.GetComponent<Image>();
            if (hero != null) { hero.sprite = Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body"); hero.enabled = true; }
            layout.SelectHeroTab(0); layout.RefreshLayout();
            Canvas.ForceUpdateCanvases();
            typeof(CharacterExpeditionLayout).GetMethod("ApplyGeometry", Hidden)?.Invoke(layout, null);
            Title(canvas.transform, "ГЕРОЙ · ОБЩИЕ ИКОНКИ ХАРАКТЕРИСТИК");
            Save(canvas, camera, texture, "hero-stats", report);
            var icons = view.GetComponentsInChildren<Image>(true).Where(image => image.name == "Icon" && image.transform.parent.name.StartsWith("Stat", StringComparison.Ordinal)).ToArray();
            Check(icons.Length == 5 && icons.Any(image => image.sprite == ExpeditionArt.Icon("stat-health")) && icons.Any(image => image.sprite == ExpeditionArt.Icon("stat-healing")), "Rendered Hero must contain both distinct health/healing icons.");
            if (clone != null && !clone.transform.IsChildOf(host)) UnityEngine.Object.DestroyImmediate(clone);
        }

        private static void Battle(Canvas canvas, Camera camera, RenderTexture texture, Scene scene, Report report, MonsterCatalog monsters)
        {
            var panels = new Match3PlayerPanel[2];
            for (var i = 0; i < 2; i++)
            {
                panels[i] = Clone("Match3/Match3PlayerPanel", canvas.transform, scene).GetComponent<Match3PlayerPanel>();
                ((RectTransform)panels[i].transform).anchoredPosition = new Vector2(i == 0 ? -200 : 200, 0);
                typeof(DuelMatch3Manager).GetMethod("EnsureCombatWidgets", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { panels[i] });
            }
            var board = Clone("Match3/Match3BoardView", canvas.transform, scene).GetComponent<Match3BoardView>();
            var hud = Clone("Match3/Match3GameHUD", canvas.transform, scene).GetComponent<Match3GameHUD>();
            Clone("Match3/Match3AbilityPanel", canvas.transform, scene);
            ExpeditionBattleLayout.Install(scene);
            board.Build();
            var logic = new Match3BoardLogic(); logic.FromArray(Enumerable.Range(0, 36).Select(i => (i + i / 6) % 5).ToArray()); board.RefreshAll(logic);
            var hero = Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body");
            panels[0].avatarImage.sprite = hero; panels[1].avatarImage.sprite = monsters.GetByFloor(4).Icon;
            for (var i = 0; i < panels.Length; i++)
            {
                ExpeditionBattlePortrait.MarkFullBody(panels[i].avatarImage);
                panels[i].SetPlayerName(i == 0 ? "Скиталец" : monsters.GetByFloor(4).DisplayName);
                panels[i].UpdateStats(i == 0 ? 8240 : 11250, i == 0 ? 8240 : 11250, 80, 100);
                panels[i].UpdateCombatStats(i == 0 ? 925 : 1400, i == 0 ? 384 : 550, i == 0 ? 168 : 90, i == 0 ? 12.5f : 8);
                panels[i].UpdateAvatarLevel(i == 0 ? 12 : 4);
                panels[i].GetComponent<ExpeditionCombatStatsPopover>().OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
            }
            hud.SetRoundNumber(7);
            hud.turnText.text = "ВАШ ХОД"; hud.timerText.text = "30";
            if (hud.timerPhaseText != null) hud.timerPhaseText.text = string.Empty;
            foreach (var figure in canvas.GetComponentsInChildren<ExpeditionCombatantFigure>(true))
            {
                var player = figure.name == "ExpeditionPlayerFigure";
                figure.BindPreview(player ? hero : monsters.GetByFloor(4).Icon, player);
                Check(player ? figure.transform.localScale.x < 0 : figure.transform.localScale.x > 0, "Hero and new monster must face each other.");
            }
            Canvas.ForceUpdateCanvases();
            foreach (var figure in canvas.GetComponentsInChildren<ExpeditionCombatantFigure>(true)) figure.RefreshGrounding();
            ExpeditionBattleLayout.SetMineDifficulty(panels[1], "hard");
            Save(canvas, camera, texture, "battle-stats-hard", report);
        }

        private static void Mine(Canvas canvas, Camera camera, RenderTexture texture, Report report, MonsterCatalog monsters)
        {
            var background = ExpeditionUi.Fill(canvas.transform, "MineBackground", 0, 0, 1, 1, Color.white);
            background.sprite = Resources.Load<Sprite>("Expedition/Art/mine-backdrop");
            var tabs = ExpeditionUi.Rect(background.transform, "DifficultyTabs", .1f, .88f, .9f, .94f);
            var group = tabs.gameObject.AddComponent<HorizontalLayoutGroup>(); group.childForceExpandHeight = group.childForceExpandWidth = true;
            foreach (var difficulty in new[] { "easy", "medium", "hard" })
                ExpeditionUi.Button(tabs, "Diff_" + difficulty, difficulty == "easy" ? "ЛЁГКАЯ" : difficulty == "medium" ? "СРЕДНЯЯ" : "ТЯЖЁЛАЯ", 0, 0, 1, 1, null);
            var mine = ExpeditionMinePresentation.Install(background.transform, _ => { }, _ => { });
            for (var floor = 1; floor <= 12; floor++)
                mine.SetFloor(new ExpeditionMinePresentation.FloorState
                { floor = floor, hasCatalog = true, unlocked = true, boss = floor % 4 == 0, portrait = monsters.GetByFloor(floor).Icon,
                    name = monsters.GetByFloor(floor).DisplayName, rewards = "Руда 40 · Золото 65" });
            mine.SetProgress(12, true);
            Canvas.ForceUpdateCanvases(); mine.ReflowRoute(); mine.SelectFloor(4, false);
            typeof(ExpeditionMinePresentation).GetMethod("SnapRouteToFloor", Hidden).Invoke(mine, new object[] { 4 });
            foreach (var difficulty in new[] { "easy", "medium", "hard" })
            {
                ExpeditionMinePresentation.ApplyDifficultyStyle(tabs, difficulty);
                Title(canvas.transform, "ШАХТА · " + (difficulty == "easy" ? "ЛЁГКАЯ" : difficulty == "medium" ? "СРЕДНЯЯ" : "ТЯЖЁЛАЯ"));
                Save(canvas, camera, texture, "mine-" + difficulty, report);
            }
        }

        private static GameObject Clone(string prefab, Transform parent, Scene scene)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/" + prefab + ".prefab");
            Check(source != null, "Missing production prefab: " + prefab);
            var clone = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            clone.transform.SetParent(parent, false); clone.SetActive(true); return clone;
        }

        private static void Title(Transform canvas, string title)
        {
            var previous = canvas.Find("QaTitle"); if (previous != null) UnityEngine.Object.DestroyImmediate(previous.gameObject);
            ExpeditionUi.Text(canvas, "QaTitle", title, 27, .025f, .947f, .975f, .989f, TextAlignmentOptions.Center).color = ExpeditionTheme.Amber;
        }

        private static void Save(Canvas canvas, Camera camera, RenderTexture texture, string name, Report report)
        {
            foreach (var transform in canvas.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            foreach (var portrait in canvas.GetComponentsInChildren<ExpeditionBattlePortrait>(true)) typeof(ExpeditionBattlePortrait).GetMethod("Refresh", Hidden).Invoke(portrait, new object[] { true });
            foreach (var aura in canvas.GetComponentsInChildren<ExpeditionDifficultyAura>(true))
                if (aura.enabled) typeof(ExpeditionDifficultyAura).GetMethod("Refresh", Hidden).Invoke(aura, null);
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render();
            RenderTexture.active = texture;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0); pixels.Apply();
                var samples = new HashSet<int>(); var data = pixels.GetPixels32();
                for (var i = 0; i < data.Length; i += 997) { var c = data[i]; samples.Add((c.r << 16) | (c.g << 8) | c.b); }
                Check(samples.Count > 32, "Blank native render: " + name);
                var path = Output + "/" + name + ".png"; File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static void Clear(Transform canvas) { while (canvas.childCount > 0) UnityEngine.Object.DestroyImmediate(canvas.GetChild(0).gameObject); }
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
