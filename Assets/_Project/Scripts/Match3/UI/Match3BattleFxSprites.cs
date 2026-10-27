using System;
using UnityEngine;

namespace Project.Match3
{
    /// <summary>Shared imported full-rect slices; no readable texture or runtime texture allocation.</summary>
    public static class Match3BattleFxSprites
    {
        private const string Root = "Expedition/Effects/038/";
        private static Sprite[] _fury, _explosion;
        private static Sprite _petard, _shield;
        public static Sprite[] Fury => _fury ??= Frames("fury-frame-atlas");
        public static Sprite[] Explosion => _explosion ??= Frames("explosion-atlas");
        public static Sprite Petard => _petard != null ? _petard : (_petard = Resources.Load<Sprite>(Root + "petard"));
        public static Sprite Shield => _shield != null ? _shield : (_shield = Resources.Load<Sprite>(Root + "shield"));
        private static Sprite[] Frames(string name)
        {
            var result = Resources.LoadAll<Sprite>(Root + name);
            Array.Sort(result, (a,b) => string.CompareOrdinal(a.name,b.name));
            return result;
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _fury = _explosion = null; _petard = _shield = null; }
    }
}
