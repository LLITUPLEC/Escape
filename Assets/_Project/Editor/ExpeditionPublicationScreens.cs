using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Full production workshop/dossier views with populated local catalog data. Never enters Play mode or calls transport.</summary>
    public static class ExpeditionPublicationScreens
    {
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites, preferenceWrites;
            public string version, utc, error;
            public List<string> checks = new(), screenshots = new();
        }
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(ExpeditionGlobalBossQa0314.Run("logic"));
            Debug.Log(Run());
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            var version = PlayerSettings.bundleVersion;
            if (!System.Text.RegularExpressions.Regex.IsMatch(version, @"^\d+\.\d+\.\d+$")) throw new InvalidOperationException("Release version required.");
            var output = ".cache/qa/publication-screens/" + version; Directory.CreateDirectory(output);
            var report = new Report { version = version, utc = DateTime.UtcNow.ToString("O") };
            var originalScene = SceneManager.GetActiveScene(); var oldTexture = RenderTexture.active;
            var bootField = typeof(NakamaBootstrap).GetField("<Instance>k__BackingField", S); var oldBoot = bootField.GetValue(null);
            var genderFields = typeof(AccountGender).GetFields(S).Where(field => !field.IsInitOnly && (field.FieldType == typeof(string) || field.FieldType == typeof(bool))).ToDictionary(field => field, field => field.GetValue(null));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture target = null;
            try
            {
                bootField.SetValue(null, null); typeof(AccountGender).GetField("_scope", S).SetValue(null, null); typeof(AccountGender).GetField("_loaded", S).SetValue(null, false);
                camera = new GameObject("PublicationCamera", typeof(Camera)).GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = 960;
                camera.transform.position = new Vector3(0, 0, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var inactive = new GameObject("DisabledPublicationControllers"); inactive.SetActive(false);
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
                CaptureWorkshop(inactive.transform, catalog, camera, target, output, report);
                CaptureMine(inactive.transform, catalog, camera, target, output, report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                bootField.SetValue(null, oldBoot); foreach (var field in genderFields) field.Key.SetValue(null, field.Value);
                if (camera != null) camera.targetTexture = null; RenderTexture.active = oldTexture;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true); if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                File.WriteAllText(output + "/capture-report.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return report.checks.Count + " publication view checks passed; populated workshop and mine1080x1920 PNGs in " + Path.GetFullPath(output);
        }
        private static void CaptureWorkshop(Transform inactive, ItemCatalog catalog, Camera camera, RenderTexture target, string output, Report report)
        {
            var canvas = CanvasRoot("PublicationWorkshop", camera);
            var background = ExpeditionUi.Fill(canvas.transform, "WorkshopBackground", 0, 0, 1, 1, Color.white); background.sprite = Resources.Load<Sprite>("Expedition/Art/workshop-backdrop");
            var slots = ExpeditionUi.Rect(background.transform, "CraftSlots", 0, 0, 1, 1);
            for (var index = 0; index < 8; index++)
            {
                var slot = ExpeditionUi.Panel(slots, "Slot_" + index, 0, 0, 1, 1); slot.gameObject.AddComponent<Button>();
                Text(slot.transform, "SlotLabel"); Text(slot.transform, "Timer"); ExpeditionUi.Fill(slot.transform, "Icon", 0, 0, 1, 1, Color.white);
            }
            var owner = new GameObject("DisabledPublicationWorkshop"); owner.transform.SetParent(inactive); var controller = owner.AddComponent<WorkshopSceneController>(); controller.enabled = false;
            Set(controller, "itemCatalog", catalog); Set(controller, "workshopBackground", background.rectTransform); Set(controller, "craftSlotsRoot", slots);
            Set(controller, "applyDefaultWorkshopAnchors", false); Set(controller, "applyEquipmentStyleCraftLayout", false);
            Call(controller, "BuildExtraUiIfNeeded"); Call(controller, "WireCraftSlots");
            var layout = ExpeditionWorkshopLayout.Install(background.rectTransform, slots); Set(controller, "_expeditionLayout", layout);
            var ids = catalog.EnumerateDefinitions().Where(item => item.Kind == ItemKind.Equipment).Select(item => item.ItemId).Concat(new[] { "ingot_green", "ingot_blue", "ingot_purple", "tesseract" }).ToArray();
            var profile = new CharacterGetRpcResponse { ok = true, progression = new Progression { level = 12, gold = 72500, ore = 48600 }, equipment_def_ids = new string[8],
                inventory_def_ids = ids, inventory_counts = ids.Select(id => id.StartsWith("ingot_", StringComparison.Ordinal) ? 1000 : 1).ToArray(), inventory_capacity = 50,
                learned_recipe_ids = catalog.EnumerateDefinitions().Where(item => item.Kind == ItemKind.Recipe).Select(item => item.ItemId).ToArray(), workshop_output_def_ids = new string[8], workshop_ends_at = new int[8] };
            Set(controller, "_profile", profile); Call(controller, "ApplyProfile", profile); Call(controller, "OnSlotClicked", 4);
            var epic = catalog.Get("eq_t1_epic_legs");
            var row = ((List<GameObject>)Get(controller, "_recipeRows")).Single(item => item.name == "Recipe_" + epic.ItemId); row.GetComponent<Button>().onClick.Invoke();
            Check(Get<Button>(controller, "_createButton").interactable && Get<Text>(controller, "_detailText").text.Contains(epic.CraftIngotN.ToString()), "Workshop uses current epic recipe price, actual materials and ready-to-craft gating", report);
            Check(layout.RecipePanel.Find("PrerequisiteItem").gameObject.activeSelf, "Actual prerequisite gear row is populated", report);
            Save(canvas, camera, target, output + "/workshop.png", report); canvas.gameObject.SetActive(false);
        }
        private static void CaptureMine(Transform inactive, ItemCatalog catalog, Camera camera, RenderTexture target, string output, Report report)
        {
            var root = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("UI/MonsterModal"), inactive, false);
            var view = root.GetComponent<MonsterModalView>(); view.enabled = false; view.ApplyExpeditionStyle(); view.MonsterContentRoot.SetActive(true); view.BarrierContentRoot.SetActive(false);
            var canvas = root.GetComponent<Canvas>(); Configure(canvas, camera);
            foreach (var safe in root.GetComponentsInChildren<ExpeditionSafeArea>(true)) { safe.enabled = false; ExpeditionUi.SetRect(safe.transform as RectTransform, 0, 0, 1, 1); }
            var owner = new GameObject("DisabledPublicationMine"); owner.transform.SetParent(inactive); var controller = owner.AddComponent<MineSceneController>(); controller.enabled = false;
            Set(controller, "itemCatalog", catalog); Set(controller, "affixCatalog", Resources.Load<AffixCatalog>("Match3/AffixCatalog")); Set(controller, "_difficulty", "medium"); Set(controller, "_selectedFloor", 5);
            Set(controller, "_modalRoot", root); Set(controller, "_modalStatTexts", view.StatTexts); Set(controller, "_modalFightButton", view.FightButton); Set(controller, "_modalDismissButton", view.DismissButton);
            Set(controller, "_energySprite", ExpeditionResourceIcons.Get("energy"));
            Set(controller, "_modalMonsterRewardsTitle", view.RewardsSectionTitle); Set(controller, "_modalMonsterRewardsColumnsRoot", view.RewardsDynamicRoot); Set(controller, "_modalMonsterRewardsValueFont", ExpeditionTheme.LegacyFont);
            Set(controller, "_modalAffixIcon", view.AffixIcon); Set(controller, "_modalAffixIconText", view.AffixIconGlyph); Set(controller, "_modalAffixTitleText", view.AffixTitleText); Set(controller, "_modalAffixDescriptionText", view.AffixDescriptionText);
            var botType = typeof(MineSceneController).GetNestedType("PveBotInfo", BindingFlags.NonPublic);
            var bot = JsonUtility.FromJson(ExtractObject(File.ReadAllText("Server/nakama/data/duel_match3_bots_catalog_medium.json"), "mine_5"), botType);
            var monster = AssetDatabase.LoadAssetAtPath<MonsterDefinition>("Assets/_Project/Data/Match3/Monsters/mine_5.asset");
            view.SetExpeditionContext(monster.Icon, 5, "Средняя", false); view.TitleText.text = MonsterNames.Resolve((string)Get(bot, "name"), monster, 5);
            Call(controller, "ApplyMonsterStatTexts", 150 + (int)Get(bot, "hp_bonus"), (int)Get(bot, "base_damage"), (int)Get(bot, "base_armor"), (float)Get(bot, "base_crit"), (int)Get(bot, "start_mana"), 0);
            Call(controller, "ApplyAffixVisual", "stone_skin"); Call(controller, "PopulateMonsterRewards", bot);
            // Runtime removes these serialized labels with Destroy before the next frame.
            // An edit-only synchronous capture has no such frame; remove only its cloned labels first.
            foreach (var button in new[] { view.FightButton, view.DismissButton })
            {
                var old = button.transform.Find("Label"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject);
            }
            Call(controller, "ApplyFightButtonCosts", bot); Call(controller, "ApplyDismissButtonCosts", false, bot);
            view.SupplementalInfoText.text = ""; view.RefreshDynamicStyles(); root.SetActive(true);
            foreach (var binding in root.GetComponentsInChildren<CharacterAppearanceImage>(true)) { binding.enabled = false; var image = binding.GetComponent<Image>(); image.sprite = Resources.Load<Sprite>("Expedition/Art/hero"); image.enabled = true; }
            Check(view.RewardsDynamicRoot.childCount > 0 && view.RewardsDynamicRoot.GetComponentsInChildren<Image>().Count(image => image.sprite != null) >= 4, "Mine rewards are populated through the real controller using the shipped medium bot catalog", report);
            Check(view.AffixIcon.sprite != null && !view.AffixIconGlyph.gameObject.activeSelf && view.AffixTitleText.text.Contains("Каменная кожа"), "Mine affix uses actual catalog icon/title/description, without a placeholder glyph", report);
            Save(canvas, camera, target, output + "/mine.png", report); root.SetActive(false);
        }
        private static string ExtractObject(string json, string key)
        {
            var start = json.IndexOf('{', json.IndexOf('"' + key + '"', StringComparison.Ordinal)); var depth = 0; var quoted = false; var escaped = false;
            for (var index = start; index < json.Length; index++)
            {
                var ch = json[index]; if (quoted) { if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == '"') quoted = false; continue; }
                if (ch == '"') quoted = true; else if (ch == '{') depth++; else if (ch == '}' && --depth == 0) return json.Substring(start, index - start + 1);
            }
            throw new InvalidOperationException("Catalog object missing: " + key);
        }
        private static Canvas CanvasRoot(string name, Camera camera) { var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); var canvas = root.GetComponent<Canvas>(); Configure(canvas, camera); return canvas; }
        private static void Configure(Canvas canvas, Camera camera)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var scaler = ExpeditionUi.Ensure<CanvasScaler>(canvas.gameObject); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1;
            Canvas.ForceUpdateCanvases();
        }
        private static void Save(Canvas canvas, Camera camera, RenderTexture target, string path, Report report)
        {
            foreach (var transform in canvas.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases(); foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0); pixels.Apply(); File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
        private static Text Text(Transform parent, string name) { var text = ExpeditionUi.Rect(parent, name, 0, 0, 1, 1).gameObject.AddComponent<Text>(); text.font = ExpeditionTheme.LegacyFont; return text; }
        private static object Get(object owner, string name) => owner.GetType().GetField(name, I).GetValue(owner);
        private static T Get<T>(object owner, string name) => (T)Get(owner, name);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, I).SetValue(owner, value);
        private static object Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, I).Invoke(owner, args);
        private static void Check(bool value, string message, Report report) { if (!value) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
