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
    public static class ExpeditionShopQa0314
    {
        private const string Output = ".cache/qa/legacy-0.3.14/shop";
        [Serializable] private sealed class Catalog { public int schema; public ShopService.Offer[] offers; }
        [Serializable] private sealed class Result { public bool passed, networkOrAccountActions; public string stage, error; public int assertions, products, labels, purchaseIntents, rubAttempts; }
        private static GameObject _host;
        private static ExpeditionShopPanel _panel;
        private static ShopService.State _state;
        private static string _stage;
        private static int _intents, _rubAttempts, _openedAtIntents;
        private static string _selected;
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
            if (target.StartsWith("quote-", StringComparison.Ordinal))
            {
                _selected = target.Substring(6); var offer = _state.offers.Single(o => o.id == _selected);
                Click("ShopTab-" + offer.tab); _openedAtIntents = _intents; var before = JsonUtility.ToJson(_state);
                Click("ShopBuy-" + offer.id); Require(_intents == _openedAtIntents && JsonUtility.ToJson(_state) == before, "Opening confirmation emitted a purchase or mutated quote/wallet.");
                _stage = "confirmation-" + _selected; return "Single purchase confirmation opened; no RPC, debit or intention before approval.";
            }
            if (target == "cancel")
            {
                Click("ShopCancelPurchase"); Require(_intents == _openedAtIntents, "Cancel emitted a purchase intention.");
                _stage = "cancelled"; return "Cancelled without intention, RPC, debit or sequence mutation.";
            }
            if (target == "confirm" || target == "double-confirm")
            {
                var button = FindButton("ShopConfirmPurchase"); Click("ShopConfirmPurchase");
                if (target == "double-confirm") button.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
                var rub = _state.offers.Single(o => o.id == _selected).currency == "rub";
                Require(_intents == _openedAtIntents + (rub ? 0 : 1), "Confirmation or double tap emitted the wrong number of intentions.");
                if (rub) { _rubAttempts++; Require(_panel.GetComponentsInChildren<TMP_Text>().Any(t => t.name == "PaymentMessage" && t.text == ShopService.PaymentMessage), "Exact payment message missing after RUB confirmation."); }
                _stage = rub ? "payment-stub" : "confirmed-" + _selected; return "Approved once; second tap cannot submit again. RUB remains a local payment stub.";
            }
            if (target == "payment-close") { Click("PaymentUnderstood"); _stage = "rub-closed"; return "Payment stub dismissed."; }
            throw new ArgumentException("Use logic, open-tabs, quote-<offer_id>, cancel, confirm/double-confirm, payment-close, verify or close.");
        }
        private static ShopService.State Fixture()
        {
            var catalog = JsonUtility.FromJson<Catalog>(File.ReadAllText("Server/nakama/data/duel_shop_catalog.json"));
            return new ShopService.State { ok = true, schema = 1, catalog_version = "fixture0314", next_sequence = 1, offers = catalog.offers,
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
                PolicyChecks(report);
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            Write(report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static void PolicyChecks(Result report)
        {
            var state = Fixture(); var owner = new object(); var intents = 0; var wallet = JsonUtility.ToJson(state.wallet);
            ShopPurchaseConfirmation Quote(string id = "ingots_purple", string retry = null) => new(state.offers.Single(o => o.id == id), state.catalog_version, state.next_sequence, "account-A", 7, owner, retry);
            bool Submit(ShopPurchaseConfirmation q, string scope = "account-A", int epoch = 7, object session = null, bool allowed = true, string pending = null)
            { var accepted = q.TryConfirm(state, scope, epoch, session ?? owner, allowed, pending, out _); if (accepted) intents++; return accepted; }
            var cancel = Quote(); cancel.Cancel(); Check(report, !Submit(cancel) && intents == 0, "Cancel must never dispatch.");
            foreach (var id in new[] { "ingots_purple", "boss_teleport", "rub_resources_99" })
            {
                var quote = Quote(id); Check(report, !quote.Consumed, "Preparing a quote already consumed it.");
                var before = intents; Check(report, Submit(quote) && !Submit(quote) && intents == before + 1, "Double approval dispatched twice for " + id);
            }
            var staleVersion = Quote(); state.catalog_version += "-new"; Check(report, !Submit(staleVersion), "A stale catalog version reached dispatch."); state = Fixture();
            var stalePrice = Quote(); state.offers.Single(o => o.id == "ingots_purple").price++; Check(report, !Submit(stalePrice), "A changed price without version update reached dispatch."); state = Fixture();
            var staleContents = Quote(); state.offers.Single(o => o.id == "ingots_purple").items[0].amount++; Check(report, !Submit(staleContents), "Changed quantities reached dispatch."); state = Fixture();
            Check(report, !Submit(Quote(), scope: "account-B"), "Foreign account accepted old approval.");
            Check(report, !Submit(Quote(), epoch: 8), "Locked session epoch accepted old approval.");
            Check(report, !Submit(Quote(), session: new object()), "Replaced authenticated session accepted old approval.");
            Check(report, !Submit(Quote(), allowed: false), "Disconnected or locked account reached dispatch.");
            Check(report, !Submit(Quote(), pending: "different-pending"), "A new purchase bypassed an uncertain previous purchase.");
            var retry = Quote(retry: "same-original-request"); var original = retry.Offer;
            state.catalog_version = "changed-after-lost-ack"; state.next_sequence++; state.offers.Single(o => o.id == original.id).price += 20;
            Check(report, Submit(retry, pending: "same-original-request") && retry.RetryRequestId == "same-original-request" && retry.Offer.price == original.price && retry.Sequence == 1, "Retry changed original request ID, sequence or displayed price."); state = Fixture();
            Check(report, !Submit(Quote(retry: "original"), pending: "replacement"), "Retry switched pending request identity.");
            var immutable = Quote(); var view = immutable.Offer; view.price = 1; view.items[0].amount = 999;
            Check(report, immutable.Offer.price == 5 && immutable.Offer.items[0].amount == 20 && Submit(immutable), "Exposed display DTO mutated approval terms.");
            Check(report, JsonUtility.ToJson(state.wallet) == wallet && state.next_sequence == 1, "Policy checks mutated wallet or server sequence.");
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
                    if (image.name == "RewardIcon" || image.name == "Currency" || image.name == "PriceCurrency" || image.name == "FeaturedArt" || image.name == "ConfirmationRewardIcon" || image.name == "ConfirmationCurrency") Check(report, image.sprite != null, "Shop artwork missing: " + image.transform.parent.name);
                foreach (var button in _panel.GetComponentsInChildren<Button>())
                {
                    var rt = (RectTransform)button.transform; Check(report, rt.rect.width * canvas.scaleFactor >= 44 && rt.rect.height * canvas.scaleFactor >= 44, "Shop touch target below44px: " + button.name);
                    if (button.name.StartsWith("ShopBuy-", StringComparison.Ordinal)) report.products++;
                }
                Check(report, report.products >= 3, "Expected a real product grid.");
                Check(report, _state.wallet.gold == 100000 && _state.wallet.matter == 80 && _state.next_sequence == 1, "Offline preview mutated server-owned purchase state.");
                Check(report, UnityEngine.Object.FindFirstObjectByType<NakamaBootstrap>(FindObjectsInactive.Include) == null, "Shop preview created a network bootstrap.");
                if (_stage.StartsWith("confirmation-", StringComparison.Ordinal))
                {
                    Check(report, _intents == _openedAtIntents, "Unconfirmed preview emitted an intention.");
                    var confirm = FindButton("ShopConfirmPurchase"); var cancel = FindButton("ShopCancelPurchase");
                    Check(report, TopHit(confirm).GetComponentInParent<Button>() == confirm, "Confirm is blocked by another modal canvas.");
                    Check(report, TopHit(cancel).GetComponentInParent<Button>() == cancel, "Cancel is blocked by another modal canvas.");
                    Check(report, confirm.GetComponentInParent<Canvas>().rootCanvas == canvas, "Confirmation escaped its modal canvas.");
                }
                report.passed = true;
            }
            catch (Exception error) { report.error = error.ToString(); }
            Write(report); if (!report.passed) throw new InvalidOperationException(report.error); return JsonUtility.ToJson(report);
        }
        private static void Click(string name)
        {
            var button = FindButton(name); Require(button.gameObject.activeInHierarchy && button.interactable, "Shop control unavailable: " + name);
            button.OnPointerClick(new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left });
        }
        private static Button FindButton(string name) => _panel.GetComponentsInChildren<Button>().Single(b => b.name == name);
        private static GameObject TopHit(Button button)
        {
            // The disposable battle UI scene intentionally has no input host.
            // Register a scene-local EventSystem solely for actual UI raycasts.
            if (EventSystem.current == null)
                new GameObject("OfflineShop0314RaycastEvents", typeof(EventSystem));
            var data = new PointerEventData(EventSystem.current) { position = RectTransformUtility.WorldToScreenPoint(null, button.transform.TransformPoint(((RectTransform)button.transform).rect.center)) };
            var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(data, hits); Require(hits.Count > 0, "No physical raycast hit on confirmation."); return hits[0].gameObject;
        }
        private static void Check(Result report, bool condition, string message) { report.assertions++; Require(condition, message); }
        private static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        private static void Write(Result report) { Directory.CreateDirectory(Output); File.WriteAllText(Path.Combine(Output, "ui-" + report.stage + ".json"), JsonUtility.ToJson(report, true)); }
    }
}
