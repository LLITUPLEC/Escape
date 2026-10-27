using Project.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Audio
{
    /// <summary>Compact, device-wide listening controls inside the existing account settings sheet.</summary>
    [DisallowMultipleComponent]
    public sealed class AudioSettingsBlock : MonoBehaviour
    {
        private RectTransform _root;
        private readonly Toggle[] _toggles = new Toggle[3];
        private readonly TMP_Text[] _states = new TMP_Text[3];
        public void Build(RectTransform parent)
        {
            if (_root != null) { Refresh(); return; }
            _root = ExpeditionUi.Panel(parent, "ExpeditionAudioSettings", .065f, .65f, .935f, .825f).rectTransform;
            var caption = ExpeditionUi.Text(_root, "AudioCaption", "ЗВУК И МУЗЫКА", 21, .035f, .80f, .965f, .94f);
            caption.color = ExpeditionTheme.Amber; caption.margin = Vector4.zero;
            caption.enableAutoSizing = true; caption.fontSizeMin = 17; caption.fontSizeMax = 21;
            caption.textWrappingMode = TextWrappingModes.NoWrap;
            var labels = new[] { "Звуки", "Фоновая музыка", "Озвучка 18+" };
            var names = new[] { "SoundToggle", "MusicToggle", "AdultVoiceToggle" };
            for (var i = 0; i < 3; i++)
            {
                var index = i;
                var y = .59f - i * .20f;
                var row = ExpeditionUi.Rect(_root, names[i], .035f, y, .965f, y + .20f);
                var surface = row.gameObject.AddComponent<Image>(); surface.color = new Color(0, 0, 0, .001f); surface.raycastTarget = true;
                var toggle = row.gameObject.AddComponent<Toggle>(); toggle.targetGraphic = surface;
                var label = ExpeditionUi.Text(row, "Label", labels[i], 26, .01f, .06f, .69f, .94f);
                label.enableAutoSizing = true; label.fontSizeMin = 21; label.fontSizeMax = 26; label.textWrappingMode = TextWrappingModes.NoWrap; label.margin = Vector4.zero;
                var box = ExpeditionUi.Panel(row, "Checkbox", .895f, .12f, .985f, .88f);
                var check = ExpeditionUi.Fill(box.transform, "Check", .23f, .23f, .77f, .77f, ExpeditionTheme.Amber);
                check.sprite = ModalPanelCloseButton.WhiteSprite(); toggle.graphic = check;
                _states[i] = ExpeditionUi.Text(row, "State", "", 19, .70f, .05f, .88f, .95f, TextAlignmentOptions.Center);
                _states[i].color = ExpeditionTheme.Muted; _states[i].margin = Vector4.zero;
                toggle.onValueChanged.AddListener(value => Set(index, value));
                _toggles[i] = toggle;
            }
            var note = ExpeditionUi.Text(_root, "AdultVoiceNote", "Для 18+ пока используются текущие звуки", 17, .035f, .09f, .965f, .18f);
            note.color = ExpeditionTheme.Muted; note.enableAutoSizing = true; note.fontSizeMin = 15; note.fontSizeMax = 17; note.margin = Vector4.zero;
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
