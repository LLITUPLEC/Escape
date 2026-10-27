using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Project.Character;
using Project.Nakama;
using Project.Utils;
using UnityEngine;

namespace Project.UI
{
    /// <summary>Server quotes and account-scoped purchase intentions. An uncertain reply always retries the same sequence.</summary>
    public static class ShopService
    {
        public const string PaymentMessage = "Оплата ещё не подключена";
        [Serializable] public sealed class Item { public string def_id; public int amount; }
        [Serializable] public sealed class Offer
        {
            public string id, tab, title, description, badge, currency; public bool featured, enabled;
            public int price, gold, ore, matter, energy; public Item[] items;
        }
        [Serializable] public sealed class Wallet { public long gold, ore, matter; public int energy, energy_max; }
        [Serializable] public sealed class Receipt
        {
            public string request_id, offer_id, catalog_version, currency; public long purchase_seq, bought_at;
            public int price, gold, ore, matter, energy; public Item[] delivered_items, pending_items;
        }
        [Serializable] public sealed class State
        {
            public bool ok, already_purchased; public int schema; public string err, catalog_version;
            public long next_sequence; public Offer[] offers; public Wallet wallet; public Receipt receipt, last_receipt;
        }
        [Serializable] private sealed class Buy
        {
            public string offer_id, request_id, catalog_version; public long purchase_seq; public int session_epoch;
        }
        private static readonly SemaphoreSlim Gate = new(1, 1);
        private static string _scope, _error;
        private static State _state;
        private static Buy _pending;
        private static Offer _pendingTerms;
        private static bool _busy;
        public static event Action Changed;
        public static State Current { get { Observe(); return _state; } }
        public static bool Busy { get { Observe(); return _busy; } }
        public static string Error { get { Observe(); return _error; } }
        public static string PendingOfferId { get { Observe(); return _pending?.offer_id; } }
        private static void Observe()
        {
            var scope = LocalAccountCacheScope.Current;
            if (_scope == scope) return;
            _scope = scope; _state = null; _pending = null; _pendingTerms = null; _busy = false; _error = null; Changed?.Invoke();
        }
        public static ShopPurchaseConfirmation PreparePurchase(string offerId)
        {
            Observe();
            var bootstrap = NakamaBootstrap.Instance;
            if (_busy || _scope == null || bootstrap?.Session == null || !Valid(_state)) return null;
            if (_pending != null)
            {
                if (_pending.offer_id != offerId) { _error = "Сначала проверьте предыдущую покупку."; Changed?.Invoke(); return null; }
                return new ShopPurchaseConfirmation(_pendingTerms, _pending.catalog_version, _pending.purchase_seq, _scope,
                    NakamaBootstrap.GetLocalSessionEpoch(), bootstrap.Session, _pending.request_id);
            }
            var offer = _state.offers.FirstOrDefault(o => o.id == offerId);
            return offer == null ? null : new ShopPurchaseConfirmation(offer, _state.catalog_version, _state.next_sequence,
                _scope, NakamaBootstrap.GetLocalSessionEpoch(), bootstrap.Session);
        }
        public static async Task BuyAsync(ShopPurchaseConfirmation confirmation, CancellationToken ct)
        {
            string owner = null; global::Nakama.ISession session = null; NakamaBootstrap bootstrap = null; Buy intention = null;
            var held = await MainThreadDispatcher.RunAsync(() =>
            {
                Observe();
                if (confirmation == null || _busy || !Gate.Wait(0)) return false;
                bootstrap = NakamaBootstrap.Instance; session = bootstrap?.Session;
                if (!confirmation.TryConfirm(_state, _scope, NakamaBootstrap.GetLocalSessionEpoch(), session,
                        ServerConnectionGuard.CanPlay && bootstrap?.GameplaySessionAllowed == true, _pending?.request_id, out var error))
                { _error = Message(error); Gate.Release(); Changed?.Invoke(); return false; }
                var offer = confirmation.Offer;
                if (offer.currency == "rub")
                { _error = PaymentMessage; Gate.Release(); Changed?.Invoke(); return false; }
                if (_pending == null)
                {
                    _pending = new Buy { offer_id = offer.id, request_id = Guid.NewGuid().ToString("N"),
                        purchase_seq = confirmation.Sequence, catalog_version = confirmation.CatalogVersion };
                    _pendingTerms = ShopPurchaseConfirmation.Copy(offer);
                }
                intention = _pending; intention.session_epoch = NakamaBootstrap.GetLocalSessionEpoch();
                owner = _scope; _busy = true; _error = null; Changed?.Invoke(); return true;
            }).ConfigureAwait(false);
            if (!held) return;
            try
            {
                await ServerConnectionGuard.RequireAsync(ct).ConfigureAwait(false);
                var stillCurrent = await MainThreadDispatcher.RunAsync(() =>
                {
                    Observe();
                    var allowed = LocalAccountCacheScope.IsCurrent(owner) && ReferenceEquals(session, bootstrap.Session) &&
                        ServerConnectionGuard.CanPlay && bootstrap.GameplaySessionAllowed && intention.session_epoch == NakamaBootstrap.GetLocalSessionEpoch();
                    if (!allowed && LocalAccountCacheScope.IsCurrent(owner)) _error = Message("session_stale");
                    return allowed;
                }).ConfigureAwait(false);
                if (!stillCurrent) return;
                var response = await bootstrap.Client.RpcAsync(session, "duel_shop_buy", JsonUtility.ToJson(intention), canceller: ct).ConfigureAwait(false);
                var reply = string.IsNullOrEmpty(response?.Payload) ? null : JsonUtility.FromJson<State>(response.Payload);
                var accepted = await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!LocalAccountCacheScope.IsCurrent(owner) || !ReferenceEquals(session, bootstrap.Session)) return false;
                    if (!Valid(reply))
                    {
                        _error = Message(reply?.err);
                        // These server rejections certify that this intention did
                        // not debit again. Refresh before a new explicit tap.
                        if (reply != null && (reply.err == "catalog_changed" || reply.err == "sequence_changed" || reply.err == "request_compacted" || reply.err == "request_id_reused" || reply.err == "offer_unavailable" || reply.err == "not_enough_matter" || reply.err == "not_enough_gold" || reply.err == "energy_full")) { _pending = null; _pendingTerms = null; }
                        return false;
                    }
                    _state = reply; _error = null;
                    if (_pending != null && (Matches(reply.receipt, _pending) || Matches(reply.last_receipt, _pending))) { _pending = null; _pendingTerms = null; }
                    return true;
                }).ConfigureAwait(false);
                if (accepted) await PlayerResourcesService.GetAsync(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception)
            {
                if (owner != null) await MainThreadDispatcher.RunAsync(() =>
                { if (LocalAccountCacheScope.IsCurrent(owner)) _error = "Подтверждение не получено. Проверьте ту же покупку ещё раз — повторного списания не будет."; }).ConfigureAwait(false);
            }
            finally
            {
                if (owner != null) await MainThreadDispatcher.RunAsync(() => { if (LocalAccountCacheScope.IsCurrent(owner)) { _busy = false; Changed?.Invoke(); } }).ConfigureAwait(false);
                Gate.Release();
            }
        }
        public static async Task RefreshAsync(CancellationToken ct)
        {
            try { await Gate.WaitAsync(ct).ConfigureAwait(false); } catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            string owner = null; global::Nakama.ISession session = null; NakamaBootstrap bootstrap = null;
            try
            {
                bootstrap = NakamaBootstrap.Instance; if (bootstrap == null) return;
                await bootstrap.EnsureConnectedAsync(ct).ConfigureAwait(false); session = bootstrap.Session;
                owner = await MainThreadDispatcher.RunAsync(() => { Observe(); if (_scope == null || session == null) return null; _busy = true; _error = null; Changed?.Invoke(); return _scope; }).ConfigureAwait(false);
                if (owner == null) return;
                var response = await bootstrap.Client.RpcAsync(session, "duel_shop_get", "{}", canceller: ct).ConfigureAwait(false);
                var reply = string.IsNullOrEmpty(response?.Payload) ? null : JsonUtility.FromJson<State>(response.Payload);
                await MainThreadDispatcher.RunAsync(() =>
                {
                    if (!LocalAccountCacheScope.IsCurrent(owner) || !ReferenceEquals(session, bootstrap.Session)) return;
                    if (!Valid(reply)) { _error = Message(reply?.err); return; }
                    _state = reply; _error = null;
                    if (_pending != null && Matches(reply.last_receipt, _pending)) { _pending = null; _pendingTerms = null; }
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch { if (owner != null) await MainThreadDispatcher.RunAsync(() => { if (LocalAccountCacheScope.IsCurrent(owner)) _error = "Нет связи с сервером. Обновите витрину."; }).ConfigureAwait(false); }
            finally { if (owner != null) await MainThreadDispatcher.RunAsync(() => { if (LocalAccountCacheScope.IsCurrent(owner)) { _busy = false; Changed?.Invoke(); } }).ConfigureAwait(false); Gate.Release(); }
        }
        private static bool Matches(Receipt receipt, Buy intention) => receipt != null && receipt.request_id == intention.request_id && receipt.purchase_seq == intention.purchase_seq && receipt.offer_id == intention.offer_id && receipt.catalog_version == intention.catalog_version;
        public static bool Valid(State state) => state != null && state.ok && state.schema == 1 && !string.IsNullOrEmpty(state.catalog_version) && state.next_sequence >= 1 && state.wallet != null && state.wallet.gold >= 0 && state.wallet.ore >= 0 && state.wallet.matter >= 0 && state.wallet.energy >= 0 && state.wallet.energy_max >= state.wallet.energy && state.offers != null && state.offers.Length > 0 && state.offers.Length <= 64 && state.offers.All(o => o != null && !string.IsNullOrEmpty(o.id) && o.price > 0 && (o.currency == "matter" || o.currency == "gold" || o.currency == "rub"));
        public static string Message(string code) => code switch
        {
            "payment_not_connected" => PaymentMessage,
            "not_enough_matter" => "Недостаточно материи.", "not_enough_gold" => "Недостаточно золота.",
            "energy_full" => "В запасе энергии не хватает места для всей покупки.",
            "catalog_changed" => "Витрина обновилась. Нажмите «Обновить» и выберите покупку заново.",
            "sequence_changed" or "request_compacted" or "request_id_reused" => "Состояние покупки изменилось. Обновите витрину и проверьте инвентарь перед новой покупкой.",
            "offer_unavailable" => "Этот товар уже недоступен. Обновите витрину.",
            "session_stale" or "session_epoch_mismatch" or "stale_session" => "Сессия изменилась. Откройте магазин заново.",
            _ => "Не удалось подтвердить покупку. Повторите ту же кнопку: материя повторно не спишется."
        };
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _scope = _error = null; _state = null; _pending = null; _pendingTerms = null; _busy = false; Changed = null; }
    }
}
