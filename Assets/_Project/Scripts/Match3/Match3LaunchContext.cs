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
    }
}
