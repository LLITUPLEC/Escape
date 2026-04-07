using System;
using System.Collections.Generic;

namespace Project.Character
{
    [Serializable]
    public sealed class CharacterGetRpcResponse
    {
        public bool ok;
        public string err;

        public Progression progression;
        public StatsMap stats;

        // placeholders for future server-backed data
        public object equipment;
        public Inventory inventory;
    }

    [Serializable]
    public sealed class Progression
    {
        public int level;
        public int xp;
        public int gold;
        public int max_level;
    }

    /// <summary>
    /// Unity JsonUtility не умеет Dictionary, поэтому используем явные поля для базовых статов
    /// и оставляем место под расширение через доп. сериализацию позже.
    /// </summary>
    [Serializable]
    public sealed class StatsMap
    {
        public int hp;
        public int damage;
        public int armor;
        public float crit_chance;
        public int healing;
    }

    [Serializable]
    public sealed class Inventory
    {
        public int size = 25;
        public List<object> items;
    }
}

