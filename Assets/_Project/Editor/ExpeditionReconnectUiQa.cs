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
    /// <summary>Production Shelter resource controls, large appearance typography and actual ability cost rendering. Edit mode, no transport or persisted account state.</summary>
    public static class ExpeditionReconnectUiQa
    {
        private const string Output = ".cache/qa/reconnect-0317/ui";
        private const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        [Serializable] private sealed class Fixtures { public CharacterAppearanceService.State male, female, male_selected, female_selected; }
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, preferenceWrites;
            public string utc, error, shelterChecks;
            public List<string> checks = new(), screenshots = new();
        }
        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(Run());
        }
        public static string Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Edit mode required.");
            Directory.CreateDirectory(Output);
            var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            var scene = SceneManager.GetActiveScene(); var texture = RenderTexture.active;
            var saved = new Dictionary<FieldInfo, object>();
            Save(saved, typeof(NakamaBootstrap), "<Instance>k__BackingField");
            Save(saved, typeof(AccountGender), "_scope", "_loaded", "_female", "_canChoose", "_unsupported", "Changed");
            Save(saved, typeof(CharacterAppearance), "_scope", "_loaded", "_female", "_genderLoaded", "_current", "Changed");
            Save(saved, typeof(CharacterAppearanceService), "_scope", "_loaded", "_saving", "_state", "_lastError", "Changed", "UnlockRewardsGranted");
            var source = default(Scene);
            try
            {
                report.shelterChecks = ExpeditionShelterConceptCQa.Run(Output);
                foreach (var owner in new[] { typeof(AccountGender), typeof(CharacterAppearance), typeof(CharacterAppearanceService) }) Set(owner, "Changed", null);
                Set(typeof(CharacterAppearanceService), "UnlockRewardsGranted", null);
                Set(typeof(NakamaBootstrap), "<Instance>k__BackingField", null);
                source = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/DuelMatch3.unity");
                var manager = source.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<DuelMatch3Manager>(true)).Single();
                var fixtures = JsonUtility.FromJson<Fixtures>(File.ReadAllText("Assets/_Project/Editor/Fixtures/appearance-server-039.json"));
                foreach (var height in new[] { 1920, 2400 }) Profile(height, fixtures, manager, report);
                Check(NakamaBootstrap.Instance == null, "No network/auth bootstrap was created", report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                if (source.IsValid()) EditorSceneManager.ClosePreviewScene(source);
                foreach (var pair in saved) pair.Key.SetValue(null, pair.Value);
                RenderTexture.active = texture;
                if (scene.IsValid() && scene.isLoaded) SceneManager.SetActiveScene(scene);
                File.WriteAllText(Output + "/ui-report.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return report.checks.Count + " UI typography/cost checks plus " + report.shelterChecks + "; output " + Output;
        }

        private static void Profile(int height, Fixtures fixtures, DuelMatch3Manager manager, Report report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            var previousTarget = RenderTexture.active;
            Camera camera = null; RenderTexture target = null;
            try
            {
                camera = new GameObject("Ui0317Camera", typeof(Camera)).GetComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = height * .5f; camera.transform.position = new Vector3(0, 0, -10);
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = ExpeditionTheme.Panel;
                camera.cullingMask = 1 << 31; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                target = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = new GameObject("Ui0317Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1; canvas.sortingOrder = 70;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                var scaler = canvas.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = 1;
                var bg = ExpeditionUi.Fill(canvas.transform, "Background", 0, 0, 1, 1, Color.white);
                bg.sprite = Resources.Load<Sprite>("Expedition/Art/character-backdrop");
                // Use the same outer safe area and Hero host as the shipping shell.
                var safe = ExpeditionUi.Rect(canvas.transform, "SafeArea", 18f / 1080, 40f / height, 1 - 18f / 1080, 1 - 80f / height);
                var host = ExpeditionUi.Rect(safe, "HeroPageHost", 0, .105f, 1, .922f);
                var panel = host.gameObject.AddComponent<CharacterAppearancePanel>(); panel.Build(host);
                foreach (var female in new[] { false, true })
                {
                    SeedGender(female, female ? fixtures.female_selected : fixtures.male);
                    panel.Show();
                    for (var theme = 0; theme < CharacterAppearance.Themes.Count; theme++)
                    {
                        panel.SelectTheme(theme); Flush(canvas);
                        Check(panel.PreviewId == CharacterAppearance.Id(theme, female), "Preview selection retains paired account gender " + theme + "/" + female, report);
                        VerifyAppearance(panel, report, height + "/" + female + "/" + theme);
                    }
                    panel.SelectTheme(female ? 1 : 9); Flush(canvas);
                    Capture(canvas, camera, target, "appearance-" + (female ? "female-open-" : "male-locked-") + height, report);
                    var details = Get<RectTransform>(panel, "_detailsRoot");
                    Check(panel.DetailsOpen && details.GetComponentsInChildren<ScrollRect>(true).Length == 0,
                        "Selected appearance has a complete detail modal without description or bonus scrolling " + height, report);
                    if (!female) Capture(canvas, camera, target, "appearance-rewards-" + height, report);
                    panel.Hide();
                }
                host.gameObject.SetActive(false); bg.sprite = Resources.Load<Sprite>("Expedition/Art/battle-world-v5");
                SeedGender(false, fixtures.male_selected);
                VerifyAbilities(canvas, safe, camera, target, manager, height, report);
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = previousTarget;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void SeedGender(bool female, CharacterAppearanceService.State state)
        {
            Set(typeof(AccountGender), "_scope", null); Set(typeof(AccountGender), "_loaded", true); Set(typeof(AccountGender), "_female", female);
            Set(typeof(AccountGender), "_canChoose", false); Set(typeof(AccountGender), "_unsupported", false);
            Set(typeof(CharacterAppearance), "_scope", null); Set(typeof(CharacterAppearance), "_loaded", false);
            foreach (var theme in state.themes) if (theme.requirements == null) theme.requirements = Array.Empty<CharacterAppearanceService.Requirement>();
            Set(typeof(CharacterAppearanceService), "_scope", null); Set(typeof(CharacterAppearanceService), "_loaded", true);
            Set(typeof(CharacterAppearanceService), "_saving", false); Set(typeof(CharacterAppearanceService), "_lastError", null); Set(typeof(CharacterAppearanceService), "_state", state);
        }

        private static void VerifyAppearance(CharacterAppearancePanel panel, Report report, string name)
        {
            var root = Get<RectTransform>(panel, "_root");
            var title = Get<TMP_Text>(panel, "_title"); var description = Get<TMP_Text>(panel, "_description");
            Check(title.fontSize >= 40 && description.fontSize >= 27, "Appearance title and description retain readable phone typography " + name, report);
            foreach (var field in new[] { "_requirements", "_reward", "_status", "_collectionBonus" })
            {
                var text = Get<TMP_Text>(panel, field); text.ForceMeshUpdate();
                Check(text.fontSize >= 27 && !text.isTextOverflowing && text.preferredHeight <= text.rectTransform.rect.height + 2, "Detail has full non-truncated content " + field + "/" + name, report);
                var content = Get<RectTransform>(panel, "_detailsContent");
                Check(-text.rectTransform.anchoredPosition.y + text.rectTransform.rect.height <= content.rect.height + 2,
                    "Detail stays fully visible without scrolling " + field + "/" + name, report);
            }
            Check(!title.isTextOverflowing && !description.isTextOverflowing, "Title and description fit without ellipsis " + name, report);
            var cards = Get<Button[]>(panel, "_cards"); var a = cards[0].transform as RectTransform; var b = cards[1].transform as RectTransform;
            Check(panel.GridColumns >= 3 && Mathf.Abs(a.anchoredPosition.y - b.anchoredPosition.y) < .1f
                && Mathf.Abs(cards[2].GetComponent<RectTransform>().anchoredPosition.y - a.anchoredPosition.y) < .1f
                && cards[panel.GridColumns].GetComponent<RectTransform>().anchoredPosition.y < a.anchoredPosition.y,
                "Appearance catalog uses at least three responsive columns " + name, report);
            foreach (var card in cards)
            foreach (var text in card.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate();
                Check(text.fontSize >= 25 && !text.isTextOverflowing, "Appearance catalog card text fits " + card.name + "/" + text.name + "/" + name, report);
            }
            var apply = Get<Button>(panel, "_apply");
            Check(apply.GetComponentInChildren<TMP_Text>().fontSize >= 44 && !apply.interactable, "Offline preview keeps enlarged apply control server-authorized " + name, report);
            foreach (var field in new[] { "_restore", "_refresh" })
            {
                var button = Get<Button>(panel, field); var label = button.GetComponentInChildren<TMP_Text>();
                label.ForceMeshUpdate(); var glyphs = label.textBounds;
                var low = button.transform.InverseTransformPoint(label.transform.TransformPoint(glyphs.min));
                var high = button.transform.InverseTransformPoint(label.transform.TransformPoint(glyphs.max));
                var rect = (button.transform as RectTransform).rect;
                Check(!label.isTextOverflowing && label.fontSize >= 32 && low.y >= rect.yMin + 6 && high.y <= rect.yMax - 6,
                    "Footer control has readable text and at least6px above/below its glyphs " + field + "/" + name, report);
            }
            Check(root.GetComponentsInChildren<Image>(true).Single(image => image.name == "AppearanceBodyPreview").sprite != null
                && root.GetComponentsInChildren<Image>(true).Single(image => image.name == "AppearanceAvatarPreview").sprite != null,
                "Appearance preview contains both shipping body and paired portrait " + name, report);
        }

        private static void VerifyAbilities(Canvas canvas, RectTransform safe, Camera camera, RenderTexture target, DuelMatch3Manager manager, int height, Report report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<Match3AbilityPanel>("Assets/_Project/Prefabs/Match3/Match3AbilityPanel.prefab");
            Check(prefab != null, "Shipping ability panel prefab exists", report);
            var panel = UnityEngine.Object.Instantiate(prefab, safe, false);
            ExpeditionUi.SetRect(panel.transform as RectTransform, .025f, .05f, .975f, .16f);
            // The saved prefab intentionally contains only Cross/Square. Normal startup creates the other three.
            // Exercise that exact factory before resolving button or numeric-label references.
            var binding = typeof(DuelMatch3Manager).GetField("_abilityPanel", I);
            var configure = typeof(DuelMatch3Manager).GetMethod("ConfigureAbilityButtonsVisuals", I);
            Check(binding != null && configure != null, "Production five-button factory and owner field exist", report);
            var previousPanel = binding.GetValue(manager);
            try { binding.SetValue(manager, panel); configure.Invoke(manager, null); }
            finally { binding.SetValue(manager, previousPanel); }
            var buttons = new[] { panel.petardButton, panel.crossButton, panel.squareButton, panel.shieldButton, panel.furyButton };
            var names = new[] { "Petard", "Cross", "Square", "Shield", "Fury" };
            var labels = new TMP_Text[buttons.Length];
            for (var i = 0; i < buttons.Length; i++)
            {
                Check(buttons[i] != null, "Production factory supplies " + names[i] + " button / " + height, report);
                var label = buttons[i].transform.Find("AbilityCost");
                Check(label != null, "Production " + names[i] + " button contains its live AbilityCost child / " + height, report);
                labels[i] = label.GetComponent<TMP_Text>();
                Check(labels[i] != null, "Production " + names[i] + " cost node carries TMP_Text / " + height, report);
                var icon = buttons[i].transform.Find("AbilityIcon")?.GetComponent<Image>();
                Check(icon != null && icon.sprite != null, "Production " + names[i] + " button retains its authored ability icon / " + height, report);
            }
            panel.UseExpeditionStyle(); ExpeditionBattleLayout.ApplyAbilityLayout(panel);
            BuildCombatScreen(canvas, manager, report, height);
            panel.Refresh(new PlayerStats { mana = 500 }, true, false, 20, 20, 30, 40, 30);
            Flush(canvas);
            // Full diagnostics are written before any numeric-corner geometry assertion.
            Capture(canvas, camera, target, "ability-costs-" + height, report);
            var sharedMaterial = ExpeditionTheme.Font.material; var sharedDilate = sharedMaterial.GetFloat("_FaceDilate");
            foreach (var cost in labels)
            {
                Check(cost.font == ExpeditionTheme.Font && cost.fontSharedMaterial != sharedMaterial && cost.fontSharedMaterial.GetTexture("_MainTex") == cost.font.atlasTexture,
                    "Cost owns an isolated material with the actual font atlas " + cost.transform.parent.name + "/" + height, report);
                Check(cost.fontSharedMaterial.GetFloat("_FaceDilate") >= .15f && cost.fontWeight == FontWeight.Bold && cost.fontSize >= 20 && !cost.isTextOverflowing,
                    "Live cost has visible ink weight without shrinking to12px " + cost.text + "/" + height, report);
            }
            VerifyCostInk(canvas, camera, target, labels, height, report);
            panel.Refresh(new PlayerStats { mana = 500 }, true, false, 120, 120, 120, 120, 120); Flush(canvas);
            Capture(canvas, camera, target, "ability-costs-120-" + height, report);
            Check(labels.All(cost => cost.text == "120" && cost.fontSize >= 20 && !cost.isTextOverflowing), "Dynamic three-digit costs remain legible and are never baked into icons " + height, report);
            VerifyCostInk(canvas, camera, target, labels, height, report);
            Check(Mathf.Approximately(sharedMaterial.GetFloat("_FaceDilate"), sharedDilate), "Cost weight never changes the shared UI font material " + height, report);
        }

        private static void VerifyCostInk(Canvas canvas, Camera camera, RenderTexture target, TMP_Text[] labels, int height, Report report)
        {
            var bold = Render(canvas, camera, target); Color32[] unlabelled;
            try
            {
                foreach (var text in labels) text.enabled = false;
                unlabelled = Render(canvas, camera, target);
            }
            finally { foreach (var text in labels) text.enabled = true; }
            foreach (var text in labels)
            {
                // SDF textBounds contain transparent atlas padding. Measure only the changed native pixels of the live
                // numeric ink, scanning outside its authored beige-corner rectangle so an overflowing glyph is caught.
                var corner = PixelRect(text.rectTransform, camera, target); var left = target.width; var right = -1; var bottom = target.height; var top = -1; var visible = 0;
                for (var y = Math.Max(0, corner.yMin - 16); y < Math.Min(target.height, corner.yMax + 16); y++)
                for (var x = Math.Max(0, corner.xMin - 16); x < Math.Min(target.width, corner.xMax + 16); x++)
                {
                    var i = y * target.width + x;
                    if (Math.Abs(bold[i].r - unlabelled[i].r) + Math.Abs(bold[i].g - unlabelled[i].g) + Math.Abs(bold[i].b - unlabelled[i].b) <= 25) continue;
                    visible++; left = Math.Min(left, x); right = Math.Max(right, x); bottom = Math.Min(bottom, y); top = Math.Max(top, y);
                }
                Check(visible > 30 && left >= corner.xMin + 1 && right < corner.xMax - 1 && bottom >= corner.yMin + 1 && top < corner.yMax - 1,
                    "Actual cost ink has unclipped padding inside its authored icon corner: " + text.transform.parent.name + " cost=" + text.text + " pixels=" + visible + " ink=(" + left + "," + bottom + ")..(" + right + "," + top + ") corner=" + corner + "/" + height, report);
            }
            var label = labels[0]; var material = label.fontSharedMaterial;
            var face = material.GetFloat("_FaceDilate"); var outline = material.GetFloat("_OutlineWidth");
            try
            {
                material.SetFloat("_FaceDilate", 0); material.SetFloat("_OutlineWidth", 0); label.SetMaterialDirty();
                var regular = Render(canvas, camera, target); var corner = PixelRect(label.rectTransform, camera, target); var darker = 0;
                for (var y = corner.yMin; y < Math.Min(target.height, corner.yMax); y++) for (var x = corner.xMin; x < Math.Min(target.width, corner.xMax); x++)
                { var i = y * target.width + x; if (bold[i].r + bold[i].g + bold[i].b + 25 < regular[i].r + regular[i].g + regular[i].b) darker++; }
                Check(darker >= 15, "Actual cost " + label.text + " has visibly heavier ink than synthetic bold: " + darker + " darker pixels / " + height, report);
            }
            finally { material.SetFloat("_FaceDilate", face); material.SetFloat("_OutlineWidth", outline); label.SetMaterialDirty(); }
        }

        private static void BuildCombatScreen(Canvas canvas, DuelMatch3Manager manager, Report report, int height)
        {
            T[] Sources<T>() where T : Component => manager.gameObject.scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<T>(true)).ToArray();
            var sourcePanels = Sources<Match3PlayerPanel>().OrderBy(p => p.transform.position.x).ToArray();
            var players = sourcePanels.Select(source => UnityEngine.Object.Instantiate(source, canvas.transform, false)).ToArray();
            var sourceBoard = Sources<Match3BoardView>().Single();
            var board = UnityEngine.Object.Instantiate(sourceBoard, canvas.transform, false); board.enabled = false;
            var sourceHud = Sources<Match3GameHUD>().FirstOrDefault();
            var hud = sourceHud != null ? UnityEngine.Object.Instantiate(sourceHud, canvas.transform, false) : null;
            if (hud != null) hud.enabled = false;
            var layout = ExpeditionBattleLayout.Install(canvas.gameObject.scene);
            foreach (var binding in canvas.GetComponentsInChildren<CharacterAppearanceImage>(true)) binding.enabled = false;
            var local = new Match3BoardLogic(); local.Init(3400); board.Build(); board.RefreshAll(local);
            for (var side = 0; side < players.Length; side++)
            {
                var player = players[side]; player.enabled = false;
                player.nameText.text = side == 0 ? "ProKrust" : "Сухой грохот";
                player.avatarImage.sprite = side == 0 ? CharacterAppearance.Load("voidranger-male", false) : Resources.Load<Sprite>("Expedition/Monsters/mine-5");
                ExpeditionBattlePortrait.MarkFullBody(player.avatarImage);
                player.UpdateStats(side == 0 ? 1294 : 5796, side == 0 ? 1500 : 5796, side == 0 ? 137 : 20, side == 0 ? 200 : 100);
                player.UpdateCombatStats(side == 0 ? 173 : 549, side == 0 ? 208 : 389, side == 0 ? 45 : 0, side == 0 ? 13 : 2, side == 0);
                if (player.damagePopup != null) player.damagePopup.gameObject.SetActive(false);
                if (player.gainPopup != null) player.gainPopup.gameObject.SetActive(false);
            }
            if (hud != null) { hud.SetTurn("Ваш ход!"); hud.SetTimerPhase(false); hud.SetTimer("18", 18); }
            Canvas.ForceUpdateCanvases(); typeof(ExpeditionBattleLayout).GetMethod("ResizeBoard", I).Invoke(layout, null);
            var figures = canvas.GetComponentsInChildren<ExpeditionCombatantFigure>(true);
            Check(figures.Length == 2, "Actual battle layout supplies both full combatant figures " + height, report);
            foreach (var figure in figures)
            {
                // Production synchronizes its source in Update, then grounds in LateUpdate. Neither is automatic in this synchronous Edit-mode fixture.
                typeof(ExpeditionCombatantFigure).GetMethod("Update", I).Invoke(figure, null); figure.RefreshGrounding();
                var image = figure.GetComponent<Image>();
                var expected = figure.name == "ExpeditionPlayerFigure" ? CharacterAppearance.Load("voidranger-male", false) : Resources.Load<Sprite>("Expedition/Monsters/mine-5");
                Check(image.enabled && image.sprite == expected && ExpeditionCombatantGrounding.TryGet(image.sprite, out _) && image.rectTransform.rect.height > 50,
                    "Production Update mirrors the actual expected body and grounds a visible full figure " + figure.name + "/" + height, report);
            }
        }

        private static Color32[] Render(Canvas canvas, Camera camera, RenderTexture target)
        {
            Flush(canvas);
            void Draw() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Draw(); Canvas.ForceUpdateCanvases(); Draw(); RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply(); return pixels.GetPixels32(); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
        private static void Capture(Canvas canvas, Camera camera, RenderTexture target, string name, Report report)
        {
            var data = Render(canvas, camera, target); var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                image.SetPixels32(data); image.Apply(); var path = Output + "/" + name + ".png";
                File.WriteAllBytes(path, image.EncodeToPNG()); report.screenshots.Add(path);
                Check(data.Where((_, i) => i % 997 == 0).Select(c => (c.r << 16) | (c.g << 8) | c.b).Distinct().Count() > 32, "Native rendered UI is present " + name, report);
                if (name.StartsWith("ability-costs-", StringComparison.Ordinal))
                foreach (var figure in canvas.GetComponentsInChildren<ExpeditionCombatantFigure>())
                {
                    var body = figure.GetComponent<Image>();
                    try
                    {
                        body.enabled = false; var withoutBody = Render(canvas, camera, target); var box = PixelRect(body.rectTransform, camera, target); var changed = 0;
                        for (var y = box.yMin; y < Mathf.Min(box.yMax, target.height); y += 3) for (var x = box.xMin; x < Mathf.Min(box.xMax, target.width); x += 3)
                        { var i = y * target.width + x; if (Math.Abs(data[i].r - withoutBody[i].r) + Math.Abs(data[i].g - withoutBody[i].g) + Math.Abs(data[i].b - withoutBody[i].b) > 25) changed++; }
                        Check(changed > 100, "Full combat capture contains native body pixels: " + figure.name + "=" + changed + " / " + name, report);
                    }
                    finally { body.enabled = true; }
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(image); }
        }
        private static RectInt PixelRect(RectTransform rect, Camera camera, RenderTexture target)
        {
            var corners = new Vector3[4]; rect.GetWorldCorners(corners); var p = corners.Select(camera.WorldToScreenPoint).ToArray();
            return new RectInt(Mathf.Clamp(Mathf.FloorToInt(p.Min(v => v.x)), 0, target.width - 1), Mathf.Clamp(Mathf.FloorToInt(p.Min(v => v.y)), 0, target.height - 1),
                Mathf.Clamp(Mathf.CeilToInt(p.Max(v => v.x)) - Mathf.FloorToInt(p.Min(v => v.x)), 1, target.width), Mathf.Clamp(Mathf.CeilToInt(p.Max(v => v.y)) - Mathf.FloorToInt(p.Min(v => v.y)), 1, target.height));
        }
        private static void Flush(Canvas canvas)
        {
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            // Hidden-reference renders must not repopulate a disabled Graphic's CanvasRenderer mesh.
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true)) if (text.isActiveAndEnabled) text.ForceMeshUpdate();
        }
        private static T Get<T>(object owner, string name) => (T)owner.GetType().GetField(name, I).GetValue(owner);
        private static void Set(Type owner, string name, object value) => owner.GetField(name, S).SetValue(null, value);
        private static void Save(Dictionary<FieldInfo, object> saved, Type owner, params string[] names) { foreach (var name in names) { var f = owner.GetField(name, S); saved.Add(f, f.GetValue(null)); } }
        private static void Check(bool ok, string message, Report report) { if (!ok) throw new InvalidOperationException(message); report.checks.Add(message); }
    }
}
