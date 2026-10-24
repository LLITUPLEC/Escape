using System;
using UnityEngine;

namespace Project.UI
{
    /// <summary>References the original gameplay resource sprites without duplicating their textures.</summary>
    public sealed class ExpeditionResourceIcons : ScriptableObject
    {
        public const string ResourcesPath = "Expedition/UI/resource-icons";

        [SerializeField] private Sprite ore;
        [SerializeField] private Sprite gold;
        [SerializeField] private Sprite matter;
        [SerializeField] private Sprite energy;

        private static ExpeditionResourceIcons _instance;
        private static bool _loaded;

        public static Sprite Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            if (!_loaded)
            {
                _instance = Resources.Load<ExpeditionResourceIcons>(ResourcesPath);
                _loaded = true;
            }

            Sprite icon = null;
            if (_instance != null)
            {
                if (string.Equals(key, "ore", StringComparison.OrdinalIgnoreCase)) icon = _instance.ore;
                else if (string.Equals(key, "gold", StringComparison.OrdinalIgnoreCase)) icon = _instance.gold;
                else if (string.Equals(key, "matter", StringComparison.OrdinalIgnoreCase)) icon = _instance.matter;
                else if (string.Equals(key, "energy", StringComparison.OrdinalIgnoreCase)) icon = _instance.energy;
            }
            return icon != null ? icon : ExpeditionTheme.Icon(key);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            _instance = null;
            _loaded = false;
        }
    }
}
