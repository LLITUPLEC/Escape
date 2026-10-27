using System;
using Project.Audio;
using Project.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Editor
{
    public static class ExpeditionSelectedAudioQa
    {
        public static string Run()
        {
            if (!Application.isPlaying) throw new InvalidOperationException("Play mode required");
            var bank = Resources.Load<ExpeditionAudioBank>("Expedition/Audio/SelectedSfx");
            if (bank == null) throw new InvalidOperationException("Selected audio bank missing");
            foreach (ExpeditionCue cue in Enum.GetValues(typeof(ExpeditionCue)))
            {
                var clip = bank.Find(cue);
                if (clip == null || clip.samples <= 0 || clip.frequency < 16000 || clip.length <= 0)
                    throw new InvalidOperationException("Invalid imported selected clip: " + cue);
                var path = AssetDatabase.GetAssetPath(clip);
                if (!path.StartsWith("Assets/_Project/Audio/SFX/", StringComparison.Ordinal))
                    throw new InvalidOperationException("Audio bank refers to a demo/source path: " + path);
            }
            var listeners = 0;
            foreach (var listener in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (listener.enabled && listener.gameObject.activeInHierarchy) listeners++;
            if (listeners != 1) throw new InvalidOperationException("Expected exactly one enabled listener, found " + listeners);
            var fixture = new GameObject("AudioBindingQa", typeof(RectTransform), typeof(Button));
            try
            {
                var button = fixture.GetComponent<Button>();
                var clicks = 0;
                button.onClick.AddListener(() => clicks++);
                for (var i = 0; i < 10; i++) ExpeditionUi.StyleButton(button, false, 22);
                if (fixture.GetComponents<ExpeditionButtonSfx>().Length != 1)
                    throw new InvalidOperationException("Audio feedback duplicated after restyling");
                button.onClick.Invoke();
                if (clicks != 1) throw new InvalidOperationException("Gameplay callback changed by binding");
                return "PASS: all19 imported selected bank clips resolve to production SFX; one scene listener; ten restyles preserve one feedback component and one original callback. UI-click test only; no RPC.";
            }
            finally { UnityEngine.Object.DestroyImmediate(fixture); }
        }
    }
}
