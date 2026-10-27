using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
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
    /// <summary>Four isolated production Hero renders for unlock rewards. Editor only; never authenticates or applies a selection.</summary>
    public static class ExpeditionAppearanceCapture039
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
        private const string Fixture = "Assets/_Project/Editor/Fixtures/appearance-server-039.json";
        private const string Output = ".cache/qa/appearance-039";
        private const int Width = 1080, Height = 1920;
        [Serializable] private sealed class Fixtures { public CharacterAppearanceService.State male, female, male_selected, female_selected; }
        [Serializable] private sealed class Shot { public string path, gender, theme, rewardText, collectionText; public bool locked; public int rewardedMatter; public float hpBonusPercent; }
        [Serializable] private sealed class Report
        {
            public bool passed, accountOrNetworkActions, preferenceWrites;
            public string checkedUtc, fixture = Fixture, error;
            public int width = Width, height = Height;
            public List<Shot> screenshots = new List<Shot>();
        }
        private sealed class Caches
        {
            private readonly Dictionary<FieldInfo, object> _values = new Dictionary<FieldInfo, object>();
            public void Save(Type owner, params string[] names)
            {
                foreach (var name in names) { var field = Field(owner, name); _values.Add(field, field.GetValue(null)); }
            }
            public void Restore()
            {
                foreach (var pair in _values) pair.Key.SetValue(null, pair.Value);
                foreach (var pair in _values) Require(Equals(pair.Key.GetValue(null), pair.Value), "Fixture did not restore " + pair.Key.Name);
            }
        }
        [MenuItem("Tools/Expedition/Capture Hero Unlock Rewards 039")]
        public static void Capture() { Run("capture"); }
        public static string Run(string target)
        {
            Require(target == "capture" || string.IsNullOrEmpty(target), "Use target capture.");
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Capture Hero rewards in Edit mode.");
            var fixtures = JsonUtility.FromJson<Fixtures>(File.ReadAllText(Fixture));
            Require(fixtures != null, "039 server fixtures are missing.");
            Directory.CreateDirectory(Output);
            var caches = new Caches();
            caches.Save(typeof(AccountGender), "_scope", "_loaded", "_female", "_canChoose", "_unsupported", "Changed");
            caches.Save(typeof(CharacterAppearance), "_scope", "_loaded", "_female", "_genderLoaded", "_current", "Changed");
            caches.Save(typeof(CharacterAppearanceService), "_scope", "_loaded", "_saving", "_state", "_lastError", "Changed", "UnlockRewardsGranted");
            foreach (var type in new[] { typeof(AccountGender), typeof(CharacterAppearance), typeof(CharacterAppearanceService) }) Set(type, "Changed", null);
            Set(typeof(CharacterAppearanceService), "UnlockRewardsGranted", null);
            var previousScene = SceneManager.GetActiveScene();
            var previousTarget = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture texture = null;
            var report = new Report { checkedUtc = DateTime.UtcNow.ToString("O") };
            try
            {
                camera = new GameObject("Hero039QaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel;
                camera.orthographic = true; camera.orthographicSize = Height * .5f; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.transform.position = new Vector3(0, 0, -10);
                texture = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32); texture.Create(); camera.targetTexture = texture;
                var canvas = new GameObject("Hero039QaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1; canvas.scaleFactor = 1;
                var background = ExpeditionUi.Fill(canvas.transform, "Background", 0, 0, 1, 1, Color.white);
                background.sprite = Resources.Load<Sprite>("Expedition/Art/character-backdrop");
                if (background.sprite == null) background.color = ExpeditionTheme.Panel;
                var header = ExpeditionUi.Panel(canvas.transform, "Header", .014f, .929f, .986f, .995f);
                var title = ExpeditionUi.Text(header.transform, "Title", "ГЕРОЙ", 37, .04f, .40f, .96f, .92f, TextAlignmentOptions.Center); title.margin = Vector4.zero; title.color = ExpeditionTheme.Amber;
                var hint = ExpeditionUi.Text(header.transform, "Hint", "Награды за открытие образов", 21, .04f, .06f, .96f, .42f, TextAlignmentOptions.Center); hint.margin = Vector4.zero;
                var host = ExpeditionUi.Rect(canvas.transform, "HeroPage", 0, .105f, 1, .922f);
                var navigation = new[] { "Убежище", "Шахта", "Герой", "Арена", "Мастерская" };
                for (var i = 0; i < navigation.Length; i++) ExpeditionUi.Button(canvas.transform, "Navigation" + i, navigation[i], .015f + i * .197f, .018f, .199f + i * .197f, .087f, null, i == 2, 23);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/CharacterUI/CharacterHudOverlay.prefab");
                Require(prefab != null, "Production Hero prefab is missing.");
                var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                var view = clone.GetComponentInChildren<CharacterScreenView>(true); Require(view != null, "Character view is missing."); view.EnsureBuilt();
                var layout = (CharacterExpeditionLayout)typeof(CharacterScreenView).GetMethod("ApplyExpeditionLayout", Hidden).Invoke(view, new object[] { null, null, null, null });
                view.AttachToShell(host); view.gameObject.SetActive(true);
                var group = view.GetComponent<CanvasGroup>(); if (group != null) { group.alpha = 1; group.interactable = group.blocksRaycasts = false; }
                foreach (var extra in clone.GetComponentsInChildren<Canvas>(true)) if (!extra.transform.IsChildOf(view.transform)) extra.enabled = false;
                var ownCanvas = view.GetComponent<Canvas>(); if (ownCanvas != null) { ownCanvas.enabled = true; ownCanvas.overrideSorting = false; }
                Canvas.ForceUpdateCanvases(); Render(camera, texture); Canvas.ForceUpdateCanvases();
                typeof(CharacterExpeditionLayout).GetMethod("ApplyGeometry", Hidden).Invoke(layout, null);
                var panel = view.GetComponent<CharacterAppearancePanel>(); Require(panel != null, "Production appearance panel is missing.");
                for (var gender = 0; gender < 2; gender++)
                {
                    var female = gender == 1;
                    RenderCase(fixtures, female, false, panel, layout, view, camera, texture, report);
                    RenderCase(fixtures, female, true, panel, layout, view, camera, texture, report);
                }
                report.passed = true;
            }
            catch (Exception ex) { report.error = ex.ToString(); throw; }
            finally
            {
                try
                {
                    RenderTexture.active = previousTarget;
                    if (camera != null) camera.targetTexture = null;
                    if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
                    if (previousScene.IsValid() && previousScene.isLoaded) SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(scene, true);
                }
                finally
                {
                    caches.Restore();
                    File.WriteAllText(Path.Combine(Output, "hero039-capture-report.json"), JsonUtility.ToJson(report, true));
                }
            }
            return JsonUtility.ToJson(report);
        }
        private static void RenderCase(Fixtures fixtures, bool female, bool unlocked, CharacterAppearancePanel panel, CharacterExpeditionLayout layout,
            CharacterScreenView view, Camera camera, RenderTexture texture, Report report)
        {
            var scope = LocalAccountCacheScope.Current;
            Set(typeof(AccountGender), "_scope", scope); Set(typeof(AccountGender), "_loaded", true); Set(typeof(AccountGender), "_female", female);
            Set(typeof(AccountGender), "_canChoose", false); Set(typeof(AccountGender), "_unsupported", false);
            Set(typeof(CharacterAppearance), "_loaded", false); CharacterAppearance.RefreshAccount();
            var state = unlocked ? (female ? fixtures.female_selected : fixtures.male_selected) : (female ? fixtures.female : fixtures.male);
            Require(state != null && state.ok && state.unlock_reward_schema == 1, "Reward-enabled server fixture is missing.");
            Set(typeof(CharacterAppearanceService), "_scope", scope); Set(typeof(CharacterAppearanceService), "_loaded", false); Set(typeof(CharacterAppearanceService), "_saving", false);
            Require((bool)typeof(CharacterAppearanceService).GetMethod("Accept", Static).Invoke(null, new object[] { state }), "Client rejected the039 server fixture.");
            layout.SelectHeroTab(1); panel.SelectTheme(unlocked ? 1 : 5);
            Canvas.ForceUpdateCanvases(); panel.RefreshGridLayout();
            var reward = ExpeditionUi.Find(view.transform, "AppearanceUnlockReward")?.GetComponent<TMP_Text>();
            var collection = ExpeditionUi.Find(view.transform, "AppearanceCollectionBonus")?.GetComponent<TMP_Text>();
            Require(reward != null && reward.isActiveAndEnabled && reward.text.Contains("5 материи") && reward.text.Contains("0,5%"), "Per-skin5 matter/+0.5% reward is absent.");
            var percent = state.passive_hp_bonus_percent.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"));
            Require(collection != null && collection.isActiveAndEnabled && collection.text.Contains("+" + percent + "%") && collection.text.Contains(state.rewarded_matter_total + " материи"), "Collection reward totals are absent.");
            var apply = ExpeditionUi.Find(view.transform, "ApplyAppearance")?.GetComponent<Button>();
            Require(apply != null && (unlocked ? apply.GetComponentInChildren<TMP_Text>().text == "ВЫБРАНО" : !apply.interactable), "Locked/selected UI state is incorrect.");
            Require(CharacterAppearance.AvatarId == CharacterAppearance.BodyId && panel.PreviewId.EndsWith(female ? "-female" : "-male", StringComparison.Ordinal), "Gender or paired avatar drifted.");
            var shot = new Shot { gender = female ? "female" : "male", theme = unlocked ? "voidranger" : "phoenix", locked = !unlocked,
                rewardText = reward.text, collectionText = collection.text, hpBonusPercent = state.passive_hp_bonus_percent, rewardedMatter = state.rewarded_matter_total };
            shot.path = Path.Combine(Output, "hero039-" + shot.gender + "-" + (unlocked ? "unlocked-" : "locked-") + shot.theme + ".png");
            foreach (var text in view.GetComponentsInChildren<TMP_Text>())
            {
                text.ForceMeshUpdate();
                if (text.name.StartsWith("Appearance", StringComparison.Ordinal)) Require(!text.isTextOverflowing, "Hero039 overflow: " + text.name + " / " + text.text + " / rect=" + text.rectTransform.rect);
            }
            Render(camera, texture); RenderTexture.active = texture;
            var pixels = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); pixels.Apply(); File.WriteAllBytes(shot.path, pixels.EncodeToPNG()); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
            report.screenshots.Add(shot);
        }
        private static void Render(Camera camera, RenderTexture texture)
        {
            if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = texture });
            else camera.Render();
        }
        private static FieldInfo Field(Type owner, string name) { var field = owner.GetField(name, Static); Require(field != null, "Missing fixture cache field " + owner.Name + "." + name); return field; }
        private static void Set(Type owner, string name, object value) => Field(owner, name).SetValue(null, value);
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
