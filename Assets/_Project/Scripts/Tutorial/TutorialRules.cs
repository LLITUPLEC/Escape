using Project.Match3;

namespace Project.Tutorial
{
    /// <summary>Audited base rules from duel_match3_config.lua. Editor QA checks this mirror before release.</summary>
    public static class TutorialRules
    {
        public const int MaxHp = 150, MaxMana = 100, BombDamage = 5, CapsuleHeal = 1, AbilityDamage = 3, PetardDamage = 15;
        public const int ShieldArmor = 4, ShieldHeal = 3, ShieldTurns = 3, MaxShieldStacks = 3;
        public const float FuryCritChance = .18f;
        public static int Mana(PieceType type) => type == PieceType.GemRed ? 5 : type == PieceType.GemYellow ? 3 : type == PieceType.GemGreen ? 1 : 0;
        public static int Cost(AbilityType ability) => ability == AbilityType.Shield ? 40 : ability == AbilityType.Fury || ability == AbilityType.Petard ? 30 : 20;
        public static int Cooldown(AbilityType ability) => ability == AbilityType.Shield || ability == AbilityType.Petard ? 1 : 2;
    }
}
