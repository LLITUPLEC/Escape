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
    /// <summary>Actual workshop list/detail writers in a disabled, transport-free controller fixture.</summary>
    public static class ExpeditionWorkshopRecipeQa
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const string Output = ".cache/qa/mine-refresh/workshop";
        [Serializable] private sealed class Report
        {
            public bool passed, networkActions, accountWrites;
            public int checkedSlots, deterministicTieCases;
            public List<string> checks = new(), screenshots = new();
        }

        public static void RunBatch()
        {
            EditorSceneManager.OpenScene("Assets/_Project/Scenes/MainMenu.unity", OpenSceneMode.Single);
            Debug.Log(Run());
        }

        public static string Run()
        {
            Check(!EditorApplication.isPlayingOrWillChangePlaymode, "Workshop recipe QA requires Edit mode.");
            Directory.CreateDirectory(Output);
            var report = new Report();
            var originalScene = SceneManager.GetActiveScene(); var previousTexture = RenderTexture.active;
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive); SceneManager.SetActiveScene(scene);
            Camera camera = null; RenderTexture target = null; GameObject controllerRoot = null;
            ItemCatalog tieCatalog = null; ItemDefinition tieA = null, tieZ = null;
            try
            {
                camera = new GameObject("WorkshopRecipeQaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.orthographic = true; camera.orthographicSize = 960; camera.nearClipPlane = .1f; camera.farClipPlane = 100;
                camera.transform.position = new Vector3(0, 0, -10); camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel; camera.cullingMask = 1 << 31;
                target = new RenderTexture(1080, 1920, 24, RenderTextureFormat.ARGB32); target.Create(); camera.targetTexture = target;
                var canvas = new GameObject("WorkshopRecipeQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                canvas.additionalShaderChannels = AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.Normal | AdditionalCanvasShaderChannels.Tangent;
                canvas.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
                var background = ExpeditionUi.Fill(canvas.transform, "WorkshopBackground", 0, 0, 1, 1, Color.white);
                background.sprite = Resources.Load<Sprite>("Expedition/Art/workshop-backdrop");
                var slots = ExpeditionUi.Rect(background.transform, "CraftSlots", 0, 0, 1, 1);
                for (var i = 0; i < 8; i++)
                {
                    var slot = ExpeditionUi.Panel(slots, "Slot_" + i, 0, 0, 1, 1);
                    slot.gameObject.AddComponent<Button>();
                    LegacyText(slot.transform, "SlotLabel", "", 20);
                    LegacyText(slot.transform, "Timer", "", 18);
                    ExpeditionUi.Fill(slot.transform, "Icon", 0, 0, 1, 1, Color.white);
                }
                var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
                Check(catalog != null, "Shipping item catalog is missing.");
                controllerRoot = new GameObject("DisabledWorkshopRecipeQaController"); controllerRoot.SetActive(false);
                var controller = controllerRoot.AddComponent<WorkshopSceneController>(); controller.enabled = false;
                Set(controller, "itemCatalog", catalog); Set(controller, "workshopBackground", background.rectTransform);
                Set(controller, "craftSlotsRoot", slots); Set(controller, "applyDefaultWorkshopAnchors", false); Set(controller, "applyEquipmentStyleCraftLayout", false);
                Call(controller, "BuildExtraUiIfNeeded"); Call(controller, "WireCraftSlots");
                var layout = ExpeditionWorkshopLayout.Install(background.rectTransform, slots);
                Set(controller, "_expeditionLayout", layout);
                var profile = Profile(catalog);
                Set(controller, "_profile", profile); Call(controller, "ApplyProfile", profile);
                for (var slot = 0; slot < 8; slot++)
                {
                    Call(controller, "OnSlotClicked", slot);
                    var rows = Rows(controller);
                    Check(rows.Length == 4, "Each slot should expose its four learned recipes: " + slot);
                    var expected = new[] { ItemQualityTier.Legendary, ItemQualityTier.Epic, ItemQualityTier.Rare, ItemQualityTier.Normal };
                    for (var i = 0; i < rows.Length; i++)
                    {
                        var definition = catalog.Get(rows[i].name.Substring("Recipe_".Length));
                        Check(definition != null && definition.Quality == expected[i], "Rarity order is wrong for slot " + slot);
                        Check(rows[i].transform.Find("RecipeIconFrame/RecipeResultIcon").GetComponent<Image>().sprite == catalog.GetDisplayIcon(definition), "Row result icon differs from the crafted item.");
                        Check(rows[i].transform.Find("RarityBorder").GetComponent<Image>().color == EquipmentSlotView.QualitySlotColor(definition.Quality), "Row border lost its rarity color.");
                    }
                    Check(Get<string>(controller, "_selectedOutputDefId") == rows[0].name.Substring("Recipe_".Length), "A new queue did not select its highest quality learned recipe.");
                    report.checkedSlots++;
                }
                report.checks.Add("All8 queues order learned results Legendary/Epic/Rare/Common, show actual result sprites, and preserve rarity borders during selection.");

                tieA = UnityEngine.Object.Instantiate(catalog.Get("eq_t1_legendary_helmet"));
                tieZ = UnityEngine.Object.Instantiate(tieA);
                Set(tieA, "itemId", "qa_legendary_helmet_a"); Set(tieA, "craftRecipeId", "qa_recipe_a");
                Set(tieZ, "itemId", "qa_legendary_helmet_z"); Set(tieZ, "craftRecipeId", "qa_recipe_z");
                tieCatalog = UnityEngine.Object.Instantiate(catalog);
                var entries = catalog.EnumerateDefinitions().Reverse().Concat(new[] { tieZ, tieA }).ToList();
                Set(tieCatalog, "items", entries); Call(tieCatalog, "RebuildMap");
                var originalLearned = profile.learned_recipe_ids;
                profile.learned_recipe_ids = originalLearned.Concat(new[] { "qa_recipe_z", "qa_recipe_a" }).ToArray();
                Set(controller, "itemCatalog", tieCatalog); Call(controller, "OnSlotClicked", 0);
                var firstOrder = Rows(controller).Select(row => row.name).ToArray();
                Check(Array.IndexOf(firstOrder, "Recipe_qa_legendary_helmet_a") < Array.IndexOf(firstOrder, "Recipe_qa_legendary_helmet_z"), "Equal rarity/tier must use ordinal item IDs.");
                entries.Reverse(); Set(tieCatalog, "items", entries); Call(tieCatalog, "RebuildMap"); Call(controller, "RebuildRecipeList");
                Check(firstOrder.SequenceEqual(Rows(controller).Select(row => row.name)), "Recipe order depends on source catalog ordering.");
                report.deterministicTieCases = 2;
                profile.learned_recipe_ids = originalLearned; Set(controller, "itemCatalog", catalog);
                Call(controller, "OnSlotClicked", 0); layout.OpenRecipes();
                Save(canvas, camera, target, "learned-recipes-sorted", report);

                var legendary = catalog.Get("eq_t1_legendary_helmet");
                Rows(controller).Single(row => row.name == "Recipe_" + legendary.ItemId).GetComponent<Button>().onClick.Invoke();
                var required = catalog.Get(legendary.CraftItemId);
                VerifyPrerequisite(layout, required, true);
                Check(Get<Button>(controller, "_createButton").interactable, "All supplied materials and the prerequisite should permit creating.");
                Save(canvas, camera, target, "legendary-requirements-owned", report);
                var requiredIndex = Array.IndexOf(profile.inventory_def_ids, required.ItemId);
                Check(requiredIndex >= 0, "Owned prerequisite fixture is missing."); profile.inventory_counts[requiredIndex] = 0;
                Call(controller, "UpdateDetailPanel"); VerifyPrerequisite(layout, required, false);
                Check(!Get<Button>(controller, "_createButton").interactable, "Missing prerequisite must still block crafting.");
                Save(canvas, camera, target, "legendary-requirements-missing", report);
                profile.inventory_counts[requiredIndex] = 1;
                Rows(controller).Single(row => row.name == "Recipe_eq_t1_normal_helmet").GetComponent<Button>().onClick.Invoke();
                Check(!layout.RecipePanel.Find("PrerequisiteItem").gameObject.activeSelf, "Basic recipe left the previous prerequisite visible.");
                Save(canvas, camera, target, "basic-requirements-no-prerequisite", report);
                Set(controller, "_selectedOutputDefId", legendary.ItemId);
                profile.workshop_output_def_ids[0] = legendary.ItemId; profile.workshop_ends_at[0] = (int)DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 300;
                Call(controller, "UpdateDetailPanel");
                Check(!layout.RecipePanel.Find("PrerequisiteItem").gameObject.activeSelf, "Running craft left a stale prerequisite visible.");
                profile.workshop_ends_at[0] = 1; Call(controller, "UpdateDetailPanel");
                Check(!layout.RecipePanel.Find("PrerequisiteItem").gameObject.activeSelf, "Ready craft left a stale prerequisite visible.");
                profile.workshop_output_def_ids[0] = string.Empty;
                profile.learned_recipe_ids = profile.learned_recipe_ids.Where(id => id != legendary.CraftRecipeId).ToArray();
                Call(controller, "RebuildRecipeList");
                Check(!Rows(controller).Any(row => row.name == "Recipe_" + legendary.ItemId), "An unlearned recipe appeared in the chooser.");
                report.checks.Add("Exact prerequisite item icon/name/rarity and owned/missing state match live craft gating; basic/busy/ready states clear old gear; unlearned recipes remain excluded.");
                Check(!controllerRoot.activeInHierarchy && !controller.enabled, "The offline controller was activated.");
                report.passed = true;
            }
            finally
            {
                RenderTexture.active = previousTexture;
                if (camera != null) camera.targetTexture = null;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (controllerRoot != null) UnityEngine.Object.DestroyImmediate(controllerRoot);
                foreach (var temporary in new UnityEngine.Object[] { tieCatalog, tieA, tieZ }) if (temporary != null) UnityEngine.Object.DestroyImmediate(temporary);
                EditorSceneManager.CloseScene(scene, true);
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                File.WriteAllText(Output + "/workshop-recipes-report.json", JsonUtility.ToJson(report, true));
            }
            return "PASS workshop recipes:8 queue sorts, deterministic ties, real result/rarity art and prerequisite owned/missing/basic/busy/ready states; native screenshots. No network/account actions.";
        }

        private static CharacterGetRpcResponse Profile(ItemCatalog catalog)
        {
            var ids = catalog.EnumerateDefinitions().Where(item => item.Kind == ItemKind.Equipment).Select(item => item.ItemId)
                .Concat(new[] { "ingot_green", "ingot_blue", "ingot_purple", "tesseract" }).ToArray();
            return new CharacterGetRpcResponse
            { ok = true, progression = new Progression { level = 12, ore = 1000000, gold = 1000000 }, equipment_def_ids = new string[8],
                inventory_def_ids = ids, inventory_counts = ids.Select(id => id.StartsWith("eq_", StringComparison.Ordinal) ? 1 : 500).ToArray(),
                learned_recipe_ids = catalog.EnumerateDefinitions().Where(item => item.Kind == ItemKind.Recipe).Select(item => item.ItemId).ToArray(),
                workshop_output_def_ids = new string[8], workshop_ends_at = new int[8] };
        }

        private static void VerifyPrerequisite(ExpeditionWorkshopLayout layout, ItemDefinition item, bool owned)
        {
            var root = layout.RecipePanel.Find("PrerequisiteItem");
            Check(root != null && root.gameObject.activeSelf, "Prerequisite gear row is hidden.");
            Check(root.Find("PrerequisiteFrame/PrerequisiteIcon").GetComponent<Image>().sprite == item.Icon, "Wrong prerequisite gear sprite.");
            Check(root.Find("PrerequisiteFrame/RarityBorder").GetComponent<Image>().color == EquipmentSlotView.QualitySlotColor(item.Quality), "Prerequisite rarity frame differs from the required gear.");
            Check(root.Find("FodderRequirement").GetComponent<Text>().text == item.DisplayName, "Prerequisite gear name differs from the item catalog.");
            var status = root.Find("PrerequisiteStatus").GetComponent<Text>().text;
            Check(owned ? status.Contains("Есть") && status.Contains("поглощён") : status.Contains("Нет в наличии"), "Prerequisite availability status is stale.");
            Canvas.ForceUpdateCanvases();
            var label = root.Find("PrerequisiteStatus").GetComponent<Text>();
            Check(label.canvasRenderer.GetMesh() != null && label.canvasRenderer.GetMesh().vertexCount > 0, "Prerequisite availability is clipped instead of rendered.");
        }

        private static void Save(Canvas canvas, Camera camera, RenderTexture target, string name, Report report)
        {
            foreach (var child in canvas.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            foreach (var text in canvas.GetComponentsInChildren<TMP_Text>()) text.ForceMeshUpdate();
            void Render() { if (GraphicsSettings.currentRenderPipeline != null) RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target }); else camera.Render(); }
            Render(); Canvas.ForceUpdateCanvases(); Render(); RenderTexture.active = target;
            var pixels = new Texture2D(1080, 1920, TextureFormat.RGB24, false);
            try { pixels.ReadPixels(new Rect(0, 0, 1080, 1920), 0, 0); pixels.Apply(); var path = Output + "/" + name + ".png"; File.WriteAllBytes(path, pixels.EncodeToPNG()); report.screenshots.Add(path); }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static Text LegacyText(Transform parent, string name, string value, int size)
        {
            var text = ExpeditionUi.Rect(parent, name, 0, 0, 1, 1).gameObject.AddComponent<Text>();
            text.font = ExpeditionTheme.LegacyFont; text.text = value; text.fontSize = size; text.raycastTarget = false; return text;
        }
        private static GameObject[] Rows(WorkshopSceneController controller) => Get<List<GameObject>>(controller, "_recipeRows").Where(row => row != null && row.activeSelf && row.GetComponent<Button>() != null).ToArray();
        private static T Get<T>(object target, string field) => (T)target.GetType().GetField(field, Hidden).GetValue(target);
        private static void Set(object target, string field, object value) => target.GetType().GetField(field, Hidden).SetValue(target, value);
        private static object Call(object target, string method, params object[] arguments) => target.GetType().GetMethod(method, Hidden).Invoke(target, arguments);
        private static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    }
}
