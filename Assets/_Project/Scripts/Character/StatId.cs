namespace Project.Character
{
    /// <summary>
    /// Строковые id статов (можно добавлять новые без миграции enum).
    /// Сервер отдаёт эти же ключи.
    /// </summary>
    public static class StatId
    {
        public const string Hp = "hp";
        public const string Damage = "damage";
        public const string Armor = "armor";
        public const string CritChance = "crit_chance"; // 0..1
        public const string Healing = "healing";
    }
}

