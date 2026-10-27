using System;

namespace Project.Nakama
{
    /// <summary>A socket flag alone cannot detect a blackholed connection. Only a recent reply grants play access.</summary>
    public sealed class ServerConnectionLease
    {
        public const double LifetimeSeconds = 7;
        private object _transport;
        private double _confirmedAt = double.NegativeInfinity;
        public void Revoke() { _transport = null; _confirmedAt = double.NegativeInfinity; }
        public void Confirm(object transport, double now) { _transport = transport; _confirmedAt = now; }
        public bool Allows(object transport, bool connected, double now) => transport != null && connected &&
            ReferenceEquals(_transport, transport) && now >= _confirmedAt && now - _confirmedAt < LifetimeSeconds;
        public bool Fresh(object transport, bool connected, double now) => Allows(transport, connected, now) && now - _confirmedAt < .5;
    }
}
