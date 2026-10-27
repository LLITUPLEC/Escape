using UnityEngine;
using UnityEngine.UI;

namespace Project.Character.UI
{
    /// <summary>Keeps a scene-owned body/portrait image in step with the local cosmetic selection.</summary>
    [DisallowMultipleComponent]
    public sealed class CharacterAppearanceImage : MonoBehaviour
    {
        private Image _image;
        private Sprite _fallback;
        private bool _avatar;
        private float _nextCheck;
        private string _lastScope;
        private bool _waitingForGender, _enabledBeforeWaiting;
        public static void Bind(Image image, bool avatar = false)
        {
            if (image == null) return;
            var binding = Project.UI.ExpeditionUi.Ensure<CharacterAppearanceImage>(image.gameObject);
            binding._image = image; binding._fallback = image.sprite; binding._avatar = avatar;
            binding.Refresh();
        }
        private void OnEnable() { CharacterAppearance.Changed += Refresh; _nextCheck = 0; }
        private void OnDisable() { CharacterAppearance.Changed -= Refresh; }
        private void Update()
        {
            if (_lastScope == Project.Nakama.LocalAccountCacheScope.Current && Time.unscaledTime < _nextCheck) return;
            _nextCheck = Time.unscaledTime + .4f;
            Refresh();
        }
        private void Refresh()
        {
            if (_image == null) return;
            _lastScope = Project.Nakama.LocalAccountCacheScope.Current;
            if (!AccountGender.IsLoaded)
            {
                if (!_waitingForGender) { _enabledBeforeWaiting = _image.enabled; _waitingForGender = true; }
                _image.sprite = null; _image.enabled = false;
                return;
            }
            if (_waitingForGender) { _image.enabled = _enabledBeforeWaiting; _waitingForGender = false; }
            _image.sprite = _avatar ? CharacterAppearance.Avatar(_fallback) : CharacterAppearance.Body(_fallback);
            _image.preserveAspect = true;
        }
    }
}
