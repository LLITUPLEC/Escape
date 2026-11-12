using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Audio
{
    /// <summary>Three audio circuits on the shelter's device console.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioSettingsBlock : MonoBehaviour
    {
        private RectTransform _root;
        private readonly Toggle[] _toggles = new Toggle[3];
        private readonly TMP_Text[] _states = new TMP_Text[3];
        public void Build(RectTransform parent)
        {
            if (_root != null) { Refresh(); return; }
            _root = ExpeditionShelterSurface.Inset(parent, "ExpeditionAudioSettings", new Color32(15, 31, 32, 255), .045f, .261f, .955f, .744f);
            if (Application.isPlaying) _root.gameObject.AddComponent<ExpeditionPanelArrival>();
            var caption = ExpeditionUi.Text(_root, "AudioCaption", "ЗВУК И МУЗЫКА", 29, .055f, .824f, .945f, .954f);
            caption.color = ExpeditionTheme.Amber; caption.margin = Vector4.zero;
            caption.enableAutoSizing = true; caption.fontSizeMin = 21; caption.fontSizeMax = 29;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            var labels = new[] { "Звуки", "Фоновая музыка", "Озвучка 18+" };
            var names = new[] { "SoundToggle", "MusicToggle", "AdultVoiceToggle" };
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                var y = .609f - i * .221f;
                var row = ExpeditionShelterSurface.Inset(_root, names[i], new Color32(24, 26, 23, 255), .052f, y, .948f, y + .185f);
                var surface = row.GetComponent<Image>();
                surface.raycastTarget = true;
                var toggle = row.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = surface;
                var label = ExpeditionUi.Text(row, "Label", labels[i], 33, .045f, .06f, .67f, .94f);
                label.enableAutoSizing = true; label.fontSizeMin = 23; label.fontSizeMax = 33; label.textWrappingMode = TextWrappingModes.NoWrap; label.margin = Vector4.zero;
                var socket = ExpeditionUi.Rect(row, "SignalSocket", .875f, .14f, .972f, .86f).gameObject.AddComponent<ExpeditionCircleGraphic>();
                socket.color = ExpeditionShelterSurface.Brass;
                socket.raycastTarget = false;
                var glass = ExpeditionUi.Rect(socket.transform, "Glass", .12f, .12f, .88f, .88f).gameObject.AddComponent<ExpeditionCircleGraphic>();
                glass.color = new Color32(70, 40, 26, 255);
                glass.raycastTarget = false;
                var lamp = ExpeditionUi.Rect(socket.transform, "Filament", .18f, .18f, .82f, .82f).gameObject.AddComponent<ExpeditionCircleGraphic>();
                lamp.color = ExpeditionTheme.Amber;
                lamp.raycastTarget = false;
                toggle.graphic = lamp;
                _states[i] = ExpeditionUi.Text(row, "State", "", 24, .71f, .05f, .865f, .95f, TextAlignmentOptions.Center);
                _states[i].color = ExpeditionTheme.Muted; _states[i].margin = Vector4.zero;
                toggle.onValueChanged.AddListener(value => Set(index, value));
                _toggles[i] = toggle;
            }
            var note = ExpeditionUi.Text(_root, "AdultVoiceNote", "Для 18+ пока используются текущие звуки", 24, .055f, .033f, .945f, .136f);
            note.color = ExpeditionTheme.Muted; note.enableAutoSizing = true; note.fontSizeMin = 18; note.fontSizeMax = 24; note.margin = Vector4.zero;
            Refresh();
        }
        private static void Set(int index, bool value)
        {
            if (index == 0) AudioPreferences.SetSoundEnabled(value);
            else if (index == 1) AudioPreferences.SetMusicEnabled(value);
            else AudioPreferences.SetAdultVoiceEnabled(value);
            ExpeditionAudio.Play(ExpeditionCue.UiClick);
        }
        private void OnEnable() { AudioPreferences.Changed += Refresh; Refresh(); }
        private void OnDisable() { AudioPreferences.Changed -= Refresh; }
        private void Refresh()
        {
            if (_root == null) return;
            var values = new[] { AudioPreferences.SoundEnabled, AudioPreferences.MusicEnabled, AudioPreferences.AdultVoiceEnabled };
            for (var i = 0; i < _toggles.Length; i++)
            {
                _toggles[i].SetIsOnWithoutNotify(values[i]); _states[i].text = values[i] ? "Вкл" : "Выкл";
                _states[i].color = values[i] ? ExpeditionTheme.Amber : ExpeditionTheme.Muted;
            }
        }
    }
}
