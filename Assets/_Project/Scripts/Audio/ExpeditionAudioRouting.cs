using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Project.Audio
{
    public enum ExpeditionAudioBus { Sound, Music }

    /// <summary>Independent sound/music gates. Never changes AudioListener or authored source volume.</summary>
    public sealed class ExpeditionAudioRouting : MonoBehaviour
    {
        private sealed class Route
        {
            public ExpeditionAudioBus Bus;
            public bool AuthoredMute, ResumeMusic;
        }
        private static readonly Dictionary<AudioSource, Route> Sources = new Dictionary<AudioSource, Route>();
        private static ExpeditionAudioRouting _instance;
        private float _nextScan;

        public static void RegisterSource(AudioSource source, ExpeditionAudioBus bus = ExpeditionAudioBus.Sound)
        {
            if (source == null || !Application.isPlaying) return;
            EnsureInstance();
            if (!Sources.TryGetValue(source, out var route))
            {
                route = new Route { AuthoredMute = source.mute };
                Sources.Add(source, route);
            }
            route.Bus = bus == ExpeditionAudioBus.Music ? ExpeditionAudioBus.Music : ExpeditionAudioBus.Sound;
            Apply(source, route);
        }

        public static void UnregisterSource(AudioSource source) { if (!ReferenceEquals(source, null)) Sources.Remove(source); }
        public static bool CanPlay(ExpeditionAudioBus bus) => bus == ExpeditionAudioBus.Music ? AudioPreferences.MusicEnabled : AudioPreferences.SoundEnabled;

        private static void EnsureInstance()
        {
            if (_instance != null) return;
            var go = new GameObject("ExpeditionAudioRouting"); DontDestroyOnLoad(go);
            _instance = go.AddComponent<ExpeditionAudioRouting>();
            AudioPreferences.Changed -= ApplyPreferences; AudioPreferences.Changed += ApplyPreferences;
            SceneManager.sceneLoaded -= SceneLoaded; SceneManager.sceneLoaded += SceneLoaded;
        }
        private static void SceneLoaded(Scene scene, LoadSceneMode mode) => ScanSources();
        private static void ScanSources()
        {
            if (!Application.isPlaying) return;
            var expired = new List<AudioSource>();
            foreach (var pair in Sources) if (pair.Key == null) expired.Add(pair.Key);
            foreach (var source in expired) Sources.Remove(source);
            foreach (var source in FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (Sources.ContainsKey(source)) continue;
                var marker = source.GetComponent<ExpeditionAudioSourceBus>();
                RegisterSource(source, marker != null ? marker.Bus : ExpeditionAudioBus.Sound);
            }
        }
        private static void ApplyPreferences()
        {
            if (!Application.isPlaying) return;
            // Include legacy sources before applying Off, so an already playing
            // source cannot escape the setting just because its owner is old.
            ScanSources();
            var expired = new List<AudioSource>();
            foreach (var pair in Sources)
            {
                if (pair.Key == null) expired.Add(pair.Key);
                else Apply(pair.Key, pair.Value);
            }
            foreach (var source in expired) Sources.Remove(source);
        }
        private static void Apply(AudioSource source, Route route)
        {
            if (!CanPlay(route.Bus))
            {
                source.mute = true;
                if (route.Bus == ExpeditionAudioBus.Music)
                {
                    if (source.isPlaying) { route.ResumeMusic = true; source.Pause(); }
                }
                else source.Stop(); // Flush active one-shots; they never replay when Sound is enabled again.
                return;
            }
            source.mute = route.AuthoredMute;
            if (route.Bus == ExpeditionAudioBus.Music && route.ResumeMusic && source.isActiveAndEnabled)
            { source.UnPause(); route.ResumeMusic = false; }
        }
        private void Update()
        {
            if (Time.unscaledTime < _nextScan) return;
            _nextScan = Time.unscaledTime + .5f;
            ScanSources(); // Compatibility for legacy dynamically created sources.
        }
        private void OnDestroy()
        {
            if (_instance != this) return;
            AudioPreferences.Changed -= ApplyPreferences; SceneManager.sceneLoaded -= SceneLoaded;
            _instance = null; Sources.Clear();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            AudioPreferences.Changed -= ApplyPreferences; SceneManager.sceneLoaded -= SceneLoaded;
            _instance = null; Sources.Clear();
        }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap() { EnsureInstance(); ScanSources(); }
    }
}
