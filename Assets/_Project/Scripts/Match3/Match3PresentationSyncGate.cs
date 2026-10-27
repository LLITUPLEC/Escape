using System.Collections.Generic;
using UnityEngine;

namespace Project.Match3
{
    /// <summary>Bounded duplicate/stale rejection for presentation. Server payload/state remains authoritative.</summary>
    public sealed class Match3PresentationSyncGate
    {
        private readonly HashSet<string> _seen = new();
        private readonly Queue<string> _order = new();
        private long _latestServerTime;
        public int RememberedCount => _seen.Count;
        public bool TryAccept(M3BoardSyncMsg msg, bool recoverySnapshot = false)
        {
            if (msg?.board == null || msg.board.Length != Match3BoardLogic.Size * Match3BoardLogic.Size) return false;
            // Current server timestamps have one-second precision. Equal timestamps are valid distinct actions.
            if (msg.serverNowUnixMs > 0 && msg.serverNowUnixMs < _latestServerTime) return false;
            var key = JsonUtility.ToJson(msg);
            if (_seen.Contains(key) && !recoverySnapshot) return false;
            if (_seen.Add(key))
            {
                _order.Enqueue(key);
                while (_order.Count > 64) _seen.Remove(_order.Dequeue());
            }
            if (msg.serverNowUnixMs > _latestServerTime) _latestServerTime = msg.serverNowUnixMs;
            return true;
        }
        public void Reset() { _seen.Clear(); _order.Clear(); _latestServerTime = 0; }
    }
}
