using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Project.Audio;
using Project.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    /// <summary>Device-preference round-trip, real silent AudioSource routing and offline production-settings UI. No account/network actions.</summary>
    public static class ExpeditionAudioSettingsQa
    {
        private const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static readonly string[] Keys = { AudioPreferences.SoundKey, AudioPreferences.MusicKey, AudioPreferences.AdultVoiceKey };
        private sealed class PreferencesSnapshot
        {
            private readonly bool[] _exists = new bool[3];
            private readonly int[] _values = new int[3];
            public PreferencesSnapshot() { for (var i = 0; i < Keys.Length; i++) { _exists[i] = PlayerPrefs.HasKey(Keys[i]); _values[i] = PlayerPrefs.GetInt(Keys[i]); } }
            public void Restore()
            {
                for (var i = 0; i < Keys.Length; i++) if (_exists[i]) PlayerPrefs.SetInt(Keys[i], _values[i]); else PlayerPrefs.DeleteKey(Keys[i]);
                PlayerPrefs.Save(); ReloadCache();
                (typeof(AudioPreferences).GetField("Changed", PrivateStatic)?.GetValue(null) as Action)?.Invoke();
            }
        }
        [Serializable] private sealed class Report
        {
            public bool passed, completed, realPlayMode, accountOrNetworkActions;
            public string error, checkedUtc;
            public List<string> checks = new List<string>();
        }
        private static Report _report;
        private static PreferencesSnapshot _saved;
        private static GameObject _sources, _ui;
        private static AudioClip _clip;
        private static AudioSource _sound, _music, _legacy, _authoredMuted;
        private static bool _listenerPause;
        private static float _listenerVolume;
        private static int _phase, _frame;
        private static double _deadline;
        private const string Output = ".cache/qa/legacy-0.3.8/sound-settings-validation.json";

        public static string Run(string target)
        {
            if (target == "preferences") return Preferences();
            if (target == "play") return StartPlay();
            if (target == "show") return ShowUi();
            if (target == "verify-ui") return VerifyUi();
            if (target == "restore") { RestoreUi(); return "Offline settings fixture removed; original settings/account UI unchanged."; }
            if (target == "status") return _report == null ? "No audio run started." : JsonUtility.ToJson(_report);
            throw new ArgumentException("Use preferences, play, status, show, verify-ui or restore.");
        }

        private static string Preferences()
        {
            var saved = new PreferencesSnapshot(); var events = 0;
            Action changed = () => events++;
            try
            {
                foreach (var key in Keys) PlayerPrefs.DeleteKey(key); PlayerPrefs.Save(); ReloadCache();
                Require(AudioPreferences.SoundEnabled && AudioPreferences.MusicEnabled && !AudioPreferences.AdultVoiceEnabled, "Wrong first-run defaults.");
                AudioPreferences.Changed += changed;
                var originalClip = ExpeditionAudio.Clip(ExpeditionCue.UiClick);
                Require(originalClip != null, "Current audio bank is unavailable.");
                AudioPreferences.SetSoundEnabled(false); AudioPreferences.SetSoundEnabled(false);
                Require(events == 1 && !AudioPreferences.SoundEnabled && AudioPreferences.MusicEnabled, "Sound switch is duplicated or changes Music.");
                AudioPreferences.SetMusicEnabled(false); AudioPreferences.SetAdultVoiceEnabled(true);
                Require(events == 3 && AudioPreferences.AdultVoiceEnabled && !AudioPreferences.SoundEnabled, "18+ switch changes Sound or raises duplicate events.");
                ReloadCache();
                Require(!AudioPreferences.SoundEnabled && !AudioPreferences.MusicEnabled && AudioPreferences.AdultVoiceEnabled, "Device preferences did not survive a cache reload.");
                Require(ExpeditionAudio.Clip(ExpeditionCue.UiClick) == originalClip, "18+ switch silently replaced the current sound bank.");
                return "PASS: default Sound/Music On,18+ Off; independent flags; device persistence round-trip; one event per actual change;18+ currently uses the exact same bank. Original preferences restored.";
            }
            finally { AudioPreferences.Changed -= changed; saved.Restore(); }
        }
        private static void ReloadCache() => typeof(AudioPreferences).GetField("_loaded", PrivateStatic)?.SetValue(null, false);

        private static string StartPlay()
        {
            Require(EditorApplication.isPlaying && !EditorApplication.isPaused, "Run in unpaused Play mode.");
            Require(_saved == null, "An audio run is already active.");
            _saved = new PreferencesSnapshot();
            _report = new Report { checkedUtc = DateTime.UtcNow.ToString("O"), realPlayMode = true };
            try
            {
                _report.checks.Add(Preferences());
                AudioPreferences.SetSoundEnabled(true); AudioPreferences.SetMusicEnabled(true); AudioPreferences.SetAdultVoiceEnabled(false);
                _listenerPause = AudioListener.pause; _listenerVolume = AudioListener.volume;
                _sources = new GameObject("AudioSettingsQaSilentSources");
                _clip = AudioClip.Create("AudioSettingsQaSilent", 44100 * 10, 1, 44100, false);
                _sound = Source("Sound", .37f); _music = Source("Music", .62f); _legacy = Source("UnregisteredLegacy", .43f);
                _authoredMuted = Source("AuthoredMuted", .5f); _authoredMuted.mute = true;
                ExpeditionAudioRouting.RegisterSource(_sound, ExpeditionAudioBus.Sound);
                ExpeditionAudioRouting.RegisterSource(_music, ExpeditionAudioBus.Music);
                ExpeditionAudioRouting.RegisterSource(_authoredMuted, ExpeditionAudioBus.Sound);
                _sound.loop = false; _sound.PlayOneShot(_clip); _music.Play(); _legacy.Play();
                _phase = 0; _frame = Time.frameCount; _deadline = EditorApplication.timeSinceStartup + 10;
                EditorApplication.update -= Tick; EditorApplication.update += Tick;
                EditorApplication.playModeStateChanged -= PlayState; EditorApplication.playModeStateChanged += PlayState;
                return "Silent real-source routing run started. Request status after several rendered frames.";
            }
            catch (Exception ex) { Finish(ex); return JsonUtility.ToJson(_report); }
        }
        private static AudioSource Source(string name, float volume)
        {
            var go = new GameObject(name); go.transform.SetParent(_sources.transform);
            var source = go.AddComponent<AudioSource>(); source.playOnAwake = false; source.clip = _clip;
            source.loop = true; source.spatialBlend = 0; source.volume = volume; source.ignoreListenerPause = true;
            return source;
        }
        private static void Tick()
        {
            if (_saved == null) return;
            if (EditorApplication.timeSinceStartup > _deadline) { Finish(new TimeoutException("Rendered audio frames did not advance.")); return; }
            if (Time.frameCount <= _frame) return;
            _frame = Time.frameCount;
            try
            {
                if (_phase == 0)
                {
                    Require(_sound.isPlaying && _music.isPlaying && _legacy.isPlaying, "Silent real sources did not start.");
                    ExpeditionAudioRouting.UnregisterSource(_legacy); // Prove the Off transition scans an actually unregistered live source.
                    AudioPreferences.SetSoundEnabled(false);
                    Require(!_sound.isPlaying && _sound.mute && !_legacy.isPlaying && _legacy.mute, "Sound Off does not immediately stop/mute registered and legacy audio.");
                    Require(_music.isPlaying && !_music.mute, "Sound Off muted or stopped Music.");
                    ListenerUnchanged(); _report.checks.Add("Sound Off immediately stops/mutes active registered and unregistered Sound sources while Music keeps playing; AudioListener untouched.");
                }
                else if (_phase == 1)
                {
                    AudioPreferences.SetAdultVoiceEnabled(true);
                    Require(!_sound.isPlaying && _sound.mute && _music.isPlaying, "18+ bypasses Sound mute or interrupts Music.");
                    AudioPreferences.SetSoundEnabled(true);
                    Require(!_sound.mute && !_sound.isPlaying && _authoredMuted.mute, "Sound On replays a stopped effect or loses authored mute.");
                    Require(Mathf.Approximately(_sound.volume, .37f) && Mathf.Approximately(_music.volume, .62f), "Routing changed authored volume.");
                    _sound.PlayOneShot(_clip); AudioPreferences.SetMusicEnabled(false);
                    Require(_music.mute && !_music.isPlaying && _sound.isPlaying && !_sound.mute, "Music Off does not pause only Music.");
                    _report.checks.Add("18+ retains the same bank and respects Sound Off; Sound On does not replay past effects; authored mute/volume preserved; Music Off leaves Sound playing.");
                }
                else
                {
                    AudioPreferences.SetMusicEnabled(true); Require(_music.isPlaying && !_music.mute, "Music On did not resume the paused channel.");
                    AudioPreferences.SetSoundEnabled(false);
                    var fresh = Source("NewWhileOff", .25f); fresh.Play(); ExpeditionAudioRouting.RegisterSource(fresh, ExpeditionAudioBus.Sound);
                    Require(fresh.mute && !fresh.isPlaying && _music.isPlaying, "A newly registered source leaks sound while disabled.");
                    ListenerUnchanged(); _report.checks.Add("Music resumes independently; a new Sound source registered while Off is immediately muted/stopped; listener state unchanged.");
                    Finish(null); return;
                }
                _phase++;
            }
            catch (Exception ex) { Finish(ex); }
        }
        private static void ListenerUnchanged() => Require(AudioListener.pause == _listenerPause && Mathf.Approximately(AudioListener.volume, _listenerVolume), "Preferences touched AudioListener.");
        private static void Finish(Exception error)
        {
            EditorApplication.update -= Tick; EditorApplication.playModeStateChanged -= PlayState;
            try
            {
                if (_sources != null) UnityEngine.Object.DestroyImmediate(_sources);
                if (_clip != null) UnityEngine.Object.DestroyImmediate(_clip);
            }
            finally
            {
                var saved = _saved; _saved = null; saved?.Restore();
                _report.passed = error == null; _report.completed = true; _report.error = error?.ToString() ?? "";
                Directory.CreateDirectory(Path.GetDirectoryName(Output)); File.WriteAllText(Output, JsonUtility.ToJson(_report, true));
            }
        }
        private static void PlayState(PlayModeStateChange state) { if (state == PlayModeStateChange.ExitingPlayMode && _saved != null) Finish(new OperationCanceledException("Play mode exited during QA.")); }

        private static string ShowUi()
        {
            Require(EditorApplication.isPlaying, "Actual runtime UI fixture requires Play mode.");
            RestoreUi();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/MainMenu/SettingsModal.prefab");
            Require(prefab != null, "Settings prefab is missing.");
            _ui = new GameObject("AudioSettingsQaOffline", typeof(RectTransform)); _ui.SetActive(false);
            var canvas = _ui.AddComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 31000;
            var scaler = _ui.AddComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920); scaler.matchWidthOrHeight = .5f; _ui.AddComponent<GraphicRaycaster>();
            var modal = UnityEngine.Object.Instantiate(prefab, _ui.transform, false); modal.name = "SettingsModal";
            var controller = _ui.AddComponent<MainMenuSettingsModalController>();
            typeof(MainMenuSettingsModalController).GetField("settingsModalRoot", PrivateInstance)?.SetValue(controller, modal);
            _ui.SetActive(true); // The actual controller constructs/wires its presentation; it never calls OpenModal/RefreshStatusAsync here.
            controller.enabled = false; modal.SetActive(true);
            foreach (var button in modal.GetComponentsInChildren<Button>(true)) button.onClick.RemoveAllListeners();
            var close = modal.transform.Find("Panel/CloseButton")?.GetComponent<Button>(); if (close != null) close.onClick.AddListener(RestoreUi);
            var status = modal.transform.Find("Panel/Status")?.GetComponent<TMP_Text>(); if (status != null) status.text = "Настройки устройства";
            var emailLine = modal.transform.Find("Panel/LinkedEmailLine")?.GetComponent<TMP_Text>(); if (emailLine != null) emailLine.text = "Данные аккаунта скрыты для снимка";
            foreach (var input in modal.GetComponentsInChildren<InputField>(true)) { input.SetTextWithoutNotify(""); input.interactable = false; }
            Canvas.ForceUpdateCanvases();
            return "Actual SettingsModal prefab + production controller layout shown in an offline runtime fixture; no account/email read or RPC. Capture native GameView, then restore.";
        }
        private static string VerifyUi()
        {
            Require(_ui != null && _ui.activeInHierarchy, "Show the offline settings fixture first."); Canvas.ForceUpdateCanvases();
            var block = _ui.GetComponentInChildren<AudioSettingsBlock>(true); Require(block != null, "Audio block missing.");
            var toggles = block.GetComponentsInChildren<Toggle>(true); Require(toggles.Length == 3, "Expected three live audio toggles.");
            foreach (var toggle in toggles)
            {
                Require(toggle.interactable && ((RectTransform)toggle.transform).rect.height >= 40, "An audio control is disabled or too short.");
                var expected = toggle.name == "SoundToggle" ? AudioPreferences.SoundEnabled : toggle.name == "MusicToggle" ? AudioPreferences.MusicEnabled : AudioPreferences.AdultVoiceEnabled;
                Require(toggle.isOn == expected, "Toggle does not reflect device preferences: " + toggle.name);
            }
            foreach (var label in block.GetComponentsInChildren<TMP_Text>(true)) { label.ForceMeshUpdate(); Require(!label.isTextOverflowing, "Audio label overflows: " + label.name); }
            return "PASS actual runtime UI:3 active toggles, saved values match, touch rows>=40 reference pixels, labels fit;18+ openly uses current sounds. No account data fetched.";
        }
        private static void RestoreUi() { if (_ui != null) UnityEngine.Object.DestroyImmediate(_ui); _ui = null; }
        private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
