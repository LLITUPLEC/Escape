using System;

namespace Project.Character
{
    [Serializable]
    public sealed class PlayerResourcesRpcResponse
    {
        public bool ok;
        public string err;

        public int energy;
        public int energy_max;
        public long ore;
        public long gold;
        public long ingots;
        public long matter;
        public long keys;

        public string resource;
        public string reason;
        public int spent;
        public int required;
    }

    [Serializable]
    public sealed class PlayerResourceSpendRpcRequest
    {
        public string resource;
        public int amount;
        public string reason;
        public int session_epoch;
    }
}
