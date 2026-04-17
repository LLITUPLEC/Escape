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

        /// <summary>25 значений: количество в ячейке (стек). 0 если ячейка пуста.</summary>
        public int[] inventory_counts;

        /// <summary>Выученные рецепты (def_id из каталога рецептов).</summary>
        public string[] learned_recipe_ids;

        /// <summary>8 слотов мастерской (индекс = EquipmentSlotId): предмет в крафте или пусто.</summary>
        public string[] workshop_output_def_ids;

        /// <summary>Unix-время окончания крафта по слоту; 0 если слот свободен.</summary>
        public int[] workshop_ends_at;
    }

    [Serializable]
    public sealed class Progression
    {
        public int level;
        public int xp;
        public long gold;
        public int max_level;
        public int energy;
        public int energy_max;
        public long ore;
        public long ingots;
        public long matter;
        public long keys;
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

