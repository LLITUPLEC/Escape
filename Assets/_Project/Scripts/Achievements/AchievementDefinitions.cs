using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Achievements
{
    public enum AchievementTab
    {
        Obsession = 0,
        Slaughter = 1,
        Dnn = 2,
        Resources = 3,
    }

    public readonly struct AchievementSection
    {
        public AchievementTab Tab { get; }
        public string Id { get; }
        public string TitleRu { get; }

        internal AchievementSection(AchievementTab tab, string id, string titleRu)
        {
            Tab = tab;
            Id = id;
            TitleRu = titleRu;
        }
    }

    public static class AchievementSections
    {
        public static IReadOnlyList<AchievementSection> All { get; } = Array.AsReadOnly(new[]
        {
            new AchievementSection(AchievementTab.Obsession, "obsession", "Одержимость"),
            new AchievementSection(AchievementTab.Slaughter, "slaughter", "Бойня"),
            new AchievementSection(AchievementTab.Dnn, "dnn", "ДНН"),
            new AchievementSection(AchievementTab.Resources, "resources", "Ресурсы"),
        });
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
    /// Порог хранится как итоговое значение или прирост, в зависимости от режима цепочки.
    /// </summary>
    public sealed class AchievementChainDefinition
    {
        public string ChainId;
        /// <summary>Локализованное имя цепочки (Storage: title_ru).</summary>
        public string TitleRu;
        public AchievementTab Tab;
        public string StatKey;
        public int[] Thresholds;
        public bool UsesTotalThresholds;
        public string[] Descriptions;
        public string[] RewardTexts;
        public string[][] RewardTypes;
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
        private static readonly Lazy<AchievementChainDefinition[]> FallbackChains = new Lazy<AchievementChainDefinition[]>(Build);
        private static AchievementChainDefinition[] _chains;
        private static bool _fromServer;

        /// <summary>Каталог подтянут с сервера (или из локального кэша RPC).</summary>
        public static bool IsFromServer => _fromServer;

        public static AchievementChainDefinition[] Chains => _chains ?? FallbackChains.Value;

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

                ChainFromServer("dnn.flower_bets", "dnn", "Цветочный", "dnn.arena_all_bets_lost_final_loss",
                    new[] { 1, 5, 20, 50 },
                    new[] { "Проиграть все возможные ставки и сыгранный финал одного турнира", "Проиграть все возможные ставки и сыгранный финал одного турнира", "Проиграть все возможные ставки и сыгранный финал одного турнира", "Проиграть все возможные ставки и сыгранный финал одного турнира" },
                    new[] { "Награда: +1% к урону", "Награда: +2% к здоровью", "Награда: +3% к броне", "Награда: +4% к урону" }, usesTotalThresholds: true),

                ChainFromServer("dnn.petard_50", "dnn", "Бешеный", "dnn.petard_50_pvp",
                    new[] { 1, 10, 50, 250 },
                    new[] { "Нанести «Петардой» не менее 50 урона за удар в классическом PvP или турнире", "Нанести «Петардой» не менее 50 урона за удар в классическом PvP или турнире", "Нанести «Петардой» не менее 50 урона за удар в классическом PvP или турнире", "Нанести «Петардой» не менее 50 урона за удар в классическом PvP или турнире" },
                    new[] { "Награда: +1% к урону", "Награда: +2% к урону", "Награда: +3% к урону", "Награда: +5% к урону" }, usesTotalThresholds: true),

                ChainFromServer("sl.blocked_damage", "slaughter", "Крепкий", "slaughter.damage_blocked_arena",
                    new[] { 100, 500, 2000, 5000 },
                    new[] { "Заблокировать бронёй и щитом 100 урона в классическом PvP или турнире", "Заблокировать бронёй и щитом 500 урона в классическом PvP или турнире", "Заблокировать бронёй и щитом 2 000 урона в классическом PvP или турнире", "Заблокировать бронёй и щитом 5 000 урона в классическом PvP или турнире" },
                    new[] { "Награда: +50 к броне", "Награда: +150 к броне", "Награда: +300 к броне", "Награда: +5% к броне" }, usesTotalThresholds: true),

                ChainFromServer("sl.critical_hits", "slaughter", "Обезумевший", "slaughter.critical_hits",
                    new[] { 10, 100, 500, 2000 },
                    new[] { "Нанести урон критическим ударом 10 раз в бою", "Нанести урон критическим ударом 100 раз в бою", "Нанести урон критическим ударом 500 раз в бою", "Нанести урон критическим ударом 2 000 раз в бою" },
                    new[] { "Награда: +1% к урону", "Награда: +2% к урону", "Награда: +3% к урону", "Награда: +2 п. п. к шансу критического удара" }, usesTotalThresholds: true),

                ChainFromServer("obs.cleanse_fistula", "obsession", "Обеззаразить", "pve.kill.mine_3",
                    new[] { 20, 60, 150, 350 },
                    new[] { "Победить «Гнилой свищ» на 3-м этаже шахты", "Победить «Гнилой свищ» на 3-м этаже шахты", "Победить «Гнилой свищ» на 3-м этаже шахты", "Победить «Гнилой свищ» на 3-м этаже шахты" },
                    new[] { "Награда: +25 к броне", "Награда: +75 к броне", "Награда: +150 к броне", "Награда: +1% к броне" }, usesTotalThresholds: true),

                ChainFromServer("obs.wipe_soot", "obsession", "Смыть копоть", "pve.kill.mine_7",
                    new[] { 15, 45, 120, 300 },
                    new[] { "Победить «Чёрную копоть» на 7-м этаже шахты", "Победить «Чёрную копоть» на 7-м этаже шахты", "Победить «Чёрную копоть» на 7-м этаже шахты", "Победить «Чёрную копоть» на 7-м этаже шахты" },
                    new[] { "Награда: +15 к урону", "Награда: +40 к урону", "Награда: +90 к урону", "Награда: +1% к урону" }, usesTotalThresholds: true),

                ChainFromServer("obs.sever_vein", "obsession", "Перерезать жилу", "pve.kill.mine_12",
                    new[] { 3, 12, 40, 100 },
                    new[] { "Победить «Жилу» на 12-м этаже шахты", "Победить «Жилу» на 12-м этаже шахты", "Победить «Жилу» на 12-м этаже шахты", "Победить «Жилу» на 12-м этаже шахты" },
                    new[] { "Награда: +50 к здоровью", "Награда: +150 к здоровью", "Награда: +300 к здоровью", "Награда: +2% к здоровью" }, usesTotalThresholds: true),

                ChainFromServer("res.ore_earned", "resources", "Рудная жила", "res.ore_earned",
                    new[] { 500, 5000, 25000, 500000 },
                    new[] { "Получить 500 ед. руды", "Получить 5 000 ед. руды", "Получить 25 000 ед. руды", "Получить 500 000 ед. руды" },
                    new[] { "Награда: +10 к броне", "Награда: +25 к броне", "Награда: +60 к броне", "Награда: +2% к броне" }, usesTotalThresholds: true),

                ChainFromServer("res.gold_earned", "resources", "Золотая россыпь", "res.gold_earned",
                    new[] { 1000, 10000, 50000, 250000 },
                    new[] { "Получить 1 000 ед. золота", "Получить 10 000 ед. золота", "Получить 50 000 ед. золота", "Получить 250 000 ед. золота" },
                    new[] { "Награда: +20 к здоровью", "Награда: +50 к здоровью", "Награда: +100 к здоровью", "Награда: +2% к здоровью" }, usesTotalThresholds: true),

                ChainFromServer("res.matter_earned", "resources", "Материя разлома", "res.matter_earned",
                    new[] { 5, 25, 100, 500 },
                    new[] { "Получить 5 ед. материи", "Получить 25 ед. материи", "Получить 100 ед. материи", "Получить 500 ед. материи" },
                    new[] { "Награда: +5 к урону", "Награда: +10 к урону", "Награда: +25 к урону", "Награда: +2% к урону" }, usesTotalThresholds: true),

                ChainFromServer("res.gold_spent", "resources", "Золотой оборот", "res.gold_spent",
                    new[] { 1000, 10000, 50000, 250000 },
                    new[] { "Потратить 1 000 ед. золота", "Потратить 10 000 ед. золота", "Потратить 50 000 ед. золота", "Потратить 250 000 ед. золота" },
                    new[] { "Награда: +10 к броне", "Награда: +25 к броне", "Награда: +60 к броне", "Награда: +2% к броне" }, usesTotalThresholds: true),

                ChainFromServer("res.ore_spent", "resources", "Руда в дело", "res.ore_spent",
                    new[] { 500, 5000, 25000, 150000 },
                    new[] { "Потратить 500 ед. руды", "Потратить 5 000 ед. руды", "Потратить 25 000 ед. руды", "Потратить 150 000 ед. руды" },
                    new[] { "Награда: +10 к лечению", "Награда: +25 к лечению", "Награда: +60 к лечению", "Награда: +2% к лечению" }, usesTotalThresholds: true),

                ChainFromServer("res.matter_spent", "resources", "Цена редкости", "res.matter_spent",
                    new[] { 5, 25, 100, 500 },
                    new[] { "Потратить 5 ед. материи", "Потратить 25 ед. материи", "Потратить 100 ед. материи", "Потратить 500 ед. материи" },
                    new[] { "Награда: +5 к урону", "Награда: +10 к урону", "Награда: +25 к урону", "Награда: +1 п. п. к шансу критического удара" }, usesTotalThresholds: true),

                ChainFromServer("res.tournament_ore_earned", "resources", "Рудный трофей", "res.tournament_ore_earned",
                    new[] { 2000, 10000, 50000, 250000 },
                    new[] { "Выиграть 2 000 ед. руды в турнирах", "Выиграть 10 000 ед. руды в турнирах", "Выиграть 50 000 ед. руды в турнирах", "Выиграть 250 000 ед. руды в турнирах" },
                    new[] { "Награда: +25 к здоровью", "Награда: +60 к здоровью", "Награда: +120 к здоровью", "Награда: +2% к здоровью" }, usesTotalThresholds: true),

                ChainFromServer("res.tournament_gold_earned", "resources", "Золотой трофей", "res.tournament_gold_earned",
                    new[] { 2000, 10000, 50000, 250000 },
                    new[] { "Выиграть 2 000 ед. золота в турнирах", "Выиграть 10 000 ед. золота в турнирах", "Выиграть 50 000 ед. золота в турнирах", "Выиграть 250 000 ед. золота в турнирах" },
                    new[] { "Награда: +10 к броне", "Награда: +25 к броне", "Награда: +60 к броне", "Награда: +2% к броне" }, usesTotalThresholds: true),

                ChainFromServer("res.tournament_ore_spent", "resources", "Рудный взнос", "res.tournament_ore_spent",
                    new[] { 500, 2500, 15000, 75000 },
                    new[] { "Потратить 500 ед. руды на вступительные взносы", "Потратить 2 500 ед. руды на вступительные взносы", "Потратить 15 000 ед. руды на вступительные взносы", "Потратить 75 000 ед. руды на вступительные взносы" },
                    new[] { "Награда: +10 к лечению", "Награда: +25 к лечению", "Награда: +60 к лечению", "Награда: +2% к лечению" }, usesTotalThresholds: true),

                ChainFromServer("res.tournament_gold_spent", "resources", "Золотой взнос", "res.tournament_gold_spent",
                    new[] { 500, 2500, 15000, 75000 },
                    new[] { "Потратить 500 ед. золота на вступительные взносы", "Потратить 2 500 ед. золота на вступительные взносы", "Потратить 15 000 ед. золота на вступительные взносы", "Потратить 75 000 ед. золота на вступительные взносы" },
                    new[] { "Награда: +20 к здоровью", "Награда: +50 к здоровью", "Награда: +100 к здоровью", "Награда: +2% к здоровью" }, usesTotalThresholds: true),
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
            foreach (var section in AchievementSections.All)
                if (string.Equals(category, section.Id, StringComparison.OrdinalIgnoreCase))
                    return section.Tab;
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
            string[] rewardTexts,
            bool usesTotalThresholds = false)
        {
            var definition = Chain(
                TabFromCategoryId(category),
                id,
                titleRu,
                counterKey,
                thresholdDeltas,
                descriptions,
                rewardTexts,
                TierAccentColorsForStepCount(thresholdDeltas?.Length ?? 0));
            definition.UsesTotalThresholds = usesTotalThresholds;
            return definition;
        }
    }
}
