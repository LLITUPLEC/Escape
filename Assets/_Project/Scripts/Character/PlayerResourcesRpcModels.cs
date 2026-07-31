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
        /// <summary>См. duel_match3 build_resource_payload: отдельные ключи (агрегат keys может совпадать с их суммой).</summary>
        public long miner_key;
        public long dark_key;

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

    [Serializable]
    public sealed class EnergyBuyRpcRequest
    {
        public int session_epoch;
        /// <summary>Server: "matter" (1 → 100 эн) или "gold" (1000 → 100 эн).</summary>
        public string mode;
        /// <summary>Сколько пакетов купить (1..50). Сервер умножает cost/grant.</summary>
        public int count;
    }
}
