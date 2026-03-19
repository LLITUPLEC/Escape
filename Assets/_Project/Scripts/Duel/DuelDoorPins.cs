namespace Project.Duel
{
    /// <summary>Идентификаторы дверей дуэли. Длины кодов: 1/3 — 2 цифры, 2/4 — 3 цифры. PIN задаётся на сервере (Nakama RPC).</summary>
    public static class DuelDoorPins
    {
        public const int LeftDoor1Id = 1;
        public const int LeftDoor2Id = 2;
        public const int RightDoor1Id = 3;
        public const int RightDoor2Id = 4;

        public static bool TryGetCodeLengthForDoorId(int doorId, out int codeLen)
        {
            codeLen = 0;
            if (doorId < 1 || doorId > 4) return false;
            var shortDoor = doorId == LeftDoor1Id || doorId == RightDoor1Id;
            codeLen = shortDoor ? 2 : 3;
            return true;
        }
    }
}
