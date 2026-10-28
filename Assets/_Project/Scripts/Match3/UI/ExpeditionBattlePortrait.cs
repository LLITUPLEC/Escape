using Project.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>A head crop for the header; the source Image keeps its full-body sprite for the scene figure.</summary>
    [DisallowMultipleComponent]
    public sealed class ExpeditionBattlePortrait : MonoBehaviour
    {
        private Image _source;
        private RawImage _portrait;
        private Sprite _lastSprite;
        private Sprite _fullBodySprite;
        private Sprite _avatarSprite, _avatarBodySprite;
        private float _headCenterX = .5f;
        private bool _localPlayer;

        /// <summary>The figure retains the body sprite; its header uses the independently selected avatar.</summary>
        public static void MarkLocalPlayer(Image source)
        {
            if (source == null) return;
            var view = ExpeditionUi.Ensure<ExpeditionBattlePortrait>(source.gameObject);
            view._localPlayer = true;
            MarkFullBody(source);
            view.Refresh(true);
        }

        // This opt-in comes from the full-body source assignment, never from a
        // square/tall aspect-ratio guess. Unmarked portrait sprites remain intact.
        public static void MarkFullBody(Image source, float headCenterX = .5f)
        {
            if (source == null) return;
            var view = ExpeditionUi.Ensure<ExpeditionBattlePortrait>(source.gameObject);
            if (view._fullBodySprite == source.sprite && Mathf.Approximately(view._headCenterX, headCenterX)) return;
            view._source = source;
            view._fullBodySprite = source.sprite;
            view._headCenterX = Mathf.Clamp01(headCenterX);
            view.Refresh(true);
        }

        /// <summary>Use a separately authored portrait in the header while preserving the scene figure's body sprite.</summary>
        public static void MarkAvatar(Image source, Sprite avatar)
        {
            if (source == null) return;
            var view = ExpeditionUi.Ensure<ExpeditionBattlePortrait>(source.gameObject);
            view._source = source;
            view._localPlayer = false;
            view._avatarSprite = avatar;
            view._avatarBodySprite = source.sprite;
            view.Refresh(true);
        }

        public void Bind(Image source)
        {
            _source = source;
            if (_portrait == null)
            {
                var existing = transform.Find("ExpeditionHeadCrop");
                _portrait = existing != null ? existing.GetComponent<RawImage>() : null;
                if (_portrait == null)
                    _portrait = ExpeditionUi.Rect(transform, "ExpeditionHeadCrop", 0, 0, 1, 1).gameObject.AddComponent<RawImage>();
                _portrait.raycastTarget = false;
                _portrait.transform.SetAsFirstSibling();
            }
            Refresh(true);
        }

        private void LateUpdate() => Refresh(false);

        private void Refresh(bool force)
        {
            if (_source == null || _portrait == null) return;
            _source.enabled = false;
            var selectedAvatar = _localPlayer ? Project.Character.CharacterAppearance.Avatar()
                : _source.sprite == _avatarBodySprite ? _avatarSprite : null;
            var sprite = selectedAvatar != null ? selectedAvatar : _source.sprite;
            if (!force && sprite == _lastSprite) return;
            _lastSprite = sprite;
            _portrait.enabled = sprite != null;
            if (sprite == null) return;
            var region = sprite.textureRect;
            var crop = new Rect(.04f, .04f, .92f, .92f);
            if (selectedAvatar == null && (_localPlayer || sprite == _fullBodySprite))
            {
                const float height = .28f;
                var width = Mathf.Min(1, height * 1.05f * region.height / region.width);
                crop = new Rect(Mathf.Clamp(_headCenterX - width * .5f, 0, 1 - width), .71f, width, height);
            }
            _portrait.texture = sprite.texture;
            _portrait.uvRect = new Rect((region.x + region.width * crop.x) / sprite.texture.width,
                (region.y + region.height * crop.y) / sprite.texture.height,
                region.width * crop.width / sprite.texture.width, region.height * crop.height / sprite.texture.height);
            _portrait.color = Color.white;
            var fit = ExpeditionUi.Ensure<AspectRatioFitter>(_portrait.gameObject);
            fit.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fit.aspectRatio = region.width * crop.width / (region.height * crop.height);
        }
    }
}
