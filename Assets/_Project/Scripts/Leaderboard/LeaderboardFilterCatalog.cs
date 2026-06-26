using System;
using System.Collections.Generic;

namespace Project.Leaderboard
{
    public readonly struct LeaderboardViewOption
    {
        public readonly string Id;
        public readonly string Label;

        public LeaderboardViewOption(string id, string label)
        {
            Id = id;
            Label = label;
        }
    }

    /// <summary>Статический каталог фильтров «Тип» / «Вид» для UI таблицы лидеров.</summary>
    public static class LeaderboardFilterCatalog
    {
        public const int MineFloorCount = 12;

        private static readonly IReadOnlyList<LeaderboardViewOption> TournamentViews = new[]
        {
            new LeaderboardViewOption("tournament_ore", "Турнир руды"),
            new LeaderboardViewOption("tournament_gold", "Турнир золота"),
            new LeaderboardViewOption("tournament_smith", "Турнир кузнеца"),
        };

        private static readonly IReadOnlyList<LeaderboardViewOption> DuelViews = new[]
        {
            new LeaderboardViewOption("duel_skirmish", "Схватка"),
            new LeaderboardViewOption("duel_arena", "Арена"),
        };

        private static readonly IReadOnlyList<LeaderboardViewOption> MineViews = BuildMineViews();

        public static string TypeLabel(LeaderboardType type) => type switch
        {
            LeaderboardType.Tournament => "Турнир",
            LeaderboardType.Duel => "Дуэль",
            LeaderboardType.Mine => "Шахта",
            _ => "Турнир",
        };

        public static string PeriodLabel(LeaderboardPeriod period) => period switch
        {
            LeaderboardPeriod.Day => "ДЕНЬ",
            LeaderboardPeriod.Week => "НЕДЕЛЯ",
            LeaderboardPeriod.Month => "МЕСЯЦ",
            LeaderboardPeriod.AllTime => "ВСЕ ВРЕМЯ",
            _ => "НЕДЕЛЯ",
        };

        public static IReadOnlyList<LeaderboardViewOption> ViewsForType(LeaderboardType type) => type switch
        {
            LeaderboardType.Duel => DuelViews,
            LeaderboardType.Mine => MineViews,
            _ => TournamentViews,
        };

        public static LeaderboardViewOption DefaultView(LeaderboardType type)
        {
            var views = ViewsForType(type);
            return views.Count > 0 ? views[0] : new LeaderboardViewOption("default", "—");
        }

        public static string ViewLabel(LeaderboardType type, string viewId)
        {
            foreach (var v in ViewsForType(type))
            {
                if (string.Equals(v.Id, viewId, StringComparison.Ordinal))
                    return v.Label;
            }

            return DefaultView(type).Label;
        }

        public static bool IsValidView(LeaderboardType type, string viewId)
        {
            foreach (var v in ViewsForType(type))
            {
                if (string.Equals(v.Id, viewId, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static IReadOnlyList<LeaderboardViewOption> BuildMineViews()
        {
            var list = new List<LeaderboardViewOption>(MineFloorCount);
            for (var floor = 1; floor <= MineFloorCount; floor++)
                list.Add(new LeaderboardViewOption($"mine_floor_{floor}", $"Этаж {floor}"));
            return list;
        }
    }
}
