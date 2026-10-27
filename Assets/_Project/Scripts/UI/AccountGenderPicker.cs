using Project.Character;
using Project.Nakama;
using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Two icon-only registration choices. The server locks this choice once.</summary>
    public sealed class AccountGenderPicker : MonoBehaviour
    {
        private readonly Button[] _buttons = new Button[2];
        private readonly AccountGenderSymbol[] _symbols = new AccountGenderSymbol[2];
        private bool _built, _female, _busy, _seenLoaded;
        private string _scope;
        public bool SelectedFemale => _female;

        public static AccountGenderPicker Create(Transform parent, float x0, float y0, float x1, float y1)
        {
            var root = ExpeditionUi.Rect(parent, "RegistrationGender", x0, y0, x1, y1);
            var picker = root.gameObject.AddComponent<AccountGenderPicker>();
            for (var i = 0; i < 2; i++)
            {
                var index = i;
                var button = ExpeditionUi.Button(root, i == 0 ? "ChooseMale" : "ChooseFemale", "",
                    i == 0 ? 0 : .525f, 0, i == 0 ? .475f : 1, 1, () => picker.Select(index == 1), false, 1);
                var iconRoot = ExpeditionUi.Rect(button.transform, "GenderSymbol", .12f, .08f, .88f, .92f);
                var icon = iconRoot.gameObject.AddComponent<AccountGenderSymbol>();
                icon.Female = i == 1; icon.raycastTarget = false;
                picker._buttons[i] = button; picker._symbols[i] = icon;
            }
            picker._built = true;
            picker.Refresh();
            return picker;
        }

        public void SetBusy(bool busy) { _busy = busy; Refresh(); }
        private void Select(bool female) { if (!_busy && AccountGender.CanChoose) { _female = female; Refresh(); } }
        private void OnEnable() { AccountGender.Changed += Refresh; Refresh(); }
        private void OnDisable() { AccountGender.Changed -= Refresh; }
        private void Refresh()
        {
            if (!_built) return;
            var scope = LocalAccountCacheScope.Current;
            if (_scope != scope) { _scope = scope; _female = false; _seenLoaded = false; }
            if (AccountGender.IsLoaded && !_seenLoaded) { _female = AccountGender.IsFemale; _seenLoaded = true; }
            if (AccountGender.IsLoaded && !AccountGender.CanChoose) _female = AccountGender.IsFemale;
            for (var i = 0; i < 2; i++)
            {
                var selected = _female == (i == 1);
                ExpeditionUi.StyleButton(_buttons[i], selected, 1);
                _buttons[i].interactable = !_busy && AccountGender.IsLoaded && AccountGender.CanChoose;
                _symbols[i].color = selected ? new Color32(37, 31, 22, 255) : ExpeditionTheme.Ivory;
            }
        }
    }

}
