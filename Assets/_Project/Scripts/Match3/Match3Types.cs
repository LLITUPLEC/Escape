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
        /// <summary>«Спуск»: накопленные анхи для петарды (списание маны сопернику).</summary>
        public int racePetardBank;
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

        // Server-authoritative character stats (applied only in PVE).
        public int baseDamage = 0;
        public int baseArmor = 0;
        public float baseCrit = 0f; // 0..1
        public int baseHeal = 0;
        public string consumableId;
        public float damageMultiplier=1, armorMultiplier=1, healingMultiplier=1, consumableCritAdd;
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
        public bool globalBoss, globalBossQualified, bossScreamTriggered, bossAdaptationTriggered;
        public bool turnPaused;
        public int turnDurationSeconds;
        public long bossActionSequence;
        public string globalBossEventId;
        public long globalBossMyDamage, globalBossTotalDamage;
        public int bossOwnTurn, bossScreamCooldown, bossScreamDamage, bossPlayerTurnsLeft;
        // board[y * 6 + x] = (int)PieceType, 36 elements
        public int[] board = new int[36];

        // cheatRows are 2 extra visual rows above the normal 6x6 board.
        // Order: yView=0 (logical y=-2), then yView=1 (logical y=-1), each with x=0..5.
        // Only included for whitelisted users; others should receive empty array.
        public int[] cheatRows = new int[0];

        /// <summary>Optional one-off popup message shown on the board.</summary>
        public string boardPopup;

        // "a" = sorted userId[0], "b" = sorted userId[1]
        public int aHp, aMana, aCrossCd, aSquareCd, aPetardCd, aShieldCd, aFuryCd;
        public int aMaxHp;
        public int aBaseDamage;
        public int aBaseArmor;
        public float aBaseCrit; // 0..1
        public int aBaseHeal;
        public int aShieldT1, aShieldT2, aShieldT3, aFuryTurns, aFuryBonus;
        public int aLevel;
        public int aMaxMana;
        public int aPetardBank;
        public int aRaceActions;
        public string aConsumableId;
        public float aDamageMultiplier=1, aArmorMultiplier=1, aHealingMultiplier=1, aConsumableCritAdd;

        public int bHp, bMana, bCrossCd, bSquareCd, bPetardCd, bShieldCd, bFuryCd;
        public int bMaxHp;
        public int bBaseDamage;
        public int bBaseArmor;
        public float bBaseCrit; // 0..1
        public int bBaseHeal;
        public int bShieldT1, bShieldT2, bShieldT3, bFuryTurns, bFuryBonus;
        public int bLevel;
        public int bMaxMana;
        public int bPetardBank;
        public int bRaceActions;
        public string bConsumableId;
        public float bDamageMultiplier=1, bArmorMultiplier=1, bHealingMultiplier=1, bConsumableCritAdd;
        public long bountyEndsAt;
        /// <summary>«Спуск»: цель по мане на этот матч.</summary>
        public int raceGoalMana;
        /// <summary>«Спуск»: чей last-turn (пусто, если ещё не начался).</summary>
        public string raceLastTurnUserId;
        /// <summary>«Спуск»: каждые N ходов игрока — +1 к мане с камней.</summary>
        public int raceManaBonusEvery;

        /// <summary>Сколько маны списано у цели за этот ход (Race: черепа/петарда).</summary>
        public int manaDrain;
        /// <summary>Режим «Спуск» (PvP Race).</summary>
        public bool pvpRace;

        public bool extraTurn;      // active player gets another turn
        /// <summary>Кому принадлежит текущий ход после применения этого синка.</summary>
        public string activeUserId;
        /// <summary>One authoritative round covers the first combatant and the response; extra actions keep it unchanged. Zero means unavailable on a legacy snapshot.</summary>
        public int roundNumber;
        /// <summary>Кто совершил действие в этом синке (swap/способность). Пусто, если синк без действия.</summary>
        public string actionActorUserId;

        /// <summary>Unix time in milliseconds when the current turn must end (server clock + remaining tick budget).</summary>
        public long turnEndsAtUnixMs;

        /// <summary>Метка времени сервера (UTC ms) на момент формирования синка; для выравнивания клиентских часов с сервером при отображении таймера.</summary>
        public long serverNowUnixMs;

        // Animation metadata for remote client replay.
        // actionType: 0 = none, 1 = swap, 2 = cross, 3 = square, 4 = petard, 5 = shield, 6 = fury
        public int actionType;
        public int fromX, fromY;
        public int toX, toY;
        public int abilityX, abilityY;
        public bool critTriggered;
        public string pveAffix;
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
        public bool globalBoss, bossAttemptFinished, bossQualified, bossAttemptRefunded;
        public long bossGlobalHp, bossGlobalMaxHp, bossMyDamage, bossTotalDamage;
        public string winnerUserId;
        public bool isDraw;
        public bool matchCancelled;
        public bool retryAllowed;
        public string endReason;
        public bool entryRefund;
        public int rewardXp;
        public int rewardGold;
        public int rewardOre;
        public int rewardMatter;
        public int rewardIngots;
        public string rewardKeyId;
        public int rewardKeyAmount;
        public string rewardBlueprint;
        /// <summary>Def id предмета-рецепта из §4.3 (v43), если сервер положил в сундук.</summary>
        public string rewardRecipeItemId;
        public int rewardTesseract;
        /// <summary>The reward transaction is not committed yet; distinct from saved items awaiting bag space.</summary>
        public bool rewardPending;
        public long itemLootPendingCount;
        public M3ItemLoot[] deliveredItems;
        public M3ItemLoot[] pendingItems;
        public int newLevel;

        // Arena tournament meta (optional).
        public string arenaTournamentId;
        public string arenaRound;   // "qf" | "sf" | "final"
        public string arenaBetTier; // "green" | "blue" | "purple"
        /// <summary>Сервер: smith | ore | gold — для достижений и UI.</summary>
        public string arenaKind;
        public M3ArenaCraftReward[] arenaCraftRewards;
    }

    [Serializable]
    public sealed class M3ArenaCraftReward
    {
        public string userId, outcome, ingotDef, recipeItemId;
        public int ingots, pendingCount;
        /// <summary>Receipts are broadcast to both players. Only the exact local user's row belongs in this result.</summary>
        public static M3ArenaCraftReward ForUser(M3GameOverMsg result, string userId)
        {
            if (string.IsNullOrEmpty(userId) || result?.arenaCraftRewards == null) return null;
            foreach(var row in result.arenaCraftRewards)
                if(row != null && string.Equals(row.userId,userId,StringComparison.Ordinal) &&
                   (row.outcome == "champion" || row.outcome == "participation")) return row;
            return null;
        }
    }

    [Serializable]
    public sealed class M3ItemLoot
    {
        public string def_id;
        public long amount;
    }

    /// <summary>Сервер (duel_match3): соперник временно потерял связь; матч продолжается до истечения grace.</summary>
    [Serializable]
    public class M3PeerDisconnectMsg
    {
        public string disconnectedUserId;
        public int reconnectGraceSeconds;
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
