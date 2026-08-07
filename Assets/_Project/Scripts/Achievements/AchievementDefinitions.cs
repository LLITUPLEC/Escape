using System;
using UnityEngine;

namespace Project.Achievements
{
    public enum AchievementTab
    {
        Obsession = 0,
        Slaughter = 1,
        Dnn = 2,
    }

    public enum AchievementNoticeKind
    {
        RewardGrantedToast = 0,
        /// <summary>Порог достигнут, награду можно забрать вручную (ещё не нажата «Получить»).</summary>
        CriterionMetAwaitClaim = 1,
    }

    /// <summary>Сводная информация для toast после автоматического получения шага.</summary>
    public sealed class AchievementUnlockInfo
    {
        public string ChainId;
        public int StepIndex;
        /// <remarks>По умолчанию локально не сериализуется с сервером — только код.</remarks>
        public AchievementNoticeKind NoticeKind = AchievementNoticeKind.RewardGrantedToast;
        public string Title;
        public string RewardLine;
    }

    /// <summary>
    /// Цепочка: один счётчик <see cref="StatKey"/> и несколько порогов.
    /// Шаг i считается выполненным, когда счётчик &gt;= Thresholds[i] при этом выполнены предыдущие пороги.
    /// </summary>
    public sealed class AchievementChainDefinition
    {
        public string ChainId;
        /// <summary>Локализованное имя цепочки (Storage: title_ru).</summary>
        public string TitleRu;
        public AchievementTab Tab;
        public string StatKey;
        public int[] Thresholds;
        public string[] Descriptions;
        public string[] RewardTexts;
        public Color[] TierAccentColors;
    }

    public static class AchievementStatKeys
    {
        public const string UsesCross = "uses.cross";
        public const string UsesSquare = "uses.square";
        public const string UsesPetard = "uses.petard";
        public const string UsesFury = "uses.fury";
        public const string UsesShield = "uses.shield";

        public const string TournamentSmithWinFinal = "slaughter.tournament_smith_final";
        public const string TournamentOreWinFinal = "slaughter.tournament_ore_final";
        public const string TournamentGoldWinFinal = "slaughter.tournament_gold_final";
        public const string DuelTriWin = "slaughter.duel_tri_win";
        public const string DuelCrossFinisher = "slaughter.duel_cross_finish";
        public const string DuelSquareFinisher = "slaughter.duel_square_finish";
        public const string DuelPetardFinisher = "slaughter.finish_petard_pvp";

        public const string Line5 = "matches.line5";
        public const string Line6 = "matches.line6";
        public const string ArenaBetsPlaced = "slaughter.arena_bets_placed";
        public const string ArenaBetsWon = "slaughter.arena_bets_won";
        public const string ArenaBetsLost = "slaughter.arena_bets_lost";

        public const string DnnDoubleFivePlusOneTurn = "dnn.double_line5_same_turn";
        public const string DnnWinAtOneHp = "dnn.win_at_one_hp";
        public const string DnnThreeFivePlusStreak = "dnn.three_five_plus_streak";
        public const string DnnArenaPerfectBetsWin = "dnn.arena_perfect_bets_win";
    }

    public static class AchievementCatalog
    {
        private static AchievementChainDefinition[] _chains;
        private static bool _fromServer;

        /// <summary>Каталог подтянут с сервера (или из локального кэша RPC).</summary>
        public static bool IsFromServer => _fromServer;

        public static AchievementChainDefinition[] Chains => _chains ?? Build();

        /// <summary>Подменить список цепочек данными с duel_match3_achievement_catalog_get.</summary>
        public static void ApplyFromServer(AchievementChainDefinition[] chains)
        {
            if (chains == null || chains.Length == 0)
                return;
            _chains = chains;
            _fromServer = true;
        }

        public static AchievementChainDefinition FindChain(string chainId)
        {
            foreach (var c in Chains)
            {
                if (string.Equals(c.ChainId, chainId, StringComparison.Ordinal))
                    return c;
            }
            return null;
        }

        private static Color G => new Color(0.35f, 0.92f, 0.42f);
        private static Color Lb => new Color(0.45f, 0.82f, 1f);
        private static Color Db => new Color(0.25f, 0.55f, 0.95f);
        private static Color P => new Color(0.72f, 0.38f, 1f);

