using System;

namespace Project.Match3
{
    /// <summary>Built-in mine identities; server custom names take precedence over the shipped naming migration.</summary>
    public static class MonsterNames
    {
        private static readonly string[] Names =
        {
            "Острый скол", "Ржавый гвоздь", "Гнилой свищ", "Крушило",
            "Сухой грохот", "Мёртвый плывун", "Чёрная копоть", "Забой",
            "Слепой бурав", "Липкий ломун", "Тихий шорох", "Жила"
        };
        private static readonly string[] Legacy =
        {
            "Острый скол", "Ржавый гвоздь", "Соляной клещ", "Корунд",
            "Шлаковый червь", "Кремень", "Пыльный коготь", "Сталеплав",
            "Липкий ломун", "Тихий шорох", "Тихий шорох", "Жила"
        };
        public static string Default(int floor) => floor >= 1 && floor <= Names.Length ? Names[floor - 1] : "Монстр";

        public static string Resolve(string serverName, MonsterDefinition definition, int floor)
        {
            if (!string.IsNullOrWhiteSpace(serverName) && !IsBuiltInName(serverName, floor)) return serverName;
            return definition != null && !string.IsNullOrWhiteSpace(definition.DisplayName) ? definition.DisplayName : Default(floor);
        }

        private static bool IsBuiltInName(string name, int floor) =>
            floor >= 1 && floor <= Names.Length && (string.Equals(name, Names[floor - 1], StringComparison.Ordinal) ||
                string.Equals(name, Legacy[floor - 1], StringComparison.Ordinal) || name == "Монстр " + floor ||
                name == "Шахтный монстр " + floor || name == "Страж шахты " + floor);
    }
}
