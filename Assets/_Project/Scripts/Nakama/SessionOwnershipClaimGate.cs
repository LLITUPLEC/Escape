using System;
using System.Threading;

namespace Project.Nakama
{
    /// <summary>A transport reconnect keeps its authenticated owner; explicit authentication starts a new lifetime.</summary>
    public sealed class SessionOwnershipClaimGate
    {
        private string _acknowledgedUserId;
        private readonly object _sync = new();
        private int _generation = 1;
        public int Generation => Volatile.Read(ref _generation);
        public bool NeedsClaim(string userId)
        {
            lock (_sync) return !string.IsNullOrEmpty(userId) &&
                !string.Equals(_acknowledgedUserId, userId, StringComparison.Ordinal);
        }
        public bool Acknowledge(string userId, int generation)
        {
            lock (_sync)
            {
                if (string.IsNullOrEmpty(userId) || generation != Generation) return false;
                _acknowledgedUserId = userId;
                return true;
            }
        }
        public int BeginAuthentication()
        {
            lock (_sync)
            {
                _acknowledgedUserId = null;
                return Interlocked.Increment(ref _generation);
            }
        }
    }
}
