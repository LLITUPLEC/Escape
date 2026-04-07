using System;

namespace Project.Character
{
    [Serializable]
    public sealed class CharacterGetRpcResponse
    {
        public bool ok;
        public string err;

        public Progression progression;
        public StatsMap stats;

        /// <summary>8 строк в порядке EquipmentSlotId (0..7); пустая строка — пустой слот.</summary>
        public string[] equipment_def_ids;

        /// <summary>25 ячеек сундука; пустая строка — пусто.</summary>
        public string[] inventory_def_ids;
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
}

