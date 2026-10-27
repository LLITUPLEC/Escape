using System;
using System.Linq;

namespace Project.UI
{
    /// <summary>A single-use approval of the exact displayed price and contents; no transport or wallet work.</summary>
    public sealed class ShopPurchaseConfirmation
    {
        private readonly ShopService.Offer _offer;
        private readonly object _owner;
        private readonly string _scope;
        private readonly int _epoch;
        private bool _consumed;
        public string CatalogVersion { get; }
        public long Sequence { get; }
        public string RetryRequestId { get; }
        public bool IsRetry => RetryRequestId != null;
        public bool Consumed => _consumed;
        public ShopService.Offer Offer => Copy(_offer);

        public ShopPurchaseConfirmation(ShopService.Offer offer, string catalogVersion, long sequence,
            string scope, int epoch, object owner, string retryRequestId = null)
        {
            _offer = Copy(offer); CatalogVersion = catalogVersion; Sequence = sequence;
            _scope = scope; _epoch = epoch; _owner = owner; RetryRequestId = retryRequestId;
        }
        public void Cancel() => _consumed = true;
        public bool IsCurrent(ShopService.State state, string scope, int epoch, object owner, bool allowed, string pendingRequestId, out string error)
        {
            error = null;
            if (!allowed || string.IsNullOrEmpty(_scope) || _scope != scope || _epoch != epoch || _owner == null || !ReferenceEquals(_owner, owner))
                error = "session_stale";
            else if (IsRetry ? RetryRequestId != pendingRequestId : pendingRequestId != null)
                error = "sequence_changed";
            else if (!IsRetry && (!ShopService.Valid(state) || CatalogVersion != state.catalog_version || Sequence != state.next_sequence ||
                     !Same(_offer, state.offers.FirstOrDefault(o => o.id == _offer?.id))))
                error = "catalog_changed";
            else if (_offer == null || _offer.price <= 0 || Sequence < 1 || string.IsNullOrEmpty(CatalogVersion))
                error = "catalog_changed";
            return error == null;
        }
        public bool TryConfirm(ShopService.State state, string scope, int epoch, object owner, bool allowed, string pendingRequestId, out string error)
        {
            if (_consumed) { error = "confirmation_consumed"; return false; }
            _consumed = true; // Reserve synchronously, before any asynchronous probe/RPC or double tap.
            return IsCurrent(state, scope, epoch, owner, allowed, pendingRequestId, out error);
        }
        public static ShopService.Offer Copy(ShopService.Offer value) => value == null ? null : new ShopService.Offer
        {
            id = value.id, tab = value.tab, title = value.title, description = value.description, badge = value.badge,
            currency = value.currency, featured = value.featured, enabled = value.enabled, price = value.price,
            gold = value.gold, ore = value.ore, matter = value.matter, energy = value.energy,
            items = value.items?.Select(i => new ShopService.Item { def_id = i.def_id, amount = i.amount }).ToArray()
        };
        private static bool Same(ShopService.Offer a, ShopService.Offer b) => a != null && b != null &&
            a.id == b.id && a.currency == b.currency && a.price == b.price && a.gold == b.gold && a.ore == b.ore && a.matter == b.matter && a.energy == b.energy &&
            a.title == b.title && a.description == b.description && a.enabled == b.enabled &&
            (a.items ?? Array.Empty<ShopService.Item>()).Select(i => i.def_id + ":" + i.amount).SequenceEqual((b.items ?? Array.Empty<ShopService.Item>()).Select(i => i.def_id + ":" + i.amount));
    }
}
