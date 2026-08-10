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
        /// <summary>Очередь «Спуск» (Race): rules=race, отдельная от classic/pro.</summary>
        private static bool _nextPvpRace;
        private static string _preferredBotId;
        private static int _preferredFloor;
        private static string _preferredDifficulty;
        private static bool _autoStartSolo;
        /// <summary>Куда вернуться после соло-PVE (MineScene / MineScene3D).</summary>
        private static string _soloReturnScene = "MineScene";

        /// <summary>Турнир арены: присоединиться к уже созданному authoritative матчу без матчмейкера.</summary>
        private static bool _arenaJoinPending;
        private static string _arenaJoinMatchId;
        private static string _arenaOpponentDisplayHint;
        private static string _arenaOpponentUserId;
        private static bool _arenaJoinOpponentIsBot;

        /// <summary>Друзья → «Спуск»: join к уже созданному race-матчу (не арена).</summary>
        private static bool _friendRaceJoinPending;
        private static string _friendRaceJoinMatchId;
        private static string _friendRaceOpponentHint;
        private static string _friendRaceOpponentUserId;
        private static int _friendRacePrepSeconds;

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
            if (pvpPro) _nextPvpRace = false;
        }

        public static bool ConsumePvpPro()
        {
            var v = _nextPvpPro;
            _nextPvpPro = false;
            return v;
        }

        /// <summary>Очередь «Спуск» (Race). Взаимоисключает Pro.</summary>
        public static void SetPvpRaceForNextMultiplayerMatch(bool pvpRace)
        {
            _nextPvpRace = pvpRace;
            if (pvpRace) _nextPvpPro = false;
        }

        public static bool ConsumePvpRace()
        {
            var v = _nextPvpRace;
            _nextPvpRace = false;
            return v;
        }

        public static void SetSoloMine(string botId, int floor, string difficulty, bool autoStart = true, string returnScene = null)
        {
            _nextMode = Match3LaunchMode.SoloBot;
            _preferredBotId = string.IsNullOrWhiteSpace(botId) ? null : botId;
            _preferredFloor = floor;
            _preferredDifficulty = string.IsNullOrWhiteSpace(difficulty) ? "easy" : difficulty;
            _autoStartSolo = autoStart;
            if (!string.IsNullOrWhiteSpace(returnScene))
                _soloReturnScene = returnScene.Trim();
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

        public static string ConsumeSoloReturnScene()
        {
            var scene = string.IsNullOrWhiteSpace(_soloReturnScene) ? "MineScene" : _soloReturnScene;
            _soloReturnScene = "MineScene";
            return scene;
        }

        public static string PeekSoloReturnScene() =>
            string.IsNullOrWhiteSpace(_soloReturnScene) ? "MineScene" : _soloReturnScene;

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

        /// <summary>Перед загрузкой DuelMatch3 после accept «Предложить Спуск».</summary>
        public static void ArmFriendRaceJoin(
            string matchId,
            string opponentDisplayHint = null,
            string opponentUserId = null,
            int prepSeconds = 5)
        {
            _friendRaceJoinMatchId = string.IsNullOrWhiteSpace(matchId) ? null : matchId.Trim();
            _friendRaceJoinPending = !string.IsNullOrEmpty(_friendRaceJoinMatchId);
            _friendRaceOpponentHint = string.IsNullOrWhiteSpace(opponentDisplayHint) ? null : opponentDisplayHint.Trim();
            _friendRaceOpponentUserId = string.IsNullOrWhiteSpace(opponentUserId) ? null : opponentUserId.Trim();
            _friendRacePrepSeconds = prepSeconds < 1 ? 5 : (prepSeconds > 15 ? 15 : prepSeconds);
            if (_friendRaceJoinPending)
            {
                _nextPvpRace = true;
                _nextPvpPro = false;
                _nextMode = Match3LaunchMode.Multiplayer;
                ClearArenaJoinArm();
            }
        }

        public static bool TryPeekFriendRaceJoin(
            out string matchId,
            out string opponentDisplayHint,
            out string opponentUserId,
            out int prepSeconds)
        {
            matchId = _friendRaceJoinMatchId;
            opponentDisplayHint = _friendRaceOpponentHint;
            opponentUserId = _friendRaceOpponentUserId;
            prepSeconds = _friendRacePrepSeconds > 0 ? _friendRacePrepSeconds : 5;
            return _friendRaceJoinPending && !string.IsNullOrEmpty(matchId);
        }

        public static void ConsumeFriendRaceJoinArm()
        {
            _friendRaceJoinPending = false;
            _friendRaceJoinMatchId = null;
            _friendRaceOpponentHint = null;
            _friendRaceOpponentUserId = null;
            _friendRacePrepSeconds = 0;
        }

        public static void ClearFriendRaceJoinArm()
        {
            ConsumeFriendRaceJoinArm();
        }

        public static void RequestArenaMenuAwaitBracketOverlay() => _arenaMenuAwaitBracketOverlay = true;

        public static bool ArenaMenuAwaitBracketOverlay => _arenaMenuAwaitBracketOverlay;

        public static void ClearArenaMenuAwaitBracketOverlay() => _arenaMenuAwaitBracketOverlay = false;
    }
}
