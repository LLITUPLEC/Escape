using System;
using System.Collections.Generic;
using Project.Nakama;

namespace Project.Friends
{
    /// <summary>Account-scoped presentation snapshot. Generations reject responses started before account changes or completed friend actions.</summary>
    public sealed class FriendsIncomingInviteState
    {
        private readonly Func<string> _scope;
        public string AccountScope { get; private set; }
        public int Generation { get; private set; }
        public int Count { get; private set; }
        public event Action Changed;
        public FriendsIncomingInviteState(Func<string> scope = null) { _scope = scope ?? (() => LocalAccountCacheScope.Current); }
        public bool SynchronizeAccount()
        {
            var current = _scope();
            if (string.Equals(AccountScope, current, StringComparison.Ordinal)) return false;
            AccountScope = current; Generation++; Count = 0; Changed?.Invoke(); return true;
        }
        public void Invalidate() { Generation++; }
        public bool IsCurrent(string scope, int generation)
        {
            SynchronizeAccount();
            return !string.IsNullOrEmpty(scope) && string.Equals(AccountScope, scope, StringComparison.Ordinal) && Generation == generation;
        }
        public bool Publish(string scope, int generation, int count)
        {
            if (count < 0 || !IsCurrent(scope, generation)) return false;
            var next = Math.Max(0, count);
            if (next != Count) { Count = next; Changed?.Invoke(); }
            return true;
        }
        public static int CountIncoming(FriendListEntry[] entries)
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries ?? Array.Empty<FriendListEntry>())
                if (entry != null && entry.State == FriendRelationState.InviteReceived && !string.IsNullOrEmpty(entry.UserId)) ids.Add(entry.UserId);
            return ids.Count;
        }
    }
}
