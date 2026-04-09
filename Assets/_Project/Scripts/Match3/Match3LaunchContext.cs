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
        private static string _preferredBotId;
        private static int _preferredFloor;
        private static string _preferredDifficulty;
        private static bool _autoStartSolo;

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
    }
}
