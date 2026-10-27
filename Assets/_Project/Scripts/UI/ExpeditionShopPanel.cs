using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Project.Character;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>A scrollable mobile storefront. Prices and rewards always come from the server quote.</summary>
    public sealed class ExpeditionShopPanel : MonoBehaviour
    {
        private readonly Dictionary<string, Button> _tabs = new();
        private readonly Dictionary<string, TMP_Text> _balances = new();
        private readonly List<(Button Button, ShopService.Offer Offer)> _buttons = new();
        private string _tab = "bundles", _shownCatalog, _fixtureMessage;
        private ShopService.State _fixture;
        private Action<ShopService.Offer> _fixturePurchase;
        private CancellationTokenSource _cts;
        private RectTransform _content, _payment, _confirmation;
        private ShopPurchaseConfirmation _quote;
        private readonly object _fixtureOwner = new();
        private TMP_Text _status, _sectionTitle;
        private ItemCatalog _catalog;
        private ScrollRect _scroll;
        public static void Open()
        {
            if (FindFirstObjectByType<ExpeditionShopPanel>() != null) return;
            var host = GameObject.Find("ExpeditionModalHost")?.GetComponent<Canvas>()?.rootCanvas;
            if (host == null) host = GameObject.Find("ExpeditionScenePageHost")?.GetComponentInParent<Canvas>()?.rootCanvas;
            if (host == null) host = FindObjectsByType<Canvas>(FindObjectsSortMode.None).FirstOrDefault(c => c.isRootCanvas && c.gameObject.activeInHierarchy && c.name != "ServerConnectionNotice");
            if (host != null) Open(host.transform);
        }
        public static ExpeditionShopPanel Open(Transform host, ShopService.State fixture = null, ItemCatalog catalog = null, Action<ShopService.Offer> fixturePurchase = null)
        {
            var root = ExpeditionUi.Rect(host, "ExpeditionShopModal", 0, 0, 1, 1).gameObject;
            var view = root.AddComponent<ExpeditionShopPanel>(); view._fixture = fixture; view._fixturePurchase = fixturePurchase;
            view._catalog = catalog ?? Resources.FindObjectsOfTypeAll<ItemCatalog>().FirstOrDefault(c => c.Get("ingot_green") != null);
            view.Build(); view.Refresh();
            if (fixture == null) _ = ShopService.RefreshAsync(view._cts.Token);
            return view;
        }
        public static Sprite TokenIcon => Resources.Load<Sprite>("Expedition/Shop/0313/global_boss_teleport") ?? ExpeditionTheme.Icon("energy");
        private void Build()
        {
            _cts = new CancellationTokenSource(); if (_fixture == null) ShopService.Changed += Refresh;
            ExpeditionUi.Fill(transform, "ShopShade", 0, 0, 1, 1, new Color(0, 0, 0, .88f)).raycastTarget = true;
            var safe = ExpeditionUi.Rect(transform, "ShopSafeArea", 0, 0, 1, 1); safe.gameObject.AddComponent<ExpeditionSafeArea>();
            var panel = ExpeditionUi.Panel(safe, "ShopPanel", .025f, .035f, .975f, .965f).rectTransform;
            Label(panel, "ShopKicker", "ПРИПАСЫ ЭКСПЕДИЦИИ", 19, .055f, .955f, .88f, .98f).color = ExpeditionTheme.Amber;
            Label(panel, "ShopTitle", "ЛАВКА УБЕЖИЩА", 38, .05f, .913f, .865f, .957f).fontStyle = FontStyles.Bold;
            ExpeditionUi.Button(panel, "ShopClose", "×", .897f, .92f, .974f, .975f, () => Destroy(gameObject), false, 42);
            var currency = new[] { "gold", "ore", "matter" };
            for (var i = 0; i < currency.Length; i++)
            {
                var x = .045f + i * .308f;
                var pill = ExpeditionUi.Panel(panel, "ShopBalance-" + currency[i], x, .837f, x + .292f, .902f).rectTransform;
                Picture(pill, "Currency", ExpeditionResourceIcons.Get(currency[i]), .035f, .11f, .25f, .89f);
                _balances[currency[i]] = Label(pill, "Balance", "—", 29, .28f, .10f, .95f, .90f); _balances[currency[i]].fontStyle = FontStyles.Bold;
            }
            var ids = new[] { "bundles", "resources", "supplies", "rub" }; var names = new[] { "Наборы", "Ресурсы", "Припасы", "За рубли" };
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i]; var x = .045f + i * .231f;
                _tabs[id] = ExpeditionUi.Button(panel, "ShopTab-" + id, names[i], x, .765f, x + .217f, .825f, () => SelectTab(id), false, 24); Fit(_tabs[id], 24);
            }
            _sectionTitle = Label(panel, "ShopSection", "СОБЕРИТЕ СВОЙ ЗАПАС", 21, .055f, .727f, .945f, .758f); _sectionTitle.color = ExpeditionTheme.Muted;
            var viewport = ExpeditionUi.Rect(panel, "ShopViewport", .04f, .14f, .96f, .722f);
            viewport.gameObject.AddComponent<RectMask2D>(); var hit = viewport.gameObject.AddComponent<Image>(); hit.color = Color.clear; hit.raycastTarget = true;
            _content = ExpeditionUi.Rect(viewport, "ShopProducts", 0, 0, 1, 1);
            _content.anchorMin = new Vector2(0, 1); _content.anchorMax = Vector2.one; _content.pivot = new Vector2(.5f, 1); _content.anchoredPosition = Vector2.zero;
            _scroll = viewport.gameObject.AddComponent<ScrollRect>(); _scroll.viewport = viewport; _scroll.content = _content;
            _scroll.horizontal = false; _scroll.vertical = true; _scroll.movementType = ScrollRect.MovementType.Clamped; _scroll.scrollSensitivity = 36;
            _status = Label(panel, "ShopStatus", "Загружаем витрину…", 21, .055f, .025f, .73f, .12f);
            var refresh = ExpeditionUi.Button(panel, "ShopRefresh", "Обновить", .755f, .04f, .951f, .105f, () =>
            { _shownCatalog = null; if (_fixture != null) Refresh(); else _ = ShopService.RefreshAsync(_cts.Token); }, false, 22); Fit(refresh, 22);
        }
        public void SelectTab(string tab)
        {
            if (!_tabs.ContainsKey(tab)) return; _tab = tab; _shownCatalog = null; Refresh();
        }
        private void Refresh()
        {
            if (_content == null) return;
            var state = _fixture ?? ShopService.Current; var busy = _fixture == null && ShopService.Busy;
            foreach (var pair in _tabs)
            {
                var text = pair.Value.GetComponentInChildren<TMP_Text>(); text.color = pair.Key == _tab ? ExpeditionTheme.Amber : ExpeditionTheme.Ivory;
                text.fontStyle = pair.Key == _tab ? FontStyles.Bold : FontStyles.Normal;
                var marker = pair.Value.transform.Find("SelectedTab");
                if (marker == null) marker = ExpeditionUi.Fill(pair.Value.transform, "SelectedTab", .06f, .015f, .94f, .055f, ExpeditionTheme.Amber).transform;
                marker.gameObject.SetActive(pair.Key == _tab);
            }
            _sectionTitle.text = _tab == "rub" ? "МАТЕРИЯ И ПРИПАСЫ ДЛЯ УБЕЖИЩА" : _tab == "supplies" ? "ПОДГОТОВЬТЕСЬ К СЛЕДУЮЩЕМУ БОЮ" : _tab == "resources" ? "ДЛЯ РЕМЕСЛА И РАЗВИТИЯ" : "ВСЁ НУЖНОЕ В ОДНОМ НАБОРЕ";
            if (!ShopService.Valid(state))
            {
                DismissConfirmation(true);
                foreach (var balance in _balances.Values) balance.text = "—";
                foreach (Transform child in _content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
                _buttons.Clear(); _shownCatalog = null;
                _status.text = _fixtureMessage ?? ShopService.Error ?? "Загружаем витрину…"; return;
            }
            _balances["gold"].text = CompactResourceAmount.Format(state.wallet.gold); _balances["ore"].text = CompactResourceAmount.Format(state.wallet.ore); _balances["matter"].text = CompactResourceAmount.Format(state.wallet.matter);
            if (_shownCatalog != state.catalog_version + "/" + _tab)
            {
                _shownCatalog = state.catalog_version + "/" + _tab; BuildProducts(state.offers.Where(o => o.tab == _tab).ToArray());
            }
            foreach (var entry in _buttons)
            {
                var pending = _fixture == null ? ShopService.PendingOfferId : null;
                entry.Button.interactable = entry.Offer.currency == "rub" || !busy && (pending == null || pending == entry.Offer.id);
            }
            var receipt = state.receipt;
            _status.text = _fixtureMessage ?? (_fixture == null ? ShopService.Error : null) ?? (busy ? "Подтверждаем покупку…" : receipt?.pending_items?.Length > 0 ? "Покупка получена. Предметы ждут места в инвентаре." : receipt != null ? "Покупка получена. Припасы уже у вас." : _tab == "rub" ? "Выберите набор материи или ресурсов." : "Цена и полный состав указаны на карточке.");
            _status.color = _status.text == ShopService.PaymentMessage ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
        }
        private void BuildProducts(ShopService.Offer[] offers)
        {
            foreach (Transform child in _content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            _buttons.Clear(); _scroll.StopMovement(); _scroll.verticalNormalizedPosition = 1;
            var featured = offers.FirstOrDefault(o => o.featured); var top = 8f;
            if (featured != null) { BuildCard(featured, .012f, .988f, top, 330, true); top += 346; }
            var rest = offers.Where(o => !ReferenceEquals(o, featured)).ToArray();
            for (var i = 0; i < rest.Length; i++)
            {
                var column = i % 2; var row = i / 2;
                BuildCard(rest[i], column == 0 ? .012f : .512f, column == 0 ? .488f : .988f, top + row * 322, 306, false);
            }
            _content.sizeDelta = new Vector2(0, top + Mathf.CeilToInt(rest.Length / 2f) * 322 + 8);
            _content.anchoredPosition = Vector2.zero;
        }
        private void BuildCard(ShopService.Offer offer, float x0, float x1, float top, float height, bool featured)
        {
            var image = ExpeditionUi.Panel(_content, "ShopOffer-" + offer.id, x0, 0, x1, 1); var card = image.rectTransform;
            card.SetInsetAndSizeFromParentEdge(RectTransform.Edge.Top, top, height); image.pixelsPerUnitMultiplier = 5;
            var accent = offer.currency == "rub" ? new Color32(176, 147, 222, 255) : offer.currency == "gold" ? ExpeditionTheme.Amber : new Color32(101, 204, 208, 255);
            ExpeditionUi.Fill(card, "OfferAccent", .035f, .97f, .965f, .985f, accent);
            var title = Label(card, "OfferTitle", offer.title, featured ? 32 : 27, featured ? .255f : .055f, featured ? .735f : .71f, .955f, featured ? .94f : .91f); title.fontStyle = FontStyles.Bold;
            var description = Label(card, "OfferDescription", offer.description, featured ? 22 : 20, featured ? .255f : .055f, featured ? .51f : .52f, .955f, featured ? .735f : .71f); description.color = ExpeditionTheme.Muted;
            if (!string.IsNullOrEmpty(offer.badge))
            {
                var badge = Label(card, "OfferBadge", offer.badge, featured ? 18 : 16, .055f, .905f, featured ? .24f : .95f, .969f);
                badge.color = accent; badge.alignment = featured ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineRight;
            }
            if (featured) Picture(card, "FeaturedArt", MainIcon(offer), .035f, .20f, .235f, .89f);
            BuildRewards(card, offer, featured ? .25f : .05f, featured ? .185f : .225f, .955f, featured ? .505f : .52f);
            if (featured) Label(card, "PriceHint", offer.currency == "rub" ? "НАБОР ДЛЯ УБЕЖИЩА" : offer.currency == "gold" ? "ОДНА ПОПЫТКА БОЯ" : "ПРИПАСЫ ЗА МАТЕРИЮ", 18, .255f, .035f, .68f, .17f).color = accent;
            var button = ExpeditionUi.Button(card, "ShopBuy-" + offer.id, "", featured ? .70f : .10f, .035f, featured ? .955f : .90f, featured ? .175f : .205f, () => Purchase(offer), true, 25);
            var old = button.GetComponentInChildren<TMP_Text>(); if (old != null) old.gameObject.SetActive(false);
            if (offer.currency != "rub") Picture(button.transform, "PriceCurrency", ExpeditionResourceIcons.Get(offer.currency), .05f, .12f, .29f, .88f);
            Label(button.transform, "PriceValue", Number(offer.price) + (offer.currency == "rub" ? " ₽" : ""), 26, offer.currency == "rub" ? .05f : .30f, .07f, .95f, .93f, TextAlignmentOptions.Center).color = new Color32(34, 28, 18, 255);
            _buttons.Add((button, offer));
        }
        private void BuildRewards(Transform parent, ShopService.Offer offer, float x0, float y0, float x1, float y1)
        {
            var rewards = Rewards(offer);
            var width = (x1 - x0) / Math.Max(1, rewards.Count);
            for (var i = 0; i < rewards.Count; i++)
            {
                var entry = rewards[i]; var root = ExpeditionUi.Rect(parent, "ShopReward-" + entry.Id, x0 + width * i, y0, x0 + width * (i + 1), y1);
                Picture(root, "RewardIcon", Icon(entry.Id), .10f, .40f, .90f, .98f);
                var count = Label(root, "RewardAmount", "×" + Number(entry.Amount), rewards.Count > 4 ? 21 : 25, .015f, .015f, .985f, .395f, TextAlignmentOptions.Center); count.fontStyle = FontStyles.Bold;
            }
        }
        private Sprite MainIcon(ShopService.Offer offer) => offer.items?.Any(i => i.def_id == "global_boss_teleport") == true ? TokenIcon : offer.matter > 0 ? ExpeditionResourceIcons.Get("matter") : offer.items?.Length == 1 ? Icon(offer.items[0].def_id) : ExpeditionTheme.Icon("inventory") ?? ExpeditionResourceIcons.Get("gold");
        private Sprite Icon(string id)
        {
            if (id == "global_boss_teleport") return TokenIcon;
            if (id == "gold" || id == "ore" || id == "matter" || id == "energy") return ExpeditionResourceIcons.Get(id);
            if (ConsumablePresentation.IsKnown(id)) return ConsumablePresentation.Icon(id);
            var definition = _catalog?.Get(id); return definition != null ? _catalog.GetDisplayIcon(definition) : ExpeditionTheme.Icon("inventory");
        }
        private void Purchase(ShopService.Offer offer)
        {
            if (_confirmation != null || _payment != null) return;
            _quote = _fixture == null ? ShopService.PreparePurchase(offer.id) :
                new ShopPurchaseConfirmation(offer, _fixture.catalog_version, _fixture.next_sequence, "offline-shop-fixture", 1, _fixtureOwner);
            if (_quote == null) { Refresh(); return; }
            ShowConfirmation(_quote);
        }
        private void ShowConfirmation(ShopPurchaseConfirmation quote)
        {
            var offer = quote.Offer;
            _confirmation = ExpeditionUi.Rect(transform, "ShopPurchaseConfirmation", 0, 0, 1, 1);
            ExpeditionUi.Fill(_confirmation, "ConfirmationShade", 0, 0, 1, 1, new Color(0, 0, 0, .90f)).raycastTarget = true;
            var panel = ExpeditionUi.Panel(_confirmation, "ConfirmationCard", .065f, .20f, .935f, .80f).rectTransform;
            Label(panel, "ConfirmationKicker", quote.IsRetry ? "ПРОВЕРКА ПОКУПКИ" : "ПОДТВЕРЖДЕНИЕ ПОКУПКИ", 21, .055f, .933f, .86f, .975f).color = ExpeditionTheme.Amber;
            Label(panel, "ConfirmationOfferTitle", offer.title, 35, .055f, .835f, .945f, .925f).fontStyle = FontStyles.Bold;
            Label(panel, "ConfirmationHint", quote.IsRetry ? "Проверим прежний запрос. Повторного списания не будет." : "Вы получите весь указанный состав одной покупкой.", 24, .055f, .755f, .945f, .835f).color = ExpeditionTheme.Muted;
            var rewards = Rewards(offer); var columns = Math.Min(4, Math.Max(1, rewards.Count)); var rows = Mathf.CeilToInt(rewards.Count / (float)columns);
            for (var i = 0; i < rewards.Count; i++)
            {
                var entry = rewards[i]; var row = i / columns; var column = i % columns;
                var x = .055f + column * .89f / columns; var y = .745f - (row + 1) * .35f / rows;
                var tile = ExpeditionUi.Rect(panel, "ConfirmationReward-" + entry.Id, x + .008f, y + .008f, x + .89f / columns - .008f, y + .35f / rows - .008f);
                ExpeditionUi.Fill(tile, "RewardWell", 0, 0, 1, 1, new Color32(20, 48, 53, 180));
                Picture(tile, "ConfirmationRewardIcon", Icon(entry.Id), .12f, .32f, .88f, .97f);
                Label(tile, "ConfirmationRewardAmount", "×" + Number(entry.Amount), 28, .025f, .025f, .975f, .31f, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            }
            var cost = ExpeditionUi.Panel(panel, "ConfirmationTotal", .055f, .255f, .945f, .38f).rectTransform;
            Label(cost, "ConfirmationTotalLabel", "ИТОГО К ОПЛАТЕ", 24, .035f, .12f, .60f, .88f).fontStyle = FontStyles.Bold;
            if (offer.currency != "rub") Picture(cost, "ConfirmationCurrency", ExpeditionResourceIcons.Get(offer.currency), .61f, .13f, .73f, .87f);
            Label(cost, "ConfirmationPrice", Number(offer.price) + (offer.currency == "rub" ? " ₽" : ""), 33, offer.currency == "rub" ? .63f : .74f, .10f, .965f, .90f, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            var state = _fixture ?? ShopService.Current;
            var available = offer.currency == "matter" ? state.wallet.matter : state.wallet.gold;
            var balance = quote.IsRetry ? "Состав и цена прежнего запроса сохранены." : offer.currency == "rub" ? "Оплата в рублях. Никаких списаний до подтверждения." :
                available < offer.price ? "Недостаточно " + (offer.currency == "matter" ? "материи" : "золота") + ": у вас " + Number(available) :
                (offer.currency == "matter" ? "Материя" : "Золото") + ": " + Number(available) + " → " + Number(available - offer.price);
            Label(panel, "ConfirmationBalance", balance, 23, .055f, .185f, .945f, .25f, TextAlignmentOptions.Center).color = ExpeditionTheme.Muted;
            ExpeditionUi.Button(panel, "ShopCancelPurchase", "Отмена", .055f, .045f, .475f, .16f, () => DismissConfirmation(true), false, 28);
            ExpeditionUi.Button(panel, "ShopConfirmPurchase", quote.IsRetry ? "Проверить" : "Подтвердить", .525f, .045f, .945f, .16f, ConfirmPurchase, true, 28);
        }
        private void DismissConfirmation(bool cancel)
        {
            if (cancel) _quote?.Cancel(); _quote = null;
            if (_confirmation == null) return;
            _confirmation.gameObject.SetActive(false); Destroy(_confirmation.gameObject); _confirmation = null;
        }
        private void ConfirmPurchase()
        {
            var quote = _quote; if (quote == null) return;
            DismissConfirmation(false); // Hide and detach immediately; a double tap cannot admit a second intent.
            if (_fixture != null)
            {
                if (!quote.TryConfirm(_fixture, "offline-shop-fixture", 1, _fixtureOwner, true, null, out var error)) _fixtureMessage = ShopService.Message(error);
                else if (quote.Offer.currency == "rub") { _fixtureMessage = ShopService.PaymentMessage; ShowPaymentMessage(); }
                else { _fixturePurchase?.Invoke(quote.Offer); _fixtureMessage = "Выбрано: " + quote.Offer.title; }
                Refresh(); return;
            }
            _ = ConfirmLiveAsync(quote);
        }
        private async System.Threading.Tasks.Task ConfirmLiveAsync(ShopPurchaseConfirmation quote)
        {
            await ShopService.BuyAsync(quote, _cts.Token);
            if (this != null && ShopService.Error == ShopService.PaymentMessage) ShowPaymentMessage();
        }
        private static List<(string Id, int Amount)> Rewards(ShopService.Offer offer)
        {
            var rewards = new List<(string Id, int Amount)>();
            if (offer.gold > 0) rewards.Add(("gold", offer.gold)); if (offer.ore > 0) rewards.Add(("ore", offer.ore));
            if (offer.matter > 0) rewards.Add(("matter", offer.matter)); if (offer.energy > 0) rewards.Add(("energy", offer.energy));
            if (offer.items != null) rewards.AddRange(offer.items.Select(i => (i.def_id, i.amount))); return rewards;
        }
        private void ShowPaymentMessage()
        {
            if (_payment != null) return;
            _payment = ExpeditionUi.Rect(transform, "ShopPaymentUnavailable", 0, 0, 1, 1);
            ExpeditionUi.Fill(_payment, "PaymentShade", 0, 0, 1, 1, new Color(0, 0, 0, .86f)).raycastTarget = true;
            var panel = ExpeditionUi.Panel(_payment, "PaymentCard", .07f, .36f, .93f, .64f).rectTransform;
            Label(panel, "PaymentMessage", ShopService.PaymentMessage, 34, .055f, .55f, .945f, .91f, TextAlignmentOptions.Center).fontStyle = FontStyles.Bold;
            Label(panel, "PaymentHint", "Выберите припасы за игровые ресурсы.", 24, .07f, .37f, .93f, .58f, TextAlignmentOptions.Center).color = ExpeditionTheme.Muted;
            ExpeditionUi.Button(panel, "PaymentUnderstood", "Понятно", .16f, .09f, .84f, .32f, () => { _payment.gameObject.SetActive(false); Destroy(_payment.gameObject); _payment = null; }, true, 28);
        }
        private static string Number(long value) => value.ToString("N0", CultureInfo.GetCultureInfo("ru-RU"));
        private static Image Picture(Transform parent, string name, Sprite sprite, float x0, float y0, float x1, float y1)
        { var image = ExpeditionUi.Fill(parent, name, x0, y0, x1, y1, Color.white); image.sprite = sprite; image.preserveAspect = true; return image; }
        private static TMP_Text Label(Transform parent, string name, string text, int size, float x0, float y0, float x1, float y1, TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft)
        {
            var value = ExpeditionUi.Text(parent, name, text, size, x0, y0, x1, y1, alignment); value.richText = false; value.margin = Vector4.zero;
            value.enableAutoSizing = true; value.fontSizeMin = Math.Max(14, size - 5); value.fontSizeMax = size; value.overflowMode = TextOverflowModes.Overflow; return value;
        }
        private static void Fit(Button button, int size)
        { var label = button.GetComponentInChildren<TMP_Text>(); label.margin = Vector4.zero; label.enableAutoSizing = true; label.fontSizeMin = size - 5; label.fontSizeMax = size; }
        private void OnDestroy() { if (_fixture == null) ShopService.Changed -= Refresh; _cts?.Cancel(); _cts?.Dispose(); }
    }
}
