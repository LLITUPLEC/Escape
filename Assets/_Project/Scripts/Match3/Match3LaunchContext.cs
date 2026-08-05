namespace Project.Match3
{
    public enum Match3LaunchMode
    {
        Multiplayer = 0,
        SoloBot = 1,
    }

    public static class Match3LaunchContext
    {
        private static Match3LaunchMode _nextMode = Match3LaunchMode.Multiplayer;
        /// <summary>§14 фаза 5: следующий мультиплеерный матч — очередь PvP Pro (уровень+экип на сервере).</summary>
        private static bool _nextPvpPro;
        private static string _preferredBotId;
        private static int _preferredFloor;
        private static string _preferredDifficulty;
        private static bool _autoStartSolo;

        /// <summary>Турнир арены: присоединиться к уже созданному authoritative матчу без матчмейкера.</summary>
        private static bool _arenaJoinPending;
        private static string _arenaJoinMatchId;
        private static string _arenaOpponentDisplayHint;
        private static string _arenaOpponentUserId;
        private static bool _arenaJoinOpponentIsBot;

        /// <summary>После автовыхода из боя 1/4–1/2: арена показывает блокер до первого poll с активной сеткой.</summary>
        private static bool _arenaMenuAwaitBracketOverlay;

        public static Match3LaunchMode ConsumeMode()
        {
            var mode = _nextMode;
            _nextMode = Match3LaunchMode.Multiplayer;
            return mode;
        }

        public static void SetMode(Match3LaunchMode mode)
        {
            _nextMode = mode;
        }

        /// <summary>Только для <see cref="Match3LaunchMode.Multiplayer"/>. Вызывать до загрузки сцены DuelMatch3.</summary>
        public static void SetPvpProForNextMultiplayerMatch(bool pvpPro)
        {
            _nextPvpPro = pvpPro;
        }

        public static bool ConsumePvpPro()
        {
            var v = _nextPvpPro;
            _nextPvpPro = false;
            return v;
        }

        public static void SetSoloMine(string botId, int floor, string difficulty, bool autoStart = true)
        {
            _nextMode = Match3LaunchMode.SoloBot;
            _preferredBotId = string.IsNullOrWhiteSpace(botId) ? null : botId;
            _preferredFloor = floor;
            _preferredDifficulty = string.IsNullOrWhiteSpace(difficulty) ? "easy" : difficulty;
            _autoStartSolo = autoStart;
        }

        public static void ConsumeSoloMine(out string botId, out int floor, out string difficulty, out bool autoStart)
        {
            botId = _preferredBotId;
            floor = _preferredFloor;
            difficulty = _preferredDifficulty;
            autoStart = _autoStartSolo;

            _preferredBotId = null;
            _preferredFloor = 0;
            _preferredDifficulty = null;
            _autoStartSolo = false;
        }

        /// <summary>Вызвать перед загрузкой сцены DuelMatch3 для боя турнира арены.</summary>
        public static void ArmArenaJoin(
            string matchId,
            string opponentDisplayHint = null,
            bool opponentIsBot = false,
            string opponentUserId = null)
        {
            _arenaJoinMatchId = string.IsNullOrWhiteSpace(matchId) ? null : matchId.Trim();
            _arenaJoinPending = !string.IsNullOrEmpty(_arenaJoinMatchId);
            _arenaOpponentDisplayHint = string.IsNullOrWhiteSpace(opponentDisplayHint) ? null : opponentDisplayHint.Trim();
            _arenaOpponentUserId = string.IsNullOrWhiteSpace(opponentUserId) ? null : opponentUserId.Trim();
            _arenaJoinOpponentIsBot = opponentIsBot;
        }

        /// <summary>Проверка без сброса (до успешного JoinMatchAsync).</summary>
        public static bool TryPeekArenaJoin(
            out string matchId,
            out string opponentDisplayHint,
            out bool opponentIsBot,
            out string opponentUserId)
        {
            matchId = _arenaJoinMatchId;
            opponentDisplayHint = _arenaOpponentDisplayHint;
            opponentIsBot = _arenaJoinOpponentIsBot;
            opponentUserId = _arenaOpponentUserId;
            return _arenaJoinPending && !string.IsNullOrEmpty(matchId);
        }

        /// <summary>Снять флаг после успешного JoinMatchAsync.</summary>
        public static void ConsumeArenaJoinArm()
        {
            _arenaJoinPending = false;
            _arenaJoinMatchId = null;
            _arenaOpponentDisplayHint = null;
            _arenaOpponentUserId = null;
            _arenaJoinOpponentIsBot = false;
        }

        /// <summary>Сброс при ошибке join или выходе со сцены до входа в матч.</summary>
        public static void ClearArenaJoinArm()
        {
            _arenaJoinPending = false;
            _arenaJoinMatchId = null;
            _arenaOpponentDisplayHint = null;
            _arenaOpponentUserId = null;
            _arenaJoinOpponentIsBot = false;
        }

        public static void RequestArenaMenuAwaitBracketOverlay() => _arenaMenuAwaitBracketOverlay = true;

        public static bool ArenaMenuAwaitBracketOverlay => _arenaMenuAwaitBracketOverlay;

        public static void ClearArenaMenuAwaitBracketOverlay() => _arenaMenuAwaitBracketOverlay = false;
    }
}
