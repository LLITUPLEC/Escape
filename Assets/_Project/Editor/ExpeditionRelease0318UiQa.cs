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
    /// <summary>Actual catalog/modal/controllers against server-produced fixtures; edit mode without account or publication actions.</summary>
    public static class ExpeditionRelease0318UiQa
    {
        private const string Output = ".cache/qa/release0318/ui";
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
        [Serializable] private sealed class Fixtures { public CharacterAppearanceService.State male, female, male_selected, female_selected, male_all, female_all; }
        [Serializable] private sealed class Report { public bool passed, networkActions, accountWrites, preferenceWrites; public string error; public List<string> checks = new(), screenshots = new(); }
        public static void RunBatch() { EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single); Debug.Log(Run()); }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            Directory.CreateDirectory(Output); var report = new Report();
            var original = SceneManager.GetActiveScene(); var oldTexture = RenderTexture.active;
            var saved = new Dictionary<FieldInfo, object>();
            Save(saved, typeof(NakamaBootstrap), "<Instance>k__BackingField");
            Save(saved, typeof(AccountGender), "_scope", "_loaded", "_female", "_canChoose", "_unsupported", "Changed");
            Save(saved, typeof(CharacterAppearance), "_scope", "_loaded", "_female", "_genderLoaded", "_current", "Changed");
            Save(saved, typeof(CharacterAppearanceService), "_scope", "_loaded", "_saving", "_state", "_lastError", "Changed", "UnlockRewardsGranted");
            try
            {
                Set(typeof(NakamaBootstrap), "<Instance>k__BackingField", null);
                foreach (var type in new[] { typeof(AccountGender), typeof(CharacterAppearance), typeof(CharacterAppearanceService) }) Set(type, "Changed", null);
                Set(typeof(CharacterAppearanceService), "UnlockRewardsGranted", null);
                var fixtures = JsonUtility.FromJson<Fixtures>(File.ReadAllText("Assets/_Project/Editor/Fixtures/appearance-server-0318.json"));
                VerifyWire(fixtures, report);
                foreach (var dimensions in new[] { new Vector2Int(1080, 1920), new Vector2Int(1080, 2400), new Vector2Int(1440, 2560) }) Profile(dimensions, fixtures, report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                foreach (var pair in saved) pair.Key.SetValue(null, pair.Value);
                RenderTexture.active = oldTexture;
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                File.WriteAllText(Output + "/native-ui.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return report.checks.Count + " production UI checks and " + report.screenshots.Count + " native captures; no network or account writes.";
        }
        private static void VerifyWire(Fixtures fixtures, Report report)
        {
            var accept = typeof(CharacterAppearanceService).GetMethod("Accept", S);
            foreach (var state in new[] { fixtures.male, fixtures.female, fixtures.male_all, fixtures.female_all })
            {
                Seed(state.gender == "female", state);
                Check(state.unlock_reward_schema == 2 && (bool)accept.Invoke(null, new object[] { state }), "Production client accepts actual v2 server rewards: " + state.gender + "/" + state.unlocked_prestige_count, report);
                var altered = JsonUtility.FromJson<CharacterAppearanceService.State>(JsonUtility.ToJson(state)); altered.passive_damage_bonus_percent++;
                Check(!(bool)accept.Invoke(null, new object[] { altered }), "Forged collection damage total is rejected: " + state.gender, report);
                altered = JsonUtility.FromJson<CharacterAppearanceService.State>(JsonUtility.ToJson(state)); altered.themes.Single(t => t.id == "alchemist").unlock_reward_tesseract++;
                Check(!(bool)accept.Invoke(null, new object[] { altered }), "Forged per-skin tesseract reward is rejected: " + state.gender, report);
            }
            Seed(false, fixtures.male_all);
            Check(CharacterAppearanceService.FormatThemeBonus(CharacterAppearanceService.GetTheme("stormforged")).Contains("п.п.")
                && CharacterAppearanceService.FormatThemeReward(CharacterAppearanceService.GetTheme("alchemist")).Contains("тессеракт")
                && CharacterAppearanceService.FormatThemeBonus(CharacterAppearanceService.GetTheme("alchemist")).Contains("300 лечения"), "Actual v2 formatter preserves critical points, tesseract reward and flat healing", report);
        }
        private static void Profile(Vector2Int dimensions, Fixtures fixtures, Report report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture target = null;
            try
            {
                camera = new GameObject("Release0318UiCamera", typeof(Camera)).GetComponent<Camera>(); camera.orthographic = true; camera.orthographicSize = dimensions.y * .5f;
                camera.transform.position = new Vector3(0, 0, -10); camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.cullingMask = 1 << 31;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel;
                target = new RenderTexture(dimensions.x, dimensions.y, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = CanvasRoot("Release0318Canvas", camera);
                var bg = ExpeditionUi.Fill(canvas.transform, "Backdrop", 0, 0, 1, 1, Color.white); bg.sprite = Resources.Load<Sprite>("Expedition/Art/character-backdrop");
                var safe = ExpeditionUi.Rect(canvas.transform, "SafeArea", 18f / dimensions.x, 40f / dimensions.y, 1 - 18f / dimensions.x, 1 - 80f / dimensions.y);
                var host = ExpeditionUi.Rect(safe, "HeroHost", 0, .105f, 1, .922f);
                var panel = host.gameObject.AddComponent<CharacterAppearancePanel>(); panel.Build(host);
                var suffix = dimensions.x + "x" + dimensions.y;
                foreach (var female in new[] { false, true })
                {
                    Seed(female, female ? fixtures.female : fixtures.male); panel.Show(); Flush(canvas); panel.RefreshGridLayout();
                    var cards = Get<Button[]>(panel, "_cards");
                    Check(!panel.DetailsOpen && panel.GridColumns >= 3 && Get<RectTransform>(panel, "_viewport").anchorMax.y - Get<RectTransform>(panel, "_viewport").anchorMin.y > .7f,
                        "Catalog is the main content in three or more columns: " + suffix + "/" + female, report);
                    if (!female) Capture(canvas, camera, target, "appearance-catalog-" + suffix, report);
                    for (var theme = 0; theme < cards.Length; theme++)
                    {
                        var scroll = Get<RectTransform>(panel, "_content").anchoredPosition; cards[theme].onClick.Invoke(); Flush(canvas);
                        Check(panel.DetailsOpen && panel.PreviewId == CharacterAppearance.Id(theme, female) && Get<RectTransform>(panel, "_detailsRoot").GetComponentsInChildren<ScrollRect>(true).Length == 0,
                            "Card click opens paired detail modal without scrolling: " + suffix + "/" + female + "/" + theme, report);
                        VerifyDetails(panel, report, suffix + "/" + female + "/" + theme);
                        if (theme == 9) Capture(canvas, camera, target, "appearance-alchemist-" + (female ? "female-" : "male-") + suffix, report);
                        var close = Get<RectTransform>(panel, "_detailsRoot").GetComponentsInChildren<Button>(true).Single(b => b.name == "CloseAppearanceDetails"); close.onClick.Invoke();
                        Check(!panel.DetailsOpen && Get<RectTransform>(panel, "_content").anchoredPosition == scroll, "Closing detail preserves catalog scroll position: " + suffix + "/" + theme, report);
                    }
                    Seed(female, female ? fixtures.female_all : fixtures.male_all); panel.SelectTheme(8); Flush(canvas); VerifyDetails(panel, report, "full collection/" + suffix + "/" + female);
                    if (!female) Capture(canvas, camera, target, "appearance-full-collection-" + suffix, report);
                    panel.Hide();
                }
                host.gameObject.SetActive(false); bg.sprite = Resources.Load<Sprite>("Expedition/Boss/0314/arena-rift");
                CaptureBoss(canvas, camera, target, suffix, report); canvas.gameObject.SetActive(false);
                CaptureMonster(camera, target, suffix, report);
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true);
            }
        }
        private static void VerifyDetails(CharacterAppearancePanel panel, Report report, string context)
        {
            var content = Get<RectTransform>(panel, "_detailsContent");
            foreach (var field in new[] { "_title", "_description", "_requirements", "_reward", "_status", "_collectionBonus" })
            {
                var text = Get<TMP_Text>(panel, field); text.ForceMeshUpdate();
                Check(!text.isTextOverflowing && text.fontSize >= 27 && text.preferredHeight <= text.rectTransform.rect.height + 3,
                    "Full readable text fits " + field + "/" + context, report);
                if (text.transform.parent == content) Check(-text.rectTransform.anchoredPosition.y + text.rectTransform.rect.height <= content.rect.height + 2,
                    "Complete text stays above the apply button " + field + "/" + context, report);
            }
        }
        private static void CaptureBoss(Canvas canvas, Camera camera, RenderTexture target, string suffix, Report report)
        {
            var fixture = new GlobalBossService.State { ok = true, event_id = "qa-release0318", status = "active", name = "Сердце Разлома", hp = 123450, hp_max = 550000,
                attempts_total = 3, attempts_left = 0, token_count = 0, base_damage = 3500, base_armor = 1000, base_crit = .05f, max_turns = 30, server_now = 1000, ends_at = 87400 };
            var boss = ExpeditionGlobalBossPanel.Open(canvas.transform, fixture); boss.enabled = false; Flush(canvas);
            var practice = boss.GetComponentsInChildren<Button>(true).Single(b => b.name == "PracticeRift"); var label = practice.GetComponentInChildren<TMP_Text>();
            Check(practice.interactable && label.fontSize >= 23 && label.fontStyle.HasFlag(FontStyles.Bold) && !label.isTextOverflowing,
                "Practice button has a readable emphasized caption and remains available without attempts: " + suffix, report);
            Capture(canvas, camera, target, "boss-practice-" + suffix, report); UnityEngine.Object.DestroyImmediate(boss.gameObject);
        }
        private static void CaptureMonster(Camera camera, RenderTexture target, string suffix, Report report)
        {
            var inactive = new GameObject("InactiveMine0318"); inactive.SetActive(false);
            var root = UnityEngine.Object.Instantiate(Resources.Load<GameObject>("UI/MonsterModal"), inactive.transform, false);
            var view = root.GetComponent<MonsterModalView>(); view.enabled = false; view.ApplyExpeditionStyle(); view.MonsterContentRoot.SetActive(true); view.BarrierContentRoot.SetActive(false);
            var canvas = root.GetComponent<Canvas>(); Configure(canvas, camera);
            foreach (var safe in root.GetComponentsInChildren<ExpeditionSafeArea>(true)) { safe.enabled = false; ExpeditionUi.SetRect(safe.transform as RectTransform, 0, 0, 1, 1); }
            var owner = new GameObject("DisabledMine0318Controller"); owner.transform.SetParent(inactive.transform); var controller = owner.AddComponent<MineSceneController>(); controller.enabled = false;
            Set(controller, "itemCatalog", AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset"));
            Set(controller, "affixCatalog", Resources.Load<AffixCatalog>("Match3/AffixCatalog")); Set(controller, "_difficulty", "medium"); Set(controller, "_selectedFloor", 5);
            Set(controller, "_modalRoot", root); Set(controller, "_modalStatTexts", view.StatTexts); Set(controller, "_modalFightButton", view.FightButton); Set(controller, "_modalDismissButton", view.DismissButton);
            Set(controller, "_energySprite", ExpeditionResourceIcons.Get("energy"));
            Set(controller, "_modalMonsterRewardsTitle", view.RewardsSectionTitle); Set(controller, "_modalMonsterRewardsColumnsRoot", view.RewardsDynamicRoot); Set(controller, "_modalMonsterRewardsValueFont", ExpeditionTheme.LegacyFont);
            Set(controller, "_modalAffixIcon", view.AffixIcon); Set(controller, "_modalAffixIconText", view.AffixIconGlyph); Set(controller, "_modalAffixTitleText", view.AffixTitleText); Set(controller, "_modalAffixDescriptionText", view.AffixDescriptionText);
            var bot = JsonUtility.FromJson(ExtractObject(File.ReadAllText("Server/nakama/data/duel_match3_bots_catalog_medium.json"), "mine_5"), typeof(MineSceneController).GetNestedType("PveBotInfo", BindingFlags.NonPublic));
            var monster = AssetDatabase.LoadAssetAtPath<MonsterDefinition>("Assets/_Project/Data/Match3/Monsters/mine_5.asset");
            view.SetExpeditionContext(monster.Icon, 5, "Средняя", false); view.TitleText.text = monster.DisplayName;
            Call(controller, "ApplyMonsterStatTexts", 1234, 173, 208, .13f, 37, 0); Call(controller, "ApplyAffixVisual", "stone_skin"); Call(controller, "PopulateMonsterRewards", bot);
            foreach (var button in new[] { view.FightButton, view.DismissButton }) { var old = button.transform.Find("Label"); if (old != null) UnityEngine.Object.DestroyImmediate(old.gameObject); }
            Call(controller, "ApplyFightButtonCosts", bot); Call(controller, "ApplyDismissButtonCosts", false, bot);
            view.SupplementalInfoText.text = ""; view.RefreshDynamicStyles(); root.SetActive(true);
            foreach (var binding in root.GetComponentsInChildren<CharacterAppearanceImage>(true)) { binding.enabled = false; var image = binding.GetComponent<Image>(); image.sprite = Resources.Load<Sprite>("Expedition/Art/hero"); image.enabled = true; }
            Flush(canvas);
            var sheet = view.DismissButton.transform.parent as RectTransform;
            var action = Bounds(view.DismissButton.transform as RectTransform, sheet);
            var affix = Bounds(root.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "AnomalyCard"), sheet);
            var stats = root.GetComponentsInChildren<RectTransform>(true).Single(r => r.name == "MonsterStatsFrame");
            var cells = Enumerable.Range(0, 5).Select(i => Bounds((RectTransform)stats.Find("MonsterStatRow_" + i), stats)).ToArray();
            Check(action.xMax < affix.xMin && action.yMin > Bounds(view.RewardsSectionTitle.rectTransform, sheet).yMax && cells[0].xMax < cells[1].xMin && Mathf.Abs(cells[0].center.y - cells[1].center.y) < 1 && cells[2].yMax < cells[0].yMin,
                "Actual monster controller keeps dismiss costs above rewards left of affix, with two stat columns: " + suffix, report);
            Check(view.DismissButton.transform.Find("MineFooterRow/CostStrip").childCount > 0 && view.RewardsDynamicRoot.childCount > 0,
                "Moved action and rewards preserve production costs/content: " + suffix, report);
            Capture(canvas, camera, target, "monster-dossier-" + suffix, report); UnityEngine.Object.DestroyImmediate(root); UnityEngine.Object.DestroyImmediate(inactive);
        }
        private static void Seed(bool female, CharacterAppearanceService.State state)
        {
            Set(typeof(AccountGender), "_scope", null); Set(typeof(AccountGender), "_loaded", true); Set(typeof(AccountGender), "_female", female); Set(typeof(AccountGender), "_canChoose", false); Set(typeof(AccountGender), "_unsupported", false);
            Set(typeof(CharacterAppearance), "_scope", null); Set(typeof(CharacterAppearance), "_loaded", false);
            Set(typeof(CharacterAppearanceService), "_scope", null); Set(typeof(CharacterAppearanceService), "_loaded", true); Set(typeof(CharacterAppearanceService), "_saving", false); Set(typeof(CharacterAppearanceService), "_lastError", null); Set(typeof(CharacterAppearanceService), "_state", state);
        }
        private static Canvas CanvasRoot(string name, Camera camera) { var root = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)); var canvas = root.GetComponent<Canvas>(); Configure(canvas, camera); return canvas; }
        private static void Configure(Canvas canvas, Camera camera)
        {
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        }
        private static void Flush(Canvas canvas) { foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31; Canvas.ForceUpdateCanvases(); foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate(); }
        private static void Capture(Canvas canvas, Camera camera, RenderTexture target, string name, Report report)
        {
            Flush(canvas); void Draw() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Draw(); Canvas.ForceUpdateCanvases(); Draw(); RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); File.WriteAllBytes(Output + "/" + name + ".png", pixels.EncodeToPNG()); report.screenshots.Add(Output + "/" + name + ".png"); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
        private static Rect Bounds(RectTransform child, RectTransform parent) { var corners = new Vector3[4]; child.GetWorldCorners(corners); var p = corners.Select(parent.InverseTransformPoint).ToArray(); return Rect.MinMaxRect(p.Min(v => v.x), p.Min(v => v.y), p.Max(v => v.x), p.Max(v => v.y)); }
        private static string ExtractObject(string json, string key)
        {
            var start = json.IndexOf('{', json.IndexOf('"' + key + '"', StringComparison.Ordinal)); var depth = 0; var quoted = false; var escaped = false;
            for (var index = start; index < json.Length; index++) { var ch = json[index]; if (quoted) { if (escaped) escaped = false; else if (ch == '\\') escaped = true; else if (ch == '"') quoted = false; continue; } if (ch == '"') quoted = true; else if (ch == '{') depth++; else if (ch == '}' && --depth == 0) return json.Substring(start, index - start + 1); }
            throw new InvalidOperationException("Catalog object missing: " + key);
        }
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, I).GetValue(owner);
        private static void Set(object owner, string name, object value) => owner.GetType().GetField(name, I).SetValue(owner, value);
        private static void Set(Type type, string name, object value) => type.GetField(name, S).SetValue(null, value);
        private static void Call(object owner, string name, params object[] args) => owner.GetType().GetMethod(name, I).Invoke(owner, args);
        private static void Save(Dictionary<FieldInfo, object> saved, Type type, params string[] names) { foreach (var name in names) { var field = type.GetField(name, S); saved.Add(field, field.GetValue(null)); } }
        private static void Check(bool condition, string message, Report report) { if (!condition) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