        private static AchievementChainDefinition[] Build()
        {
            return new[]
            {
                Chain(AchievementTab.Obsession, "obs.cross", "Крест", AchievementStatKeys.UsesCross,
                    new[] { 10, 50, 250, 300 },
                    new[]
                    {
                        "Использовать «Крест» 10 раз",
                        "Использовать «Крест» 50 раз",
                        "Использовать «Крест» 250 раз",
                        "Использовать «Крест» 300 раз",
                    },
                    new[]
                    {
                        "Награда: +10 к здоровью",
                        "Награда: +20 к здоровью",
                        "Награда: +50 к здоровью",
                        "Награда: +70 к здоровью",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Obsession, "obs.square", "Квадрат", AchievementStatKeys.UsesSquare,
                    new[] { 10, 50, 250, 500 },
                    new[]
                    {
                        "Использовать «Квадрат» 10 раз",
                        "Использовать «Квадрат» 50 раз",
                        "Использовать «Квадрат» 250 раз",
                        "Использовать «Квадрат» 500 раз",
                    },
                    new[]
                    {
                        "Награда: +10 к здоровью",
                        "Награда: +20 к здоровью",
                        "Награда: +50 к здоровью",
                        "Награда: +70 к здоровью",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Obsession, "obs.petard", "Петарда", AchievementStatKeys.UsesPetard,
                    new[] { 10, 50, 250, 500 },
                    new[]
                    {
                        "Использовать «Петарду» 10 раз",
                        "Использовать «Петарду» 50 раз",
                        "Использовать «Петарду» 250 раз",
                        "Использовать «Петарду» 500 раз",
                    },
                    new[]
                    {
                        "Награда: +5 к урону",
                        "Награда: +10 к урону",
                        "Награда: +15 к урону",
                        "Награда: +20 к урону",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Obsession, "obs.fury", "Ярость", AchievementStatKeys.UsesFury,
                    new[] { 10, 50, 250, 500 },
                    new[]
                    {
                        "Использовать «Ярость» 10 раз",
                        "Использовать «Ярость» 50 раз",
                        "Использовать «Ярость» 250 раз",
                        "Использовать «Ярость» 500 раз",
                    },
                    new[]
                    {
                        "Награда: +0.5% к шансу критического удара",
                        "Награда: +0.5% к шансу критического удара",
                        "Награда: +0.5% к шансу критического удара",
                        "Награда: +0.5% к шансу критического удара",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Obsession, "obs.shield", "Щит", AchievementStatKeys.UsesShield,
                    new[] { 10, 50, 250, 500 },
                    new[]
                    {
                        "Использовать «Щит» 10 раз",
                        "Использовать «Щит» 50 раз",
                        "Использовать «Щит» 250 раз",
                        "Использовать «Щит» 500 раз",
                    },
                    new[]
                    {
                        "Награда: +5 к броне",
                        "Награда: +10 к броне",
                        "Награда: +15 к броне",
                        "Награда: +20 к броне",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.blacksmith", "Турнир кузнеца", AchievementStatKeys.TournamentSmithWinFinal,
                    new[] { 5, 25, 100, 500 },
                    new[]
                    {
                        "Выиграть турнир кузнеца 5 раз",
                        "Выиграть турнир кузнеца 25 раз",
                        "Выиграть турнир кузнеца 100 раз",
                        "Выиграть турнир кузнеца 500 раз",
                    },
                    new[]
                    {
                        "Награда: +1000 золота",
                        "Награда: +5000 золота",
                        "Награда: +15000 золота",
                        "Награда: +5% к здоровью от носимой экипировки",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.ore_tournament", "Турнир руды", AchievementStatKeys.TournamentOreWinFinal,
                    new[] { 5, 25, 100, 500 },
                    new[]
                    {
                        "Выиграть турнир руды 5 раз",
                        "Выиграть турнир руды 25 раз",
                        "Выиграть турнир руды 100 раз",
                        "Выиграть турнир руды 500 раз",
                    },
                    new[]
                    {
                        "Награда: +1000 руды",
                        "Награда: +5000 руды",
                        "Награда: +15000 руды",
                        "Награда: +5% к здоровью от носимой экипировки",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.gold_tournament", "Турнир золота", AchievementStatKeys.TournamentGoldWinFinal,
                    new[] { 5, 25, 100, 500 },
                    new[]
                    {
                        "Выиграть турнир золота 5 раз",
                        "Выиграть турнир золота 25 раз",
                        "Выиграть турнир золота 100 раз",
                        "Выиграть турнир золота 500 раз",
                    },
                    new[]
                    {
                        "Награда: +1000 золота",
                        "Награда: +5000 золота",
                        "Награда: +15000 золота",
                        "Награда: +5% к здоровью от носимой экипировки",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.duel", "Дуэли", AchievementStatKeys.DuelTriWin,
                    new[] { 5, 25, 100, 500 },
                    new[]
                    {
                        "Выиграть дуэль (Три-в-ряд) 5 раз",
                        "Выиграть дуэль (Три-в-ряд) 25 раз",
                        "Выиграть дуэль (Три-в-ряд) 100 раз",
                        "Выиграть дуэль (Три-в-ряд) 500 раз",
                    },
                    new[]
                    {
                        "Награда: +1000 руды",
                        "Награда: +5000 руды",
                        "Награда: +10000 руды",
                        "Награда: +5% к урону от носимой экипировки",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.cross_finish", "Финиш крестом", AchievementStatKeys.DuelCrossFinisher,
                    new[] { 10, 50, 300, 1000 },
                    new[]
                    {
                        "Добить противника крестом в PvP",
                        "Добить противника крестом в PvP",
                        "Добить противника крестом в PvP",
                        "Добить противника крестом в PvP",
                    },
                    new[]
                    {
                        "Награда: +10 к урону",
                        "Награда: +25 к урону",
                        "Награда: +100 к урону",
                        "Награда: +250 к урону",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.square_finish", "Финиш квадратом", AchievementStatKeys.DuelSquareFinisher,
                    new[] { 10, 50, 300, 1000 },
                    new[]
                    {
                        "Добить противника квадратом в PvP",
                        "Добить противника квадратом в PvP",
                        "Добить противника квадратом в PvP",
                        "Добить противника квадратом в PvP",
                    },
                    new[]
                    {
                        "Награда: +10 к урону",
                        "Награда: +25 к урону",
                        "Награда: +100 к урону",
                        "Награда: +250 к урону",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Dnn, "dnn.double_line", "Две линии 5+", AchievementStatKeys.DnnDoubleFivePlusOneTurn,
                    new[] { 1, 5, 25, 100 },
                    new[]
                    {
                        "За одно действие собрать две линии 5+ (можно каскадно)",
                        "За одно действие собрать две линии 5+ (можно каскадно)",
                        "За одно действие собрать две линии 5+ (можно каскадно)",
                        "За одно действие собрать две линии 5+ (можно каскадно)",
                    },
                    new[]
                    {
                        "Награда: +1% к урону",
                        "Награда: +2% к здоровью",
                        "Награда: +3% к броне",
                        "Награда: +5% к криту",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Dnn, "dnn.win_1hp", "Победа с 1 HP", AchievementStatKeys.DnnWinAtOneHp,
                    new[] { 1, 5 },
                    new[]
                    {
                        "Имея 1 очко здоровья, выиграть соперника в турнире или дуэли",
                        "Имея 1 очко здоровья, выиграть соперника в турнире или дуэли",
                    },
                    new[]
                    {
                        "Награда: +5% к броне",
                        "Награда: +5% к здоровью",
                    },
                    new[] { G, Lb }),

                Chain(AchievementTab.Obsession, "obs.line5", "Пятёрка", AchievementStatKeys.Line5,
                    new[] { 5, 25, 100, 500 },
                    new[]
                    {
                        "Собрать 5 камней в линию 5 раз",
                        "Собрать 5 камней в линию 25 раз",
                        "Собрать 5 камней в линию 100 раз",
                        "Собрать 5 камней в линию 500 раз",
                    },
                    new[]
                    {
                        "Награда: +1% к здоровью",
                        "Награда: +2% к здоровью",
                        "Награда: +3% к здоровью",
                        "Награда: +4% к здоровью",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Obsession, "obs.line6", "Шестёрка", AchievementStatKeys.Line6,
                    new[] { 5, 25, 100, 500 },
                    new[]
                    {
                        "Собрать 6 камней в линию 5 раз",
                        "Собрать 6 камней в линию 25 раз",
                        "Собрать 6 камней в линию 100 раз",
                        "Собрать 6 камней в линию 500 раз",
                    },
                    new[]
                    {
                        "Награда: +1% к урону",
                        "Награда: +2% к урону",
                        "Награда: +3% к урону",
                        "Награда: +4% к урону",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.bets_placed", "Букмекер", AchievementStatKeys.ArenaBetsPlaced,
                    new[] { 10, 50, 250, 1000 },
                    new[]
                    {
                        "Сделать ставку в турнире 10 раз",
                        "Сделать ставку в турнире 50 раз",
                        "Сделать ставку в турнире 250 раз",
                        "Сделать ставку в турнире 1000 раз",
                    },
                    new[]
                    {
                        "Награда: +2000 золота и +2000 руды",
                        "Награда: +10000 золота и +10000 руды",
                        "Награда: +50000 золота и +50000 руды",
                        "Награда: +200000 золота, +200000 руды и +5% к броне",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.bets_won", "Оракул", AchievementStatKeys.ArenaBetsWon,
                    new[] { 8, 40, 200, 800 },
                    new[]
                    {
                        "Сделать успешную ставку в турнире 8 раз",
                        "Сделать успешную ставку в турнире 40 раз",
                        "Сделать успешную ставку в турнире 200 раз",
                        "Сделать успешную ставку в турнире 800 раз",
                    },
                    new[]
                    {
                        "Награда: +7% к здоровью",
                        "Награда: +7% к броне",
                        "Награда: +7% к хилу",
                        "Награда: +7% к урону",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Slaughter, "sl.bets_lost", "Мимо кассы", AchievementStatKeys.ArenaBetsLost,
                    new[] { 10, 50, 250, 1000 },
                    new[]
                    {
                        "Сделать неуспешную ставку в турнире 10 раз",
                        "Сделать неуспешную ставку в турнире 50 раз",
                        "Сделать неуспешную ставку в турнире 250 раз",
                        "Сделать неуспешную ставку в турнире 1000 раз",
                    },
                    new[]
                    {
                        "Награда: +10 материи",
                        "Награда: +40 материи",
                        "Награда: +100 материи",
                        "Награда: +300 материи",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Dnn, "dnn.triple_extra", "Три хода подряд", AchievementStatKeys.DnnThreeFivePlusStreak,
                    new[] { 1, 5, 25, 100 },
                    new[]
                    {
                        "Сделать 3 хода подряд",
                        "Сделать 3 хода подряд",
                        "Сделать 3 хода подряд",
                        "Сделать 3 хода подряд",
                    },
                    new[]
                    {
                        "Награда: +300 здоровья",
                        "Награда: +300 брони",
                        "Награда: +5% к броне",
                        "Награда: +10% к здоровью",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Dnn, "dnn.perfect_bets", "Тотализатор", AchievementStatKeys.DnnArenaPerfectBetsWin,
                    new[] { 1, 5, 25, 100 },
                    new[]
                    {
                        "Успешно поставить на все возможные пары турнира и выиграть его",
                        "Успешно поставить на все возможные пары турнира и выиграть его",
                        "Успешно поставить на все возможные пары турнира и выиграть его",
                        "Успешно поставить на все возможные пары турнира и выиграть его",
                    },
                    new[]
                    {
                        "Награда: +2% к урону",
                        "Награда: +4% к урону",
                        "Награда: +6% к урону",
                        "Награда: +8% к урону",
                    },
                    new[] { G, Lb, Db, P }),
            };
        }

        private static AchievementChainDefinition Chain(
            AchievementTab tab,
            string id,
            string titleRu,
            string statKey,
            int[] thresholds,
            string[] descriptions,
            string[] rewards,
            Color[] colors)
        {
            return new AchievementChainDefinition
            {
                ChainId = id,
                TitleRu = titleRu ?? string.Empty,
                Tab = tab,
                StatKey = statKey,
                Thresholds = thresholds,
                Descriptions = descriptions,
                RewardTexts = rewards,
                TierAccentColors = colors,
            };
        }

        internal static AchievementTab TabFromCategoryId(string category)
        {
            if (string.Equals(category, "slaughter", StringComparison.OrdinalIgnoreCase))
                return AchievementTab.Slaughter;
            if (string.Equals(category, "dnn", StringComparison.OrdinalIgnoreCase))
                return AchievementTab.Dnn;
            return AchievementTab.Obsession;
        }

        internal static Color[] TierAccentColorsForStepCount(int stepCount)
        {
            var n = Mathf.Max(1, stepCount);
            var palette = new[] { G, Lb, Db, P };
            var colors = new Color[n];
            for (var i = 0; i < n; i++)
                colors[i] = palette[Mathf.Min(i, palette.Length - 1)];
            return colors;
        }

        internal static AchievementChainDefinition ChainFromServer(
            string id,
            string category,
            string titleRu,
            string counterKey,
            int[] thresholdDeltas,
            string[] descriptions,
            string[] rewardTexts)
        {
            return Chain(
                TabFromCategoryId(category),
                id,
                titleRu,
                counterKey,
                thresholdDeltas,
                descriptions,
                rewardTexts,
                TierAccentColorsForStepCount(thresholdDeltas?.Length ?? 0));
        }
    }
}
