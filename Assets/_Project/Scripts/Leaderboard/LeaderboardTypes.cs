using System;

namespace Project.Leaderboard
{
    public enum LeaderboardPeriod
    {
        Day,
        Week,
        Month,
        AllTime,
    }

    public enum LeaderboardType
    {
        Tournament,
        Duel,
        Mine,
    }

    public enum LeaderboardRowStyle
    {
        Gold,
        Silver,
        Bronze,
        Standard,
        Sticky,
    }

    public enum LeaderboardRankDelta
    {
        None,
        Up,
        Down,
        New,
    }

    [Serializable]
    public sealed class LeaderboardGetRpcRequest
    {
        public string period;
        public string type;
        public string view_id;
    }

    [Serializable]
    public sealed class LeaderboardRewardItemDto
    {
        public string icon_id;
        public int amount;
    }

    [Serializable]
    public sealed class LeaderboardRewardTierDto
    {
        public int place;
        public LeaderboardRewardItemDto[] items;
    }

    [Serializable]
    public sealed class LeaderboardEntryDto
    {
        public int rank;
        public int rank_delta;
        public bool is_new;
        public string user_id;
        public string nickname;
        public long score;
        public long secondary_score;
    }

    [Serializable]
    public sealed class LeaderboardGetRpcResponse
    {
        public bool ok;
        public string err;
        public LeaderboardEntryDto[] entries;
        public LeaderboardEntryDto self_entry;
        public LeaderboardRewardTierDto[] rewards;
    }

    public sealed class LeaderboardEntry
    {
        public int Rank;
        public LeaderboardRankDelta Delta;
        public int DeltaAmount;
        public bool IsNew;
        public string UserId;
        public string Nickname;
        public long Score;
        public long SecondaryScore;
        public bool IsCurrentPlayer;

        public static LeaderboardEntry FromDto(LeaderboardEntryDto dto, string currentUserId)
        {
            if (dto == null)
                return null;

            var delta = LeaderboardRankDelta.None;
            var deltaAmount = 0;
            if (dto.is_new)
            {
                delta = LeaderboardRankDelta.New;
            }
            else if (dto.rank_delta > 0)
            {
                delta = LeaderboardRankDelta.Up;
                deltaAmount = dto.rank_delta;
            }
            else if (dto.rank_delta < 0)
            {
                delta = LeaderboardRankDelta.Down;
                deltaAmount = -dto.rank_delta;
            }

            return new LeaderboardEntry
            {
                Rank = dto.rank,
                Delta = delta,
                DeltaAmount = deltaAmount,
                IsNew = dto.is_new,
                UserId = dto.user_id ?? string.Empty,
                Nickname = string.IsNullOrWhiteSpace(dto.nickname) ? "—" : dto.nickname,
                Score = dto.score,
                SecondaryScore = dto.secondary_score,
                IsCurrentPlayer = !string.IsNullOrWhiteSpace(currentUserId)
                    && string.Equals(dto.user_id, currentUserId, StringComparison.Ordinal),
            };
        }
    }

    public static class LeaderboardPeriodIds
    {
        public static string ToId(LeaderboardPeriod period) => period switch
        {
            LeaderboardPeriod.Day => "day",
            LeaderboardPeriod.Week => "week",
            LeaderboardPeriod.Month => "month",
            LeaderboardPeriod.AllTime => "all",
            _ => "week",
        };

        public static LeaderboardPeriod FromId(string id)
        {
            if (string.Equals(id, "day", StringComparison.OrdinalIgnoreCase)) return LeaderboardPeriod.Day;
            if (string.Equals(id, "month", StringComparison.OrdinalIgnoreCase)) return LeaderboardPeriod.Month;
            if (string.Equals(id, "all", StringComparison.OrdinalIgnoreCase)) return LeaderboardPeriod.AllTime;
            return LeaderboardPeriod.Week;
        }
    }

    public static class LeaderboardTypeIds
    {
        public static string ToId(LeaderboardType type) => type switch
        {
            LeaderboardType.Tournament => "tournament",
            LeaderboardType.Duel => "duel",
            LeaderboardType.Mine => "mine",
            _ => "tournament",
        };

        public static LeaderboardType FromId(string id)
        {
            if (string.Equals(id, "duel", StringComparison.OrdinalIgnoreCase)) return LeaderboardType.Duel;
            if (string.Equals(id, "mine", StringComparison.OrdinalIgnoreCase)) return LeaderboardType.Mine;
            return LeaderboardType.Tournament;
        }
    }
}
