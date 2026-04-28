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

    /// <summary>Сводная информация для toast после автоматического получения шага.</summary>
    public sealed class AchievementUnlockInfo
    {
        public string ChainId;
        public int StepIndex;
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
        public const string DuelTriWin = "slaughter.duel_tri_win";
        public const string DuelPetardFinisher = "slaughter.duel_petard_finish";

        public const string DnnDoubleFivePlusOneTurn = "dnn.double_line5_same_turn";
        public const string DnnWinAtOneHp = "dnn.win_at_one_hp";
    }

    public static class AchievementCatalog
    {
        private static AchievementChainDefinition[] _chains;

        public static AchievementChainDefinition[] Chains
        {
            get
            {
                if (_chains == null)
                    _chains = Build();
                return _chains;
            }
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
                Chain(AchievementTab.Obsession, "obs.cross", AchievementStatKeys.UsesCross,
                    new[] { 10, 50, 250, 500 },
                    new[]
                    {
                        "Использовать «Крест» 10 раз",
                        "Использовать «Крест» 50 раз",
                        "Использовать «Крест» 250 раз",
                        "Использовать «Крест» 500 раз",
                    },
                    new[]
                    {
                        "Награда: +10 к здоровью",
                        "Награда: +20 к здоровью",
                        "Награда: +50 к здоровью",
                        "Награда: +70 к здоровью",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Obsession, "obs.square", AchievementStatKeys.UsesSquare,
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

                Chain(AchievementTab.Obsession, "obs.petard", AchievementStatKeys.UsesPetard,
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

                Chain(AchievementTab.Obsession, "obs.fury", AchievementStatKeys.UsesFury,
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

                Chain(AchievementTab.Obsession, "obs.shield", AchievementStatKeys.UsesShield,
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

                Chain(AchievementTab.Slaughter, "sl.blacksmith", AchievementStatKeys.TournamentSmithWinFinal,
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

                Chain(AchievementTab.Slaughter, "sl.duel", AchievementStatKeys.DuelTriWin,
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

                Chain(AchievementTab.Slaughter, "sl.petard_finish", AchievementStatKeys.DuelPetardFinisher,
                    new[] { 5, 50, 100, 500 },
                    new[]
                    {
                        "Победить в дуэли (Три-в-ряд) финишём «Петардой» 5 раз",
                        "Победить в дуэли финишём «Петардой» 50 раз",
                        "Победить в дуэли финишём «Петардой» 100 раз",
                        "Победить в дуэли финишём «Петардой» 500 раз",
                    },
                    new[]
                    {
                        "Награда: +5 материи",
                        "Награда: +50 материи",
                        "Награда: +100 материи",
                        "Награда: +500 материи и +1% шансу крита",
                    },
                    new[] { G, Lb, Db, P }),

                Chain(AchievementTab.Dnn, "dnn.double_line", AchievementStatKeys.DnnDoubleFivePlusOneTurn,
                    new[] { 1 },
                    new[]
                    {
                        "За один ход собрать две линии 5+ (можно каскадно)",
                    },
                    new[]
                    {
                        "Награда: +1% к урону от носимой экипировки",
                    },
                    new[] { G }),

                Chain(AchievementTab.Dnn, "dnn.win_1hp", AchievementStatKeys.DnnWinAtOneHp,
                    new[] { 1 },
                    new[]
                    {
                        "Имея 1 очко здоровья, выиграть соперника в турнире или дуэли",
                    },
                    new[]
                    {
                        "Награда: +5% к броне от носимой экипировки",
                    },
                    new[] { G }),
            };
        }

        private static AchievementChainDefinition Chain(
            AchievementTab tab,
            string id,
            string statKey,
            int[] thresholds,
            string[] descriptions,
            string[] rewards,
            Color[] colors)
        {
            return new AchievementChainDefinition
            {
                ChainId = id,
                Tab = tab,
                StatKey = statKey,
                Thresholds = thresholds,
                Descriptions = descriptions,
                RewardTexts = rewards,
                TierAccentColors = colors,
            };
        }
    }
}
