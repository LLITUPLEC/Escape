using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character.UI;
using Project.Match3;
using Project.Nakama;
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
    /// <summary>Native production-view snapshots in disposable edit scenes. Never enters Play mode or creates an authenticated transport.</summary>
    public static class ExpeditionLocalPresentationQa
    {
        private const string Output = ".cache/qa/local-presentation";
        private const BindingFlags I = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags S = BindingFlags.Static | BindingFlags.NonPublic;
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites, preferenceWrites;
            public string utc, error;
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
            var originalScene = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
            var bootstrapField = typeof(NakamaBootstrap).GetField("<Instance>k__BackingField", S);
            var originalBootstrap = bootstrapField.GetValue(null);
            var genderFields = typeof(Project.Character.AccountGender).GetFields(S).Where(f => !f.IsInitOnly && (f.FieldType == typeof(string) || f.FieldType == typeof(bool))).ToDictionary(f => f, f => f.GetValue(null));
            var sourceScene = default(Scene); var report = new Report { utc = DateTime.UtcNow.ToString("O") };
            try
            {
                // Appearance bindings observe an explicitly absent account; their fallback path never reads PlayerPrefs.
                bootstrapField.SetValue(null, null);
                typeof(Project.Character.AccountGender).GetField("_scope", S).SetValue(null, null);
                typeof(Project.Character.AccountGender).GetField("_loaded", S).SetValue(null, false);
                sourceScene = EditorSceneManager.OpenPreviewScene("Assets/_Project/Scenes/DuelMatch3.unity");
                var sourcePanels = sourceScene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Match3PlayerPanel>(true)).OrderBy(panel => panel.transform.position.x).ToArray();
                Check(sourcePanels.Length == 2 && sourcePanels.All(panel => panel.combatStatsName != null), "Actual DuelMatch3 scene supplies both prefab instances and their serialized stats frames", report);
                foreach (var height in new[] { 1920, 2400 }) RunProfile(sourcePanels, 1080, height, report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            finally
            {
                bootstrapField.SetValue(null, originalBootstrap);
                foreach (var field in genderFields) field.Key.SetValue(null, field.Value);
                if (sourceScene.IsValid()) EditorSceneManager.ClosePreviewScene(sourceScene);
                RenderTexture.active = previousTexture;
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                File.WriteAllText(Output + "/presentation-report.json", JsonUtility.ToJson(report, true));
            }
            if (!report.passed) throw new InvalidOperationException(report.error);
            return report.checks.Count + " production UI checks passed; " + report.screenshots.Count + " native PNGs at 1080x1920/1080x2400. " + Path.GetFullPath(Output);
        }

        private static void RunProfile(Match3PlayerPanel[] sources, int width, int height, Report report)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); scene.name = "OfflinePresentationQa";
            SceneManager.SetActiveScene(scene);
            var previousTexture = RenderTexture.active; Camera camera = null; RenderTexture target = null;
            try
            {
                camera = new GameObject("OfflinePresentationCamera", typeof(Camera)).GetComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = height * .5f; camera.transform.position = new Vector3(0, 0, -10);
                camera.nearClipPlane = .1f; camera.farClipPlane = 100; camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var inactive = new GameObject("InactivePrefabConstruction"); inactive.SetActive(false);
                var suffix = width + "x" + height;
                VerifyCombatStats(sources, inactive.transform, camera, target, suffix, report);
                VerifyMatchup(inactive.transform, camera, target, suffix, report);
                VerifyMonsterModal(inactive.transform, camera, target, suffix, report);
            }
            finally
            {
                if (camera != null) camera.targetTexture = null;
                RenderTexture.active = previousTexture;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void VerifyCombatStats(Match3PlayerPanel[] sources, Transform inactive, Camera camera, RenderTexture target, string suffix, Report report)
        {
            var root = new GameObject("ActualBattleHeaderPreview", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = Configure(root, camera);
            var background = ExpeditionUi.Fill(root.transform, "BattleBackdrop", 0, 0, 1, 1, Color.white);
            background.sprite = Resources.Load<Sprite>("Expedition/Art/battle-world-v5");
            var backdropFit = background.gameObject.AddComponent<AspectRatioFitter>(); backdropFit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            backdropFit.aspectRatio = background.sprite.rect.width / background.sprite.rect.height;
            for (var side = 0; side < 2; side++)
            {
                var clone = UnityEngine.Object.Instantiate(sources[side].gameObject, inactive, false);
                var panel = clone.GetComponent<Match3PlayerPanel>(); panel.enabled = false;
                var oldFrame = panel.combatStatsName.transform.parent;
                // Exercise old scene/prefab variants even when this scene has already lost its legacy layout components.
                var legacyLayout = oldFrame.GetComponent<LayoutGroup>();
                if (legacyLayout == null) legacyLayout = oldFrame.gameObject.AddComponent<HorizontalLayoutGroup>();
                legacyLayout.enabled = true;
                var legacyFitter = oldFrame.GetComponent<ContentSizeFitter>();
                if (legacyFitter == null) legacyFitter = oldFrame.gameObject.AddComponent<ContentSizeFitter>();
                legacyFitter.enabled = true;
                clone.transform.SetParent(root.transform, false);
                ExpeditionUi.SetRect(clone.transform as RectTransform, side == 0 ? .012f : .51f, .918f, side == 0 ? .49f : .988f, .992f);
                panel.UseExpeditionStyle();
                var avatar = Resources.Load<Sprite>("Expedition/Art/hero");
                panel.avatarImage.sprite = avatar; ExpeditionBattlePortrait.MarkFullBody(panel.avatarImage);
                // Start normally hides the serialized sample damage/gain labels; edit-only fixtures never run Start.
                if (panel.damagePopup != null) panel.damagePopup.gameObject.SetActive(false);
                if (panel.gainPopup != null) panel.gainPopup.gameObject.SetActive(false);
                panel.nameText.text = side == 0 ? "Герой" : "Соперник";
                panel.UpdateStats(1294, 1500, 37, 100);
                panel.UpdateCombatStats(173, 208, 45, 13, side == 0);
                var popover = clone.GetComponent<ExpeditionCombatStatsPopover>();
                popover.OnPointerClick(new PointerEventData(null) { button = PointerEventData.InputButton.Left });
                Check(popover.Expanded, "Actual header click opens the production combat stats popover " + side + "/" + suffix, report);
                Canvas.ForceUpdateCanvases();
                var stats = clone.GetComponentInChildren<ExpeditionCombatStatIcons>(true); stats.Bind(); stats.SetValues(173, 208, 45, 13, side == 0);
                VerifyStatsGeometry(stats, side == 0, suffix, report);
            }
            Save(canvas, camera, target, "combat-stats-" + suffix, report);
            root.SetActive(false);
        }

        private static void VerifyStatsGeometry(ExpeditionCombatStatIcons stats, bool showHealing, string suffix, Report report)
        {
            Canvas.ForceUpdateCanvases(); var frame = stats.transform as RectTransform;
            Check(stats.GetComponents<LayoutGroup>().All(group => !group.enabled) && stats.GetComponents<ContentSizeFitter>().All(fit => !fit.enabled), "Old frame layout and size fitter cannot override explicit stat positions " + showHealing + "/" + suffix, report);
            var cells = stats.GetComponentsInChildren<TMP_Text>(true).Where(text => text.name == "Value").ToArray();
            Check(cells.Length == 4, "Exactly four canonical combat icon/value cells exist " + suffix, report);
            for (var i = 0; i < stats.Icons.Length; i++)
            {
                var icon = stats.Icons[i]; var cell = icon.transform.parent as RectTransform; var value = cell.Find("Value").GetComponent<TMP_Text>();
                value.ForceMeshUpdate(true);
                Check(icon.sprite != null && Near(icon.rectTransform.rect.width, 50) && Near(icon.rectTransform.rect.height, 50) && Near(value.fontSizeMax, 50), "Canonical icon is 50x50 and value fontMax50: " + i + "/" + suffix, report);
                Check(cell.gameObject.activeSelf == (i != 2 || showHealing), "Healing visibility affects only its own cell " + i + "/" + suffix, report);
                if (!cell.gameObject.activeSelf) continue;
                var iconBounds = BoundsIn(icon.rectTransform, cell); var valueBounds = BoundsIn(value.rectTransform, cell);
                Check(Near((iconBounds.xMin + valueBounds.xMax) * .5f, cell.rect.center.x) && iconBounds.xMax < valueBounds.xMin && !value.isTextOverflowing, "Icon/value pair is centered with a gap and unclipped numeric text " + i + "/" + suffix, report);
                var cellBounds = BoundsIn(cell, frame);
                Check(Contains(frame.rect, cellBounds), "Combat cell stays inside the stats frame " + i + "/" + suffix, report);
            }
            var critical = stats.Icons[3].transform.parent as RectTransform;
            if (!showHealing) Check(Near(BoundsIn(critical, frame).center.x, frame.rect.center.x), "Critical pair is centered on the last row when healing is hidden " + suffix, report);
            else
            {
                var healing = stats.Icons[2].transform.parent as RectTransform;
                Check(Near((BoundsIn(critical, frame).center.x + BoundsIn(healing, frame).center.x) * .5f, frame.rect.center.x), "Full combat grid has two balanced columns " + suffix, report);
            }
        }

        private static void VerifyMatchup(Transform inactive, Camera camera, RenderTexture target, string suffix, Report report)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Match3/Match3SearchingPanel.prefab");
            var root = UnityEngine.Object.Instantiate(prefab, inactive, false); var panel = root.GetComponent<Match3SearchingPanel>(); panel.enabled = false;
            panel.Show("Поиск соперника…");
            var canvas = Configure(root, camera); RestoreLocalExplorer(root);
            foreach (var floor in new[] { 5, 11 })
            {
                var monster = AssetDatabase.LoadAssetAtPath<MonsterDefinition>("Assets/_Project/Data/Match3/Monsters/mine_" + floor + ".asset");
                Check(monster != null && monster.Icon != null, "Real mine monster artwork exists for floor " + floor, report);
                panel.ShowReady(monster.Icon, monster.DisplayName, false, "Матч готов · через 2…");
                Canvas.ForceUpdateCanvases();
                var image = (Image)typeof(Match3SearchingPanel).GetField("_opponentPortrait", I).GetValue(panel);
                var fill = image.GetComponent<ExpeditionMatchPortraitFill>();
                Check(fill != null && fill.enabled && image.sprite == monster.Icon && image.transform.parent.GetComponent<RectMask2D>() != null, "Monster reveal uses production solid-bounds fill inside the actual mask " + floor + "/" + suffix, report);
                fill.Refresh(); Canvas.ForceUpdateCanvases();
                var bounds = SolidBounds(image); var slot = (RectTransform)image.transform.parent;
                Check(Covers(bounds, slot.rect) && Near(bounds.yMax, slot.rect.yMax), "Opaque monster bounds cover both axes of the matchup card and align the head to its top " + floor + "/" + suffix, report);
                Check(!slot.Find("UnknownOpponent").gameObject.activeSelf, "Ready monster replaces the question mark " + floor + "/" + suffix, report);
                Save(canvas, camera, target, "matchup-mine" + floor + "-" + suffix, report);
            }
            var human = Resources.Load<Sprite>("Expedition/Art/hero");
            panel.ShowReady(human, "Друг", true, "Матч готов · через 2…"); Canvas.ForceUpdateCanvases();
            var restored = (Image)typeof(Match3SearchingPanel).GetField("_opponentPortrait", I).GetValue(panel);
            Check(!restored.GetComponent<ExpeditionMatchPortraitFill>().enabled && restored.GetComponent<AspectRatioFitter>().enabled && restored.rectTransform.pivot.y == 1 && restored.rectTransform.anchorMin.x < 0 && restored.rectTransform.anchorMax.x > 1, "Switching monster to a human restores the production upper-body crop " + suffix, report);
            Save(canvas, camera, target, "matchup-human-restored-" + suffix, report); root.SetActive(false);
        }

        private static void VerifyMonsterModal(Transform inactive, Camera camera, RenderTexture target, string suffix, Report report)
        {
            var prefab = Resources.Load<GameObject>("UI/MonsterModal");
            var root = UnityEngine.Object.Instantiate(prefab, inactive, false); var view = root.GetComponent<MonsterModalView>(); view.enabled = false;
            view.ApplyExpeditionStyle(); view.MonsterContentRoot.SetActive(true); view.BarrierContentRoot.SetActive(false);
            var canvas = Configure(root, camera); RestoreLocalExplorer(root);
            var controllerRoot = new GameObject("DisabledMineFormatter"); controllerRoot.SetActive(false);
            var controller = controllerRoot.AddComponent<MineSceneController>();
            typeof(MineSceneController).GetField("_modalStatTexts", I).SetValue(controller, view.StatTexts);
            var apply = typeof(MineSceneController).GetMethod("ApplyMonsterStatTexts", I);
            foreach (var floor in new[] { 5, 11 })
            {
                var monster = AssetDatabase.LoadAssetAtPath<MonsterDefinition>("Assets/_Project/Data/Match3/Monsters/mine_" + floor + ".asset");
                view.SetExpeditionContext(monster.Icon, floor, "medium", false); view.TitleText.text = monster.DisplayName;
                apply.Invoke(controller, new object[] { 1234, 173, 208, .13f, 37, 125 });
                view.AffixTitleText.text = "Без аномалии"; view.AffixDescriptionText.text = ""; view.SupplementalInfoText.text = "";
                view.RefreshDynamicStyles(); Canvas.ForceUpdateCanvases();
                var frame = root.GetComponentsInChildren<RectTransform>(true).Single(rect => rect.name == "MonsterStatsFrame");
                Check(frame.childCount == 5 && frame.GetComponents<Image>().Length == 1, "All five monster stats share one production frame " + floor + "/" + suffix, report);
                var expected = new[] { "1234", "173", "208", "13%", "37" };
                var lastBottom = float.PositiveInfinity;
                for (var i = 0; i < 5; i++)
                {
                    var row = frame.Find("MonsterStatRow_" + i) as RectTransform;
                    var icon = row.Find("StatIcon").GetComponent<Image>(); var value = view.StatTexts[i];
                    if (i == 4) Check(icon.sprite != null && icon.sprite == ExpeditionArt.Icon("stat-mana"), "Monster mana uses its canonical blue drop, distinct from energy " + floor + "/" + suffix, report);
                    var bounds = BoundsIn(row, frame);
                    Check(value.transform.parent == row && value.text == expected[i] && icon.sprite != null && row.GetComponent<Image>() == null, "Monster row has one canonical icon and numeric-only value, without another frame " + i + "/" + suffix, report);
                    Check(Contains(frame.rect, bounds) && bounds.yMax <= lastBottom + .1f && BoundsIn(icon.rectTransform, row).xMax < BoundsIn(value.rectTransform, row).xMin, "Five monster rows stay ordered, non-overlapping and inside the shared frame " + i + "/" + suffix, report);
                    lastBottom = bounds.yMin;
                }
                Check(!view.StatTexts[5].transform.IsChildOf(frame) && view.StatTexts[5].gameObject.activeInHierarchy && view.StatTexts[5].text.StartsWith("Появится:", StringComparison.Ordinal), "Respawn is visibly rendered separately from the five combat stat rows " + suffix, report);
                Save(canvas, camera, target, "monster-modal-mine" + floor + "-" + suffix, report);
                apply.Invoke(controller, new object[] { 1234, 173, 208, .13f, 37, 0 });
                Check(view.StatTexts[5].text == "", "Ready monster clears only the separate respawn text " + suffix, report);
            }
            root.SetActive(false);
        }

        private static Canvas Configure(GameObject root, Camera camera)
        {
            root.transform.SetParent(null, false); root.SetActive(true);
            var canvas = ExpeditionUi.Ensure<Canvas>(root); canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
            var scaler = ExpeditionUi.Ensure<CanvasScaler>(root); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight; scaler.matchWidthOrHeight = .5f;
            foreach (var safe in root.GetComponentsInChildren<ExpeditionSafeArea>(true)) { safe.enabled = false; ExpeditionUi.SetRect(safe.transform as RectTransform, 0, 0, 1, 1); }
            Canvas.ForceUpdateCanvases(); return canvas;
        }
        private static void RestoreLocalExplorer(GameObject root)
        {
            foreach (var binding in root.GetComponentsInChildren<CharacterAppearanceImage>(true))
            {
                binding.enabled = false; var image = binding.GetComponent<Image>(); image.sprite = Resources.Load<Sprite>("Expedition/Art/hero"); image.enabled = true; image.color = Color.white;
                if (image.name == "Portrait" && image.transform.parent.name == "PortraitCrop") ExpeditionMatchStates.CropUpperBody(image);
            }
        }
        private static Rect SolidBounds(Image image)
        {
            if (!ExpeditionCombatantGrounding.TryGet(image.sprite, out var footprint)) throw new InvalidOperationException("Missing actual artwork alpha bounds: " + image.sprite.name);
            var source = image.rectTransform.rect; var solid = footprint.SolidBounds; var parent = image.transform.parent as RectTransform;
            var low = parent.InverseTransformPoint(image.rectTransform.TransformPoint(new Vector2(source.xMin + source.width * solid.xMin, source.yMin + source.height * solid.yMin)));
            var high = parent.InverseTransformPoint(image.rectTransform.TransformPoint(new Vector2(source.xMin + source.width * solid.xMax, source.yMin + source.height * solid.yMax)));
            return Rect.MinMaxRect(low.x, low.y, high.x, high.y);
        }
        private static Rect BoundsIn(RectTransform child, RectTransform parent)
        {
            var corners = new Vector3[4]; child.GetWorldCorners(corners); var local = corners.Select(parent.InverseTransformPoint).ToArray();
            return Rect.MinMaxRect(local.Min(p => p.x), local.Min(p => p.y), local.Max(p => p.x), local.Max(p => p.y));
        }
        private static bool Near(float a, float b) => Mathf.Abs(a - b) < .15f;
        private static bool Contains(Rect outer, Rect inner) => inner.xMin >= outer.xMin - .15f && inner.yMin >= outer.yMin - .15f && inner.xMax <= outer.xMax + .15f && inner.yMax <= outer.yMax + .15f;
        private static bool Covers(Rect outer, Rect inner) => Contains(outer, inner);
        private static void Save(Canvas canvas, Camera camera, RenderTexture target, string name, Report report)
        {
            foreach (var transform in canvas.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases(); foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); pixels.Apply();
                var colors = pixels.GetPixels32().Where((_, i) => i % 997 == 0).Select(c => (c.r << 16) | (c.g << 8) | c.b).Distinct().Count();
                Check(colors > 32, "Native " + target.width + "x" + target.height + " screenshot contains rendered UI: " + name, report);
                var path = Output + "/" + name + ".png"; File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }
        private static void Check(bool passed, string message, Report report)
        {
            if (!passed) throw new InvalidOperationException(message);
            report.checks.Add(message);
        }
    }
}
