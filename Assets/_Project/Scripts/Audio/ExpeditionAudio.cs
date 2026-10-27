using System;
using System.Collections;
using System.Threading;
using Project.Utils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace Project.Audio
{
    /// <summary>Scene-independent 2D UI/action sounds. No gameplay, prices, or RPCs.</summary>
    public sealed class ExpeditionAudio : MonoBehaviour
    {
        private const string BankPath = "Expedition/Audio/SelectedSfx";
        private static ExpeditionAudio _instance;
        private static ExpeditionAudioBank _bank;
        private static int _mainThread;
        private static readonly int[] LastCueFrames = new int[19];
        private AudioSource _source;
        private AudioListener _fallbackListener;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null; _bank = null; _mainThread = 0;
            for (var i = 0; i < LastCueFrames.Length; i++) LastCueFrames[i] = -1;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
            EnsureInstance();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static ExpeditionAudio EnsureInstance()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("ExpeditionAudio");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<ExpeditionAudio>();
            _instance._source = go.AddComponent<AudioSource>();
            _instance._source.playOnAwake = false;
            _instance._source.loop = false;
            _instance._source.spatialBlend = 0;
            _instance._source.ignoreListenerPause = true;
            ExpeditionAudioRouting.RegisterSource(_instance._source, ExpeditionAudioBus.Sound);
            _instance._fallbackListener = go.AddComponent<AudioListener>();
            _instance._fallbackListener.enabled = false;
            return _instance;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            var host = EnsureInstance();
            host.EnsureListener();
            host.StartCoroutine(BindSceneAfterStart(scene));
        }

        private static IEnumerator BindSceneAfterStart(Scene scene)
        {
            yield return null;
            if (!scene.IsValid() || !scene.isLoaded) yield break;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var button in root.GetComponentsInChildren<Button>(true)) BindButton(button);
        }

        private void EnsureListener()
        {
            // Mine/workshop have no authored AudioListener. Use a fallback only there;
            // preserve each existing scene's listener and its camera transform.
            var existing = false;
            foreach (var listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (listener != _fallbackListener && listener.enabled) { existing = true; break; }
            _fallbackListener.enabled = !existing;
        }

        public static AudioClip Clip(ExpeditionCue cue)
        {
            if (_bank == null) _bank = Resources.Load<ExpeditionAudioBank>(BankPath);
            return _bank != null ? _bank.Find(cue) : null;
        }

        /// <summary>Current profiles alias the same clips; optional bank variants can be assigned later.</summary>
        public static AudioClip ResolveVoiceClip(AudioClip regular)
        {
            if (_bank == null) _bank = Resources.Load<ExpeditionAudioBank>(BankPath);
            return _bank != null ? _bank.ResolveVoiceClip(regular) : regular;
        }

        public static void Play(ExpeditionCue cue)
        {
            if (_mainThread == 0) return;
            if (Thread.CurrentThread.ManagedThreadId != _mainThread)
            {
                MainThreadDispatcher.Enqueue(() => Play(cue));
                return;
            }
            if (!Application.isPlaying) return;
            if (!AudioPreferences.SoundEnabled) return;
            var index = (int)cue;
            if (index < 0 || index >= LastCueFrames.Length || LastCueFrames[index] == Time.frameCount) return;
            var clip = Clip(cue);
            if (clip == null) return;
            LastCueFrames[index] = Time.frameCount;
            EnsureInstance()._source.PlayOneShot(clip);
        }

        /// <summary>One listener per actual Button; repeated styling never duplicates it.</summary>
        public static void BindButton(Button button, ExpeditionCue? cue = null)
        {
            if (!Application.isPlaying || button == null) return;
            var feedback = button.GetComponent<ExpeditionButtonSfx>();
            if (feedback == null) feedback = button.gameObject.AddComponent<ExpeditionButtonSfx>();
            feedback.Configure(cue ?? InferButtonCue(button.name), cue.HasValue);
        }

        private static ExpeditionCue InferButtonCue(string buttonName)
        {
            var name = (buttonName ?? "").ToLowerInvariant();
            return name.Contains("back") || name.Contains("close") || name.Contains("cancel") || name.Contains("exit") || name.Contains("leave")
                || name.Contains("назад") || name.Contains("отмен") || name.Contains("закры") || name == "нет" || name == "no"
                ? ExpeditionCue.UiBack : ExpeditionCue.UiClick;
        }

        private void OnDestroy()
        {
            if (_instance != this) return;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            _instance = null;
        }
    }

    [DisallowMultipleComponent]
    public sealed class ExpeditionButtonSfx : MonoBehaviour
    {
        private Button _button;
        private ExpeditionCue _cue;
        private bool _explicitCue;

        public void Configure(ExpeditionCue cue, bool explicitCue)
        {
            if (!_explicitCue || explicitCue) { _cue = cue; _explicitCue = explicitCue; }
            _button = GetComponent<Button>();
            _button.onClick.RemoveListener(OnClicked);
            _button.onClick.AddListener(OnClicked);
        }

        // Button itself filters disabled/dragged/rejected pointer events. Do not recheck
        // interactable here: the real action may already have set its button busy/hidden.
        private void OnClicked() => ExpeditionAudio.Play(_cue);
        private void OnDestroy() { if (_button != null) _button.onClick.RemoveListener(OnClicked); }
    }

    /// <summary>Remembers each queue's last observed state, without sounding on first load.</summary>
    public sealed class ExpeditionCraftReadyTracker
    {
        private readonly bool[] _known = new bool[8];
        private readonly bool[] _busy = new bool[8];
        public bool Observe(int slot, bool occupied, bool ready)
        {
            if (slot < 0 || slot >= _known.Length) return false;
            var becameReady = _known[slot] && _busy[slot] && occupied && ready;
            _known[slot] = true; _busy[slot] = occupied && !ready;
            return becameReady;
        }
    }
}
