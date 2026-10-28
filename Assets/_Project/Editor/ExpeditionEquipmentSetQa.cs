using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
using Project.Character.UI;
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
    /// <summary>Production Hero/card widgets, isolated edit-mode fixture; no account/network actions.</summary>
    public static class ExpeditionEquipmentSetQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Output = "Design/Validation-2026-10-28/equipment";
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites;
            public int equipmentDefinitions, setQualities, activationCount;
            public List<string> checks = new(), screenshots = new();
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(Run());
        }

        public static string Run()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Set fixture requires Edit mode.");
            Directory.CreateDirectory(Output);
            var report = new Report();
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
            Check(catalog != null, "Shipping item catalog is missing.");
            var names = new[] { "helmet", "shoulders", "chest", "gloves", "legs", "feet", "weapon_l", "weapon_r" };
            foreach (var quality in new[] { "normal", "rare", "epic", "legendary" })
            {
                var ids = names.Select(slot => "eq_t1_" + quality + "_" + slot).ToArray();
                var complete = EquipmentSetBonus.Evaluate(ids, catalog);
                Check(complete.Active && complete.Count == 8, "Complete shipping set not recognized: " + quality);
                for (var slot = 0; slot < 8; slot++)
                {
                    var item = catalog.Get(ids[slot]);
                    Check(item != null && (int)item.Slot == slot, "Invalid shipping equipment slot.");
                    var card = EquipmentSetBonus.CardText(item, ids, catalog);
                    Check(card.Contains("8/8") && card.Contains("активен") && card.Contains("от вещей") && card.Contains("Крит без бонуса"), "Set card rule is missing: " + item.ItemId);
                    report.equipmentDefinitions++;
                }
                var partial = (string[])ids.Clone(); partial[7] = "";
                Check(!EquipmentSetBonus.Evaluate(partial, catalog).Active && EquipmentSetBonus.Evaluate(partial, catalog).Count == 7, "Seven pieces must not activate.");
                partial[7] = partial[0];
                Check(!EquipmentSetBonus.Evaluate(partial, catalog).Active, "Duplicate wrong-slot piece activated a set.");
                report.setQualities++;
            }
            report.checks.Add("All32 definitions/four qualities: eight correct slots activate; seven parts/duplicates do not; cards identify equipment-only25% and excluded critical chance.");

            var original = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture target = null; GameObject controllerRoot = null;
            try
            {
                camera = new GameObject("EquipmentSetQaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 960; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = new GameObject("EquipmentSetQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                var host = ExpeditionUi.Rect(canvas.transform, "HeroPage", 0, .07f, 1, .94f);
                var clone = Clone("CharacterHudOverlay", host, scene);
                foreach (var component in clone.GetComponentsInChildren<CharacterScreenController>(true)) UnityEngine.Object.DestroyImmediate(component);
                foreach (var component in clone.GetComponentsInChildren<CharacterSheetDragController>(true)) UnityEngine.Object.DestroyImmediate(component);
                foreach (var component in clone.GetComponentsInChildren<CharacterNicknameEditor>(true)) UnityEngine.Object.DestroyImmediate(component);
                var view = clone.GetComponentInChildren<CharacterScreenView>(true); view.EnsureBuilt();
                var layout = (CharacterExpeditionLayout)Call(view, "ApplyExpeditionLayout", null, null, null, null);
                view.AttachToShell(host); view.gameObject.SetActive(true); view.SetVisible(true);
                foreach (var group in view.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
                foreach (var nested in view.GetComponentsInChildren<Canvas>(true)) { nested.overrideSorting = false; nested.worldCamera = camera; }
                foreach (var binding in view.GetComponentsInChildren<CharacterAppearanceImage>(true)) binding.enabled = false;
                var hero = ExpeditionUi.Find(view.transform, "ExpeditionHero").GetComponent<Image>();
                hero.sprite = Resources.Load<Sprite>("Expedition/Appearance/dawnwarden-male-body"); hero.enabled = true;
                var full = names.Select(slot => "eq_t1_epic_" + slot).ToArray();
                var seven = (string[])full.Clone(); seven[7] = "";
                var profile = Profile(seven, catalog); view.ApplyCharacterResponse(profile, catalog);
                Check(!layout.EquipmentSetActive && layout.SetActivationCount == 0, "Seven parts animated a full set.");
                Check(Get<TMP_Text>(view, "hpText").text == profile.stats.hp.ToString(), "Hero ignored accepted seven-part stats.");
                layout.SelectHeroTab(0); Canvas.ForceUpdateCanvases(); Call(layout, "ApplyGeometry");
                Save(canvas, camera, target, "hero-set-seven", report);

                profile = Profile(full, catalog); view.ApplyCharacterResponse(profile, catalog);
                Check(layout.EquipmentSetActive && layout.SetActivationCount == 1, "Eighth part must activate once immediately.");
                Check(Get<TMP_Text>(view, "hpText").text == profile.stats.hp.ToString() && Get<TMP_Text>(view, "damageText").text == profile.stats.damage.ToString(), "Visible stats lag behind the complete equipment response.");
                layout.ApplySetActivationFrame(.18f);
                var rims = view.GetComponentsInChildren<Image>(true).Where(image => image.name == "ExpeditionSetActivationRim").ToArray();
                Check(rims.Length == 8 && rims.All(image => image.enabled && image.color.a > .3f && !image.raycastTarget), "Activation must highlight eight frames without blocking input.");
                Save(canvas, camera, target, "hero-set-activation", report);
                layout.ApplySetActivationFrame(2);
                Check(rims.All(image => !image.enabled) && layout.EquipmentSetActive, "Pulse should stop, active set badge should remain.");
                view.ApplyCharacterResponse(profile, catalog); view.ApplyCharacterResponse(profile, catalog);
                Check(layout.SetActivationCount == 1 && Get<TMP_Text>(view, "hpText").text == profile.stats.hp.ToString(), "Repeated response multiplied stats or restarted activation.");
                Save(canvas, camera, target, "hero-set-active", report);
                var badge = ExpeditionUi.Find(view.transform, "ExpeditionEquipmentSetBadge") as RectTransform;
                var slotRects = Get<List<RectTransform>>(layout, "_slots");
                var slotIds = Get<List<int>>(layout, "_slotIds");
                var handBounds = slotRects.Where((_, index) => slotIds[index] >= 6)
                    .Select(rect => RectTransformUtility.CalculateRelativeRectTransformBounds(badge.parent, rect))
                    .OrderBy(bounds => bounds.center.x).ToArray();
                var badgeBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(badge.parent, badge);
                Check(handBounds.Length == 2 && badgeBounds.min.x > handBounds[0].max.x && badgeBounds.max.x < handBounds[1].min.x,
                    "Active set buff must stay in the gap between both hand slots.");
                var buffLabel = badge.GetComponentInChildren<TMP_Text>(); buffLabel.ForceMeshUpdate();
                Check(!buffLabel.isTextOverflowing && buffLabel.text.Contains("8/8") && buffLabel.text.Contains("+25%"), "Compact set buff text is clipped.");

                controllerRoot = new GameObject("DisabledEquipmentSetQaController"); controllerRoot.SetActive(false);
                var controller = controllerRoot.AddComponent<CharacterScreenController>(); controller.enabled = false;
                Set(controller, "view", view); Set(controller, "itemCatalog", catalog); Set(controller, "_lastProfile", profile);
                var item = catalog.Get(full[4]);
                var text = (string)Call(controller, "BuildStatsDescription", item);
                Check(text.Contains("Комплект T1") && text.Contains("8/8") && text.Contains("активен") && text.Contains("+25%"), "Production card writer omitted set status.");
                var modalRoot = Clone("CharacterItemInfoModal", canvas.transform, scene);
                var modal = modalRoot.GetComponent<CharacterItemInfoModalView>();
                Call(modal, "EnsureDefaults"); modal.gameObject.SetActive(true);
                modal.SetTitle(item.DisplayName, item.ColorDisplayName); modal.SetSlot("Ноги · эпическое · T1");
                modal.SetDescription(item.Description); modal.SetStatsCaption("ХАРАКТЕРИСТИКИ"); modal.SetStats(text);
                modal.SetEquipButton(true, true, "Снять"); modal.SetLearnRecipeButton(false, false, "");
                foreach (var group in modalRoot.GetComponentsInChildren<CanvasGroup>(true)) group.alpha = 1;
                modal.PanelRect.localScale = Vector3.one;
                Save(canvas, camera, target, "equipment-set-card", report);
                UnityEngine.Object.DestroyImmediate(modalRoot);

                view.ApplyCharacterResponse(Profile(seven, catalog), catalog);
                Check(!layout.EquipmentSetActive && !ExpeditionUi.Find(view.transform, "ExpeditionEquipmentSetBadge").gameObject.activeSelf && rims.All(image => !image.enabled), "Unequip left stale set UI.");
                var mixed = (string[])full.Clone(); mixed[7] = "eq_t1_rare_weapon_r";
                Check(!EquipmentSetBonus.Evaluate(mixed, catalog).Active, "Mixed quality set activated.");
                report.activationCount = layout.SetActivationCount;
                report.checks.Add("Actual Hero binder immediately shows authoritative7/8 and8/8 values, activation deduplicates repeated responses, finite pulse preserves quality rims, unequip clears badge; actual item-card writer includes compact set rule.");
                report.passed = true;
                File.WriteAllText(Output + "/native-set-qa.json", JsonUtility.ToJson(report, true));
                return "PASS equipment-set native QA: " + JsonUtility.ToJson(report);
            }
            finally
            {
                if (controllerRoot != null) UnityEngine.Object.DestroyImmediate(controllerRoot);
                RenderTexture.active = previousTexture;
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(scene, true);
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }

        private static CharacterGetRpcResponse Profile(string[] ids, ItemCatalog catalog)
        {
            var multiplier = EquipmentSetBonus.Evaluate(ids, catalog).Active ? 1.25f : 1;
            float Sum(string stat) => ids.Sum(id => catalog.Get(id)?.GetStatValue(stat) ?? 0);
            return new CharacterGetRpcResponse { ok = true, inventory_capacity = 25, equipment_def_ids = ids,
                inventory_def_ids = new string[25], inventory_counts = new int[25], stats = new StatsMap {
                    hp = Mathf.FloorToInt(480 + Sum(StatId.Hp) * multiplier + .5f), damage = Mathf.FloorToInt(11 + Sum(StatId.Damage) * multiplier + .5f),
                    armor = Mathf.FloorToInt(11 + Sum(StatId.Armor) * multiplier + .5f), healing = Mathf.FloorToInt(11 + Sum(StatId.Healing) * multiplier + .5f),
                    crit_chance = .06f + Sum(StatId.CritChance) } };
        }

        private static GameObject Clone(string name, Transform parent, Scene scene)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/CharacterUI/" + name + ".prefab");
            Check(prefab != null, "Missing production prefab: " + name);
            var clone = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            PrefabUtility.UnpackPrefabInstance(clone, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            clone.transform.SetParent(parent, false); clone.SetActive(true); return clone;
        }

        private static void Save(Canvas canvas, Camera camera, RenderTexture target, string name, Report report)
        {
            foreach (var transform in canvas.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0); pixels.Apply();
                var samples = pixels.GetPixels32().Where((_, i) => i % 997 == 0).Select(c => (c.r << 16) | (c.g << 8) | c.b).Distinct().Count();
                Check(samples > 32, "Native capture is blank: " + name);
                var path = Output + "/" + name + ".png"; File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path);
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static object Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Hidden).Invoke(target, args);
        private static T Get<T>(object target, string name) => (T)target.GetType().GetField(name, Hidden).GetValue(target);
        private static void Set(object target, string name, object value) => target.GetType().GetField(name, Hidden).SetValue(target, value);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
