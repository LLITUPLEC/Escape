using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Animated flame confined to the portrait rim. The centre is never drawn.</summary>
    [DisallowMultipleComponent]
    public sealed class Match3AvatarFuryFx : MonoBehaviour
    {
        public const string FxRootName = "FuryFx";
        private RectTransform _frame, _root;
        private Match3FuryRingGraphic _front, _back;
        private bool _active;
        private float _started;
        public bool IsActive => _active;

        public static Match3AvatarFuryFx Ensure(Image avatarImage, Image borderImage = null)
        {
            if (avatarImage == null) return null;
            var fx = avatarImage.GetComponent<Match3AvatarFuryFx>();
            if (fx == null) fx = avatarImage.gameObject.AddComponent<Match3AvatarFuryFx>();
            var panel = avatarImage.GetComponentInParent<Match3PlayerPanel>();
            fx._frame = panel != null ? panel.PortraitFrame : (borderImage != null ? borderImage.rectTransform : avatarImage.rectTransform);
            fx.EnsureBuilt(); return fx;
        }
        public void SetActive(bool active)
        {
            EnsureBuilt();
            if (active && !_active) _started = Time.unscaledTime;
            _active = active; enabled = active;
            if (_root != null) _root.gameObject.SetActive(active);
            if (active) Tick();
        }
        public void EnsureBuilt()
        {
            if (_root != null) return;
            if (_frame == null) _frame = transform as RectTransform;
            if (_frame == null || _frame.parent == null) return;
            var legacy = transform.Find(FxRootName);
            if (legacy != null) { legacy.gameObject.SetActive(false); Destroy(legacy.gameObject); }
            _root = new GameObject(FxRootName, typeof(RectTransform)).GetComponent<RectTransform>();
            _root.SetParent(_frame.parent, false); _root.SetAsLastSibling();
            _back = Layer("FlameA"); _front = Layer("FlameB"); Fit(); _root.gameObject.SetActive(_active);
        }
        private Match3FuryRingGraphic Layer(string name)
        {
            var graphic = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Match3FuryRingGraphic)).GetComponent<Match3FuryRingGraphic>();
            graphic.transform.SetParent(_root, false);
            var rt = graphic.rectTransform; rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            graphic.raycastTarget = false; return graphic;
        }
        private void LateUpdate() { if (_active) { Fit(); Tick(); } }
        private void Fit()
        {
            if (_root == null || _frame == null) return;
            _root.anchorMin = _frame.anchorMin; _root.anchorMax = _frame.anchorMax; _root.pivot = _frame.pivot;
            _root.offsetMin = _frame.offsetMin - Vector2.one * 8; _root.offsetMax = _frame.offsetMax + Vector2.one * 8;
            _root.localRotation = _frame.localRotation; _root.localScale = _frame.localScale;
        }
        private void Tick()
        {
            if (_back == null) return;
            var frames = Match3BattleFxSprites.Fury;
            if (frames.Length == 0) { _back.enabled = _front.enabled = false; return; }
            _back.enabled = _front.enabled = true;
            var phase = (Time.unscaledTime - _started) * 15f;
            var current = Mathf.FloorToInt(phase) % frames.Length;
            var fade = phase - Mathf.Floor(phase);
            var intensity = Mathf.Clamp01((Time.unscaledTime - _started) / .16f);
            _back.SetFrame(frames[current]); _front.SetFrame(frames[(current + 1) % frames.Length]);
            _back.color = new Color(1, 1, 1, intensity * (1 - fade)); _front.color = new Color(1, 1, 1, intensity * fade);
        }
        private void OnDisable() { if (_root != null) _root.gameObject.SetActive(false); }
        private void OnEnable() { if (_active && _root != null) _root.gameObject.SetActive(true); }
        private void OnDestroy() { if (_root != null) Destroy(_root.gameObject); }
    }

    /// <summary>Four cropped bands guarantee a clear portrait even if the source contains central smoke.</summary>
    public sealed class Match3FuryRingGraphic : MaskableGraphic
    {
        private static readonly float[] Destination = { 0, .14f, .86f, 1 };
        private static readonly float[] Source = { 0, .32f, .68f, 1 };
        private Sprite _sprite;
        public override Texture mainTexture => _sprite != null ? _sprite.texture : Texture2D.whiteTexture;
        public void SetFrame(Sprite value) { if (_sprite == value) return; _sprite = value; SetAllDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (_sprite == null) return;
            var r = GetPixelAdjustedRect(); var uv = DataUtility.GetOuterUV(_sprite);
            // Remap the broad photographed source rim into a narrow UI rim rather than cutting its hot cores away.
            for (var y=0;y<3;y++) for(var x=0;x<3;x++)
                if(x != 1 || y != 1) Band(vh,r,uv,Destination[x],Destination[y],Destination[x+1],Destination[y+1],Source[x],Source[y],Source[x+1],Source[y+1]);
        }
        private void Band(VertexHelper vh, Rect r, Vector4 uv, float x0, float y0, float x1, float y1, float u0, float v0, float u1, float v1)
        {
            var n = vh.currentVertCount; var c = (Color32)color;
            vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x0),Mathf.Lerp(r.yMin,r.yMax,y0)), c, new Vector2(Mathf.Lerp(uv.x,uv.z,u0),Mathf.Lerp(uv.y,uv.w,v0)));
            vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x0),Mathf.Lerp(r.yMin,r.yMax,y1)), c, new Vector2(Mathf.Lerp(uv.x,uv.z,u0),Mathf.Lerp(uv.y,uv.w,v1)));
            vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x1),Mathf.Lerp(r.yMin,r.yMax,y1)), c, new Vector2(Mathf.Lerp(uv.x,uv.z,u1),Mathf.Lerp(uv.y,uv.w,v1)));
            vh.AddVert(new Vector3(Mathf.Lerp(r.xMin,r.xMax,x1),Mathf.Lerp(r.yMin,r.yMax,y0)), c, new Vector2(Mathf.Lerp(uv.x,uv.z,u1),Mathf.Lerp(uv.y,uv.w,v0)));
            vh.AddTriangle(n,n+1,n+2); vh.AddTriangle(n,n+2,n+3);
        }
    }
}
