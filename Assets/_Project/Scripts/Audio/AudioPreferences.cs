using System;
using UnityEngine;

namespace Project.Audio
{
    /// <summary>Device-wide listening preferences. All access is on Unity's main thread.</summary>
    public static class AudioPreferences
    {
        public const string SoundKey = "escape.audio.sound.enabled";
        public const string MusicKey = "escape.audio.music.enabled";
        public const string AdultVoiceKey = "escape.audio.adult_voice.enabled";
        private static bool _loaded, _sound, _music, _adultVoice;
        public static event Action Changed;
        public static bool SoundEnabled { get { Load(); return _sound; } }
        public static bool MusicEnabled { get { Load(); return _music; } }
        public static bool AdultVoiceEnabled { get { Load(); return _adultVoice; } }

        public static void SetSoundEnabled(bool value) { Load(); if (_sound == value) return; _sound = value; Save(SoundKey, value); }
        public static void SetMusicEnabled(bool value) { Load(); if (_music == value) return; _music = value; Save(MusicKey, value); }
        public static void SetAdultVoiceEnabled(bool value) { Load(); if (_adultVoice == value) return; _adultVoice = value; Save(AdultVoiceKey, value); }
        public static AudioClip ResolveVoiceClip(AudioClip regular, AudioClip adultVariant = null) => AdultVoiceEnabled && adultVariant != null ? adultVariant : regular;
        private static void Load()
        {
            if (_loaded) return;
            _sound = PlayerPrefs.GetInt(SoundKey, 1) != 0;
            _music = PlayerPrefs.GetInt(MusicKey, 1) != 0;
            _adultVoice = PlayerPrefs.GetInt(AdultVoiceKey, 0) != 0;
            _loaded = true;
        }
        private static void Save(string key, bool value) { PlayerPrefs.SetInt(key, value ? 1 : 0); PlayerPrefs.Save(); Changed?.Invoke(); }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() { _loaded = false; Changed = null; }
    }
}
