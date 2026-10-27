using System;
using UnityEngine;

namespace Project.Audio
{
    public enum ExpeditionCue
    {
        LineClear, CascadeFall, AbilitySquare, AbilityCross, AbilityPetard,
        DamageHit, ExtraTurn, TimerEnd, Victory, Defeat,
        UiClick, UiBack, UiBlocked, QueueEnter, MatchReady,
        CraftStart, CraftReady, RewardClaim, BarrierUnlock
    }

    /// <summary>Selected, imported clips only. Excludes fury, shield, critical hit, and elevator.</summary>
    public sealed class ExpeditionAudioBank : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public ExpeditionCue cue;
            public AudioClip clip;
            [Tooltip("Optional future 18+ replacement. Empty uses the current clip.")]
            public AudioClip adultClip;
        }
        [Serializable] public struct VoiceVariant { public AudioClip regular; public AudioClip adultClip; }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();
        [SerializeField] private VoiceVariant[] voiceVariants = Array.Empty<VoiceVariant>();

        public AudioClip Find(ExpeditionCue cue)
        {
            foreach (var entry in entries)
                if (entry.cue == cue) return AudioPreferences.ResolveVoiceClip(entry.clip, entry.adultClip);
            return null;
        }
        public AudioClip ResolveVoiceClip(AudioClip regular)
        {
            if (regular == null) return null;
            foreach (var variant in voiceVariants ?? Array.Empty<VoiceVariant>())
                if (variant.regular == regular) return AudioPreferences.ResolveVoiceClip(regular, variant.adultClip);
            foreach (var entry in entries)
                if (entry.clip == regular) return AudioPreferences.ResolveVoiceClip(regular, entry.adultClip);
            return regular;
        }
    }
}
