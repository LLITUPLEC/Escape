using System;

namespace Project.Match3
{
    public enum PieceType { None = 0, GemRed = 1, GemYellow = 2, GemGreen = 3, Skull = 4, Ankh = 5 }

    public enum AbilityType { Cross = 0, Square = 1 }

    public class PlayerStats
    {
        public int hp        = 150;
        public int maxHp     = 150;
        public int mana      = 0;
        public int maxMana   = 150;
        public int crossCooldown  = 0;  // 0 = ready, >0 = turns remaining
        public int squareCooldown = 0;
    }

    // MatchResult used by Match3BoardLogic
    public struct MatchResult
    {
        public PieceType type;
        public int count;
        public System.Collections.Generic.List<(int x, int y)> cells;
    }

    // ─── Network Messages ─────────────────────────────────────────────────────

    /// <summary>Sent after every move/ability resolution by the active player.</summary>
    [Serializable]
    public class M3BoardSyncMsg
    {
        // board[y * 6 + x] = (int)PieceType, 36 elements
        public int[] board = new int[36];

        // "a" = sorted userId[0], "b" = sorted userId[1]
        public int aHp, aMana, aCrossCd, aSquareCd;
        public int bHp, bMana, bCrossCd, bSquareCd;

        public bool extraTurn;      // active player gets another turn
        public string activeUserId; // who just acted
    }

    [Serializable]
    public class M3GameOverMsg
    {
        public string winnerUserId;
    }
}
