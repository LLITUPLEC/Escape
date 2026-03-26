using System;

namespace Project.Match3
{
    public enum PieceType { None = 0, GemRed = 1, GemYellow = 2, GemGreen = 3, Skull = 4, Ankh = 5 }

    public enum AbilityType { Cross = 0, Square = 1, Petard = 2, Shield = 3, Fury = 4 }

    public class PlayerStats
    {
        public int hp        = 150;
        public int maxHp     = 150;
        public int mana      = 0;
        public int maxMana   = 100;
        public int crossCooldown  = 0;  // 0 = ready, >0 = turns remaining
        public int squareCooldown = 0;
        public int petardCooldown = 0;
        public int shieldCooldown = 0;
        public int furyCooldown   = 0;

        // Buffs (client-authoritative; replicated via BoardSyncMsg).
        // Shield stacks are represented as up to 3 independent durations.
        public int shieldT1 = 0;
        public int shieldT2 = 0;
        public int shieldT3 = 0;

        // Fury is a short-lived buff.
        public int furyTurnsRemaining = 0;
        public int furyDamageBonus    = 0;
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

        // cheatRows are 2 extra visual rows above the normal 6x6 board.
        // Order: yView=0 (logical y=-2), then yView=1 (logical y=-1), each with x=0..5.
        // Only included for whitelisted users; others should receive empty array.
        public int[] cheatRows = new int[0];

        // "a" = sorted userId[0], "b" = sorted userId[1]
        public int aHp, aMana, aCrossCd, aSquareCd, aPetardCd, aShieldCd, aFuryCd;
        public int aMaxHp;
        public int aShieldT1, aShieldT2, aShieldT3, aFuryTurns, aFuryBonus;

        public int bHp, bMana, bCrossCd, bSquareCd, bPetardCd, bShieldCd, bFuryCd;
        public int bMaxHp;
        public int bShieldT1, bShieldT2, bShieldT3, bFuryTurns, bFuryBonus;

        public bool extraTurn;      // active player gets another turn
        public string activeUserId; // who just acted

        // Animation metadata for remote client replay.
        // actionType: 0 = none, 1 = swap, 2 = cross, 3 = square, 4 = petard, 5 = shield, 6 = fury
        public int actionType;
        public int fromX, fromY;
        public int toX, toY;
        public int abilityX, abilityY;
        public bool critTriggered;
        public System.Collections.Generic.List<M3AnimStep> animSteps = new System.Collections.Generic.List<M3AnimStep>();
    }

    [Serializable]
    public class M3AnimStep
    {
        // 1 = clear, 2 = drop
        public int phase;
        // board[y * 6 + x] = (int)PieceType
        public int[] board = new int[36];
    }

    [Serializable]
    public class M3GameOverMsg
    {
        public string winnerUserId;
        public int rewardXp;
        public int rewardGold;
        public int newLevel;
    }

    [Serializable]
    public class M3ActionRequest
    {
        // 1 = swap, 2 = cross, 3 = square, 4 = petard, 5 = shield, 6 = fury
        public int actionType;
        public int fromX, fromY;
        public int toX, toY;
        public int cx, cy;
    }

    [Serializable]
    public class M3ActionRejectMsg
    {
        public string reason;
    }

    [Serializable]
    public class M3SelectionSyncMsg
    {
        public int x;
        public int y;
        public bool selected;
    }
}
