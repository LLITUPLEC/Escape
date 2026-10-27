using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Project.Character;
using Project.Match3;
using Project.Nakama;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Actual shop renderer and click handlers, isolated fixture data, no RPC or purchase transport.</summary>
    public static class ExpeditionShopQa0313
    {
        private const string Output = ".cache/qa/legacy-0.3.13/shop";
        [Serializable] private sealed class Catalog { public int schema; public ShopService.Offer[] offers; }
        [Serializable] private sealed class Result { public bool passed, networkOrAccountActions; public string stage, error; public int assertions, products, labels, purchaseIntents, rubAttempts; }
        private static GameObject _host;
        private static ExpeditionShopPanel _panel;
        private static ShopService.State _state;
        private static string _stage;
        private static int _intents, _rubAttempts, _cashStartIntents, _cashAttempts;
        public static string Run(string target)
        {
            if (target == "logic") return Logic();
            if (target == "close") { if (_host != null) UnityEngine.Object.Destroy(_host); _host = null; _panel = null; return "Offline shop closed."; }
            Require(EditorApplication.isPlaying && SceneManager.GetActiveScene().name.StartsWith("NotificationStakeQaFixture", StringComparison.Ordinal), "Use only the isolated notification/stake Play fixture.");
            Require(UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null && !UnityEngine.Object.FindFirstObjectByType<DuelMatch3Manager>().enabled, "No active account or match host is permitted.");
            if (target == "verify") return Verify();
            if (_panel == null) Open();
            if (target.StartsWith("open-", StringComparison.Ordinal))
            {
                _stage = target.Substring(5); Click("ShopTab-" + _stage); return "Shop " + _stage + "; wait a rendered frame, verify and capture.";
            }
            if (target == "bottom" || target == "top")
            {
                var scroll = _panel.GetComponentInChildren<ScrollRect>(); scroll.StopMovement(); scroll.verticalNormalizedPosition = target == "top" ? 1 : 0; _stage += "-" + target;
                return "Shop scrolled; wait a rendered frame, verify and capture.";
            }
            if (target == "cash")
            {
                Click("ShopTab-rub"); var before = _intents; _cashStartIntents = before; _cashAttempts = 0; var wallet = JsonUtility.ToJson(_state.wallet);
                foreach (var offer in _state.offers.Where(o => o.currency == "rub")) { Click("ShopBuy-" + offer.id); _rubAttempts++; _cashAttempts++; }
                Require(_intents == before && JsonUtility.ToJson(_state.wallet) == wallet, "A real-money stub changed a wallet or emitted a purchase intention.");
                Require(_panel.GetComponentsInChildren<TMP_Text>().Any(t => t.name == "PaymentMessage" && t.text == ShopService.PaymentMessage), "Exact payment stub message is missing.");
                _stage = "payment-stub"; return "All five real-money attempts displayed the exact payment stub, without a purchase or wallet mutation.";
            }
            if (target == "intent")
            {
                Click("ShopTab-supplies"); var before = _intents; Click("ShopBuy-boss_teleport"); Require(_intents == before + 1, "Gold-token card did not emit one offline intention.");
                _stage = "token-intent"; return "Actual token card handler emitted exactly one offline intention; no RPC or debit.";
            }
            throw new ArgumentException("Use logic, open-bundles/resources/supplies/rub, top, bottom, cash, intent, verify or close.");
        }
        private static ShopService.State Fixture()
        {
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText("Server/nakama/data/duel_shop_catalog.json"));
            return new ShopService.State { ok = true, schema = 1, catalog_version = "fixture0313", next_sequence = 1, offers = catalog.offers,
                wallet = new ShopService.Wallet { gold = 100000, ore = 12000, matter = 80, energy = 1000, energy_max = 20000 } };
        }
        private static string Logic()
        {
            var report = new Result { stage = "logic" };
            try
            {
                var state = Fixture(); Check(report, ShopService.Valid(state) && state.offers.Length == 22, "Canonical shop DTO does not parse.");
                Check(report, state.offers.Select(o => o.id).Distinct().Count() == state.offers.Length, "Duplicate offer IDs.");
                var token = state.offers.Single(o => o.id == "boss_teleport"); Check(report, token.currency == "gold" && token.price == 30000 && token.items.Single().def_id == "global_boss_teleport", "Teleport gold price differs.");
                Check(report, state.offers.Where(o => o.items != null).SelectMany(o => o.items).All(i => !i.def_id.Contains("recipe") && !i.def_id.Contains("equip")), "Mine progression item appears in the shop.");
                var anchor = state.offers.Single(o => o.id == "rub_resources_99"); Check(report, anchor.price == 99 && anchor.gold == 10000 && anchor.ore == 10000, "99-ruble resource anchor differs.");
                state.next_sequence = 0; Check(report, !ShopService.Valid(state), "Zero purchase sequence accepted."); state.next_sequence = 1;
                state.wallet.gold = -1; Check(report, !ShopService.Valid(state), "Negative wallet accepted."); state.wallet.gold = 100000;
                state.catalog_version = null; Check(report, !ShopService.Valid(state), "Missing price revision accepted.");
                Check(report, ShopService.Message("payment_not_connected") == "Оплата ещё не подключена", "Payment text changed.");
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            Write(report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static void Open()
        {
            _state = Fixture(); _intents = _rubAttempts = 0;
            _host = new GameObject("OfflineShopQaCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = _host.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 10000;
            var scaler = _host.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize; scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f;
            var catalog = AssetDatabase.LoadAssetAtPath<ItemCatalog>("Assets/_Project/Data/Character/Items/MainItemCatalog.asset");
            _panel = ExpeditionShopPanel.Open(_host.transform, _state, catalog, offer => { Require(offer.currency != "rub", "Real money reached purchase handler."); _intents++; });
        }
        private static string Verify()
        {
            var report = new Result { stage = _stage, purchaseIntents = _intents, rubAttempts = _rubAttempts };
            try
            {
                Require(_panel != null, "Open the shop first."); Canvas.ForceUpdateCanvases();
                var canvas = _host.GetComponent<Canvas>(); Check(report, canvas.pixelRect.width == 1080 && canvas.pixelRect.height == 1920, "Use native1080x1920 Game view.");
                foreach (var text in _panel.GetComponentsInChildren<TMP_Text>())
                {
                    if (string.IsNullOrEmpty(text.text)) continue; text.ForceMeshUpdate(); report.labels++;
                    Check(report, !text.isTextOverflowing, "Shop text overflow: " + text.name + " / " + text.text + " / " + text.rectTransform.rect);
                }
                foreach (var image in _panel.GetComponentsInChildren<Image>())
                    if (image.name == "RewardIcon" || image.name == "Currency" || image.name == "PriceCurrency" || image.name == "FeaturedArt") Check(report, image.sprite != null, "Shop artwork missing: " + image.transform.parent.name);
                foreach (var button in _panel.GetComponentsInChildren<Button>())
                {
                    var rt = (RectTransform)button.transform; Check(report, rt.rect.width * canvas.scaleFactor >= 44 && rt.rect.height * canvas.scaleFactor >= 44, "Shop touch target below44px: " + button.name);
                    if (button.name.StartsWith("ShopBuy-", StringComparison.Ordinal)) report.products++;
                }
                Check(report, report.products >= 3, "Expected a real product grid.");
                Check(report, _state.wallet.gold == 100000 && _state.wallet.matter == 80 && _state.next_sequence == 1, "Offline preview mutated server-owned purchase state.");
                Check(report, UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null, "Shop preview created a network bootstrap.");
                if (_stage == "payment-stub") Check(report, _cashAttempts == 5 && _intents == _cashStartIntents, "Every real-money offer must remain a stub.");
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            Write(report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static void Click(string name)
        {
            var button = _panel.GetComponentsInChildren<Button>().Single(b => b.name == name); Require(button.gameObject.activeInHierarchy && button.interactable, "Shop control unavailable: " + name);
            button.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
        }
        private static void Check(Result report, bool condition, string message) { report.assertions++; Require(condition, message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Write(Result report) { Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "ui-" + report.stage + ".json"), JsonUtility.ToJson(report, true)); }
    }
}
