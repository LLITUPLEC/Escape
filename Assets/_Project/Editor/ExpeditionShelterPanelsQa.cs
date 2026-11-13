using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Project.Character;
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
    /// <summary>Offline regression fixtures for the shop, supplies chest and settings console.</summary>
    public static class ExpeditionShelterPanelsQa
    {
        private const string Output = ".cache/qa/shelter-panels";
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;

        [Serializable]
        private sealed class Catalog
        {
            public ShopService.Offer[] offers;
        }

        public static void RunBatch()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && NakamaBootstrap.Instance == null, "Use an offline Edit Mode session.");
            Directory.CreateDirectory(Output);
            var original = SceneManager.GetActiveScene();
            var originalTarget = RenderTexture.active;
            try
            {
                foreach (var height in new[] { 1920, 2400 }) CheckPortrait(height);
                Require(NakamaBootstrap.Instance == null, "The fixture created a Nakama bootstrap.");
                Debug.Log("PASS shelter panels QA. Portrait fixtures rendered at 1080×1920 and 1080×2400.");
            }
            finally
            {
                RenderTexture.active = originalTarget;
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            }
        }

        private static void CheckPortrait(int height)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            var target = new RenderTexture(1080, height, 24, RenderTextureFormat.ARGB32);
            target.Create();
            try
            {
                var camera = new GameObject("ShelterQaCamera", typeof(Camera)).GetComponent<Camera>();
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = height * .5f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = ExpeditionTheme.Panel;
                camera.cullingMask = 1 << 31;
                camera.targetTexture = target;
                var canvas = new GameObject("ShelterQaCanvas", typeof(RectTransform), typeof(Canvas)).GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceCamera;
                canvas.worldCamera = camera;
                canvas.planeDistance = 1;
                canvas.scaleFactor = Mathf.Sqrt(height / 1920f);
                CheckShop(canvas.transform, camera, target);
                CheckSupplies(canvas.transform, camera, target);
                CheckSettings(canvas.transform, camera, target);
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
                RenderTexture.active = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void CheckShop(Transform canvas, Camera camera, RenderTexture target)
        {
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText("Server/nakama/data/duel_shop_catalog.json"));
            var state = new ShopService.State
            {
                ok = true, schema = 1, catalog_version = "offline-fixture", next_sequence = 1,
                offers = catalog.offers, wallet = new ShopService.Wallet { gold = 100000, ore = 12000, matter = 80, energy = 1000, energy_max = 20000 }
            };
            var intentions = 0;
            var panel = ExpeditionShopPanel.Open(canvas, state, AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset"), _ => intentions++);
            RequireImage(panel.transform, "QuartermasterCounter");
            var tabFrame = Resources.Load<Sprite>("Expedition/Shop/Frames/shelf-tab");
            var productFrame = Resources.Load<Sprite>("Expedition/Shop/Frames/product-frame");
            Require(tabFrame != null && tabFrame.border != Vector4.zero && productFrame != null && productFrame.border != Vector4.zero,
                "Shop signs and product frames must import with nine-slice borders.");
            var tabs = new[] { "bundles", "resources", "rub", "supplies" };
            var products = Find(panel.transform, "ShopProducts");
            foreach (var selected in tabs)
            {
                if (selected != "bundles")
                {
                    while (products.childCount > 0) UnityEngine.Object.DestroyImmediate(products.GetChild(0).gameObject);
                    Click(panel.transform, "ShopTab-" + selected);
                }
                Canvas.ForceUpdateCanvases();
                foreach (var tab in tabs)
                {
                    var sign = Find(panel.transform, "ShopTab-" + tab);
                    var frame = sign.GetComponent<Image>();
                    var label = sign.Find("Label").GetComponent<TMP_Text>();
                    label.ForceMeshUpdate();
                    Require(frame.sprite == tabFrame && frame.type == Image.Type.Sliced && frame.raycastTarget &&
                        (frame.color == Color.white) == (tab == selected) &&
                        !label.isTextOverflowing && label.textInfo.lineCount == 1,
                        "Shop sign frame, selection or label does not fit: " + tab);
                }
                Require(products.childCount == state.offers.Count(item => item.tab == selected), "The shelf sign did not select its inventory.");
                foreach (Transform product in products)
                {
                    var frame = product.GetComponent<Image>();
                    Require(frame.sprite == productFrame && frame.type == Image.Type.Sliced && !frame.raycastTarget,
                        "Product should use its own decorative frame: " + product.name);
                }
                if (selected == "bundles" || selected == "supplies")
                    Capture(canvas, camera, target, selected == "bundles" ? "shop" : "shop-supplies");
            }
            var offer = state.offers.First(item => item.tab == "supplies");
            Click(panel.transform, "ShopBuy-" + offer.id);
            var quote = Find(panel.transform, "ShopPurchaseConfirmation");
            Require(Find(quote, "ConfirmationOfferTitle").GetComponent<TMP_Text>().text == offer.title && intentions == 0,
                "Inspecting a price tag did not preserve the offer or emitted a purchase.");
            Capture(canvas, camera, target, "shop-quote");
            UnityEngine.Object.DestroyImmediate(panel.gameObject);
        }

        private static void CheckSupplies(Transform canvas, Camera camera, RenderTexture target)
        {
            var host = ExpeditionUi.Rect(canvas, "SuppliesFixture", 0, 0, 1, 1);
            var header = host.gameObject.AddComponent<ExpeditionResourceHeader>();
            Set(header, "_modalHost", host);
            foreach (var field in new[] { "_gold", "_ore", "_matter", "_energy" })
                Set(header, field, ExpeditionUi.Text(host, field, "", 18, 0, 0, 0, 0));
            Call(header, "OpenDrawer");
            RequireImage(host, "QuartermasterChest");
            var quantities = new PlayerResourcesRpcResponse { gold = 417, ore = 321, matter = 92, energy = 16, ingots = 27, keys = 12, miner_key = 7, dark_key = 5 };
            Call(header, "RenderResources", quantities);
            var compartments = host.GetComponentsInChildren<ExpeditionSupplyFrame>();
            Require(compartments.All(frame => frame.Shape == ExpeditionSupplyFrame.Silhouette.Compartment && !frame.raycastTarget) &&
                compartments.Select(frame => frame.name).SequenceEqual(new[] { "Supply_gold", "Supply_ore", "Supply_matter", "Supply_energy", "Supply_ingots" }),
                "The chest should contain exactly five decorative resource compartments.");
            Require(!host.GetComponentsInChildren<Transform>(true).Any(node => node.name is "Supply_keys" or "Supply_miner_key" or "Supply_dark_key"),
                "The chest still includes a key compartment.");
            var values = compartments.Select(frame => frame.transform.Find("Value").GetComponent<TMP_Text>()).ToArray();
            Require(values.Select(value => value.text).SequenceEqual(new[] { "417", "321", "92", "16", "27" }),
                "The supply compartments did not show their authoritative balances.");
            Capture(canvas, camera, target, "supplies");
            Call(header, "RenderResources", (object)null);
            Require(values.All(value => value.text == "—"), "Previous account balances survived resource invalidation.");
            Click(host, "ExpeditionOpenSuppliesClose");
            Require(!Find(host, "ExpeditionSupplies").gameObject.activeSelf, "Closing the chest did not dismiss the modal.");
            UnityEngine.Object.DestroyImmediate(host.gameObject);
        }

        private static void CheckSettings(Transform canvas, Camera camera, RenderTexture target)
        {
            var host = ExpeditionUi.Rect(canvas, "SettingsFixture", 0, 0, 1, 1);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/MainMenu/SettingsModal.prefab");
            var modal = UnityEngine.Object.Instantiate(prefab, host, false);
            var controller = host.gameObject.AddComponent<MainMenuSettingsModalController>();
            Set(controller, "settingsModalRoot", modal);
            Call(controller, "Awake");
            modal.SetActive(true);
            var panel = modal.transform.Find("Panel");
            RequireImage(panel, "ShelterControlConsole");
            Require(panel.GetComponentsInChildren<Toggle>().Length == 3 && !panel.Find("EmailInputSlot").gameObject.activeSelf,
                "Device view did not show the three audio controls by default.");
            Capture(canvas, camera, target, "settings-device");
            Click(panel, "SettingsAccountTab");
            var inputs = panel.GetComponentsInChildren<InputField>();
            Require(inputs.Length == 2 && panel.GetComponentsInChildren<Toggle>().Length == 0 &&
                panel.Find("LoginButton").gameObject.activeSelf && panel.Find("DeleteAccountButton").gameObject.activeSelf,
                "The account plate did not expose both fields and the account actions.");
            inputs[0].SetTextWithoutNotify("offline@example.invalid");
            panel.Find("Status").GetComponent<TMP_Text>().text = "Профиль экспедиции";
            panel.Find("LinkedEmailLine").GetComponent<TMP_Text>().text = "E-mail не привязан";
            Capture(canvas, camera, target, "settings-account");
            Click(panel, "SettingsDeviceTab");
            Require(panel.GetComponentsInChildren<Toggle>().Length == 3 && !inputs[0].gameObject.activeInHierarchy &&
                Find(panel, "TutorialReplayButton").gameObject.activeInHierarchy && Find(panel, "NicknameButton").gameObject.activeInHierarchy,
                "Switching back did not restore the three sound controls, replay and rename actions.");
            Click(panel, "SettingsAccountTab");
            Require(inputs[0].text == "offline@example.invalid", "Tab navigation discarded input.");
            Click(panel, "CloseButton");
            Require(!modal.activeSelf, "Closing settings did not dismiss the modal.");
            UnityEngine.Object.DestroyImmediate(host.gameObject);
        }

        private static void RequireImage(Transform root, string name) =>
            Require(Find(root, name).Find("Painting").GetComponent<Image>().sprite != null, "Missing illustration: " + name);

        private static Transform Find(Transform root, string name) =>
            ExpeditionUi.Find(root, name) ?? throw new InvalidOperationException("Missing element: " + name);

        private static void Click(Transform root, string name)
        {
            var button = Find(root, name).GetComponent<Button>();
            Require(button != null && button.isActiveAndEnabled && button.IsInteractable(), "Unavailable control: " + name);
            button.onClick.Invoke();
        }

        private static void Capture(Transform canvas, Camera camera, RenderTexture target, string name)
        {
            foreach (var item in canvas.GetComponentsInChildren<Transform>(true)) item.gameObject.layer = 31;
            Canvas.ForceUpdateCanvases();
            if (GraphicsSettings.currentRenderPipeline != null)
                RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
            else camera.Render();
            RenderTexture.active = target;
            var pixels = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            try
            {
                pixels.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
                pixels.Apply();
                File.WriteAllBytes(Path.Combine(Output, $"{name}-{target.width}x{target.height}.png"), pixels.EncodeToPNG());
            }
            finally { UnityEngine.Object.DestroyImmediate(pixels); }
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, Instance).SetValue(target, value);

        private static void Call(object target, string method, params object[] args) =>
            target.GetType().GetMethod(method, Instance).Invoke(target, args);

        private static void Require(bool result, string message)
        {
            if (!result) throw new InvalidOperationException(message);
        }
    }
}
