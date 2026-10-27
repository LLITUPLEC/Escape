using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Uniformly fits opaque body geometry and locks its solid foot to the illustrated floor, including background crop and safe areas.</summary>
    public sealed class ExpeditionCombatantFigure : MonoBehaviour
    {
        private Image _source, _figure;
        private RectTransform _rect,_backdrop,_parent,_header;
        private Image _shadow;
        private static Sprite _shadowSprite;
        private ExpeditionCombatantGrounding.Footprint _footprint;
        private Vector2 _parentSize,_backdropSize;
        private Matrix4x4 _parentMatrix,_backdropMatrix;
        private float _groundX;
        private float _floorUvY=ExpeditionCombatantGrounding.FloorUvY,_heightRatio=.165f,_widthRatio=.32f;
        private bool _dirty=true;
        private Vector2 _rest;
        private float _nextFrame, _phase;
        private bool _player;
        private bool _mirror;
        private Sprite _explorer;
        public Vector2 GroundLocal { get; private set; }
        public bool HasOpaqueMetadata => _footprint!=null;
        public Vector3 FootWorld => _rect!=null?_rect.TransformPoint(Vector3.zero):transform.position;
#if UNITY_EDITOR
        private Sprite _previewSprite;
        private bool _preview;
        public void BindPreview(Sprite sprite,bool player)
        {
            _preview=true;_previewSprite=sprite;Initialize(null,player);ApplySprite(sprite);
        }
#endif
        public void Bind(Image source, bool player)
        {
            Initialize(source,player);ApplySprite(ResolveSprite());
        }
        private void Initialize(Image source,bool player)
        {
            _source = source;
            _figure = GetComponent<Image>();
            _rect = (RectTransform)transform;
            _phase = player ? 0 : 1.6f;
            _player = player;
            _explorer = Resources.Load<Sprite>("Expedition/Art/hero");
        }
        private Sprite ResolveSprite()
        {
#if UNITY_EDITOR
            if(_preview)return _previewSprite;
#endif
            return _player?Project.Character.CharacterAppearance.Body(_explorer):_source!=null?_source.sprite:null;
        }
        public void GroundTo(RectTransform backdrop,RectTransform header,float sourceX)
        {
            _backdrop=backdrop;_header=header;_parent=_rect.parent as RectTransform;_groundX=sourceX;_dirty=true;
            RefreshGrounding();
        }
        public void SetArenaProfile(float floorUvY,float heightRatio,float widthRatio)
        {
            _floorUvY=Mathf.Clamp01(floorUvY);_heightRatio=Mathf.Clamp(heightRatio,.05f,.3f);_widthRatio=Mathf.Clamp(widthRatio,.1f,.5f);
            _dirty=true;RefreshGrounding();
        }
        private void Update()
        {
            if (_figure == null || Time.unscaledTime < _nextFrame) return;
            _nextFrame = Time.unscaledTime + .08f;
            var sprite = ResolveSprite();
            if (_figure.sprite != sprite) ApplySprite(sprite);
            // Breathe uniformly around the foot pivot. Translation would make
            // the soles float above or sink below the floor every idle cycle.
            var breathe=1+Mathf.Sin(Time.unscaledTime*1.7f+_phase)*.0015f;
            _rect.localScale=new Vector3((_mirror?-1:1)*breathe,breathe,1);
        }
        private void LateUpdate()=>RefreshGrounding();
        private void ApplySprite(Sprite sprite)
        {
            _figure.sprite = sprite;
            _figure.enabled = sprite != null;
            ExpeditionCombatantGrounding.TryGet(sprite,out _footprint);_dirty=true;
            // New mine bodies face left in their source art. Keep the opponent
            // unmirrored; turn the local hero toward it. Other art keeps its prior rule.
            _mirror = ShouldMirror(sprite);
            _rect.localScale = new Vector3(_mirror ? -1 : 1, 1, 1);
            if(_shadow!=null)_shadow.enabled=sprite!=null;
            RefreshGrounding();
        }
        private bool ShouldMirror(Sprite sprite)
        {
            if (_player) return true;
            if (sprite != null && sprite.name.StartsWith("mine-",System.StringComparison.Ordinal) &&
                int.TryParse(sprite.name.Substring(5),out var floor) && floor>=1 && floor<=12) return false;
            return sprite != _explorer;
        }
        public void RefreshGrounding()
        {
            if(_rect==null||_parent==null||_backdrop==null||_figure.sprite==null||_parent.rect.height<=1||_backdrop.rect.height<=1)return;
            if(!_dirty&&_parentSize==_parent.rect.size&&_backdropSize==_backdrop.rect.size&&_parentMatrix==_parent.localToWorldMatrix&&_backdropMatrix==_backdrop.localToWorldMatrix)return;
            _dirty=false;_parentSize=_parent.rect.size;_backdropSize=_backdrop.rect.size;
            _parentMatrix=_parent.localToWorldMatrix;_backdropMatrix=_backdrop.localToWorldMatrix;
            GroundLocal=ExpeditionCombatantGrounding.GroundPoint(_backdrop,_parent,_groundX,_floorUvY);
            var headerBottom=_parent.rect.yMin+_parent.rect.height*.918f;
            if(_header!=null){var corners=new Vector3[4];_header.GetWorldCorners(corners);headerBottom=_parent.InverseTransformPoint(corners[0]).y;}
            var maxHeight=Mathf.Max(1,Mathf.Min(_parent.rect.height*_heightRatio,headerBottom-GroundLocal.y-_parent.rect.height*.012f));
            var maxWidth=Mathf.Min(_parent.rect.width*_widthRatio,maxHeight*1.08f);
            _rect.anchorMin=_rect.anchorMax=Vector2.zero;
            _rect.pivot=_footprint?.Contact??new Vector2(.5f,0);
            var fullSize=ExpeditionCombatantGrounding.FullImageSize(_figure.sprite,_footprint,maxWidth,maxHeight);
            // A wide, asymmetric body can extend far to one side of its solid-foot
            // contact. On a tall cropped backdrop that foot is close to the safe edge.
            // Keep the authored floor contact fixed and reduce uniformly until both
            // opaque sides fit; centering the sprite would make the feet slide.
            var solid=_footprint?.SolidBounds??new Rect(0,0,1,1);
            var direction=_mirror?-1f:1f;
            var edgeA=(solid.x-_rect.pivot.x)*fullSize.x*direction;
            var edgeB=(solid.xMax-_rect.pivot.x)*fullSize.x*direction;
            var minimum=Mathf.Min(edgeA,edgeB);var maximum=Mathf.Max(edgeA,edgeB);
            var margin=Mathf.Max(2,_parent.rect.width*.005f);
            var leftSpace=Mathf.Max(1,GroundLocal.x-_parent.rect.xMin-margin);
            var rightSpace=Mathf.Max(1,_parent.rect.xMax-GroundLocal.x-margin);
            var sideScale=1f;
            if(minimum<0)sideScale=Mathf.Min(sideScale,leftSpace/-minimum);
            if(maximum>0)sideScale=Mathf.Min(sideScale,rightSpace/maximum);
            _rect.sizeDelta=fullSize*Mathf.Clamp(sideScale,.0001f,1);
            _rest=GroundLocal-_parent.rect.min;_rect.anchoredPosition=_rest;
            if(_shadow==null)
            {
                var go=new GameObject(name+"ContactShadow",typeof(RectTransform),typeof(Image));go.transform.SetParent(_parent,false);
                go.transform.SetSiblingIndex(_rect.GetSiblingIndex());_shadow=go.GetComponent<Image>();_shadow.sprite=ShadowSprite();
                _shadow.color=new Color(0,0,0,.24f);_shadow.raycastTarget=false;
            }
            var shadow=_shadow.rectTransform;shadow.anchorMin=shadow.anchorMax=Vector2.zero;shadow.pivot=new Vector2(.5f,.5f);
            shadow.anchoredPosition=_rest;
            var solidWidth=_rect.sizeDelta.x*(_footprint?.w??1);
            var width=Mathf.Min(maxWidth,solidWidth*.68f);shadow.sizeDelta=new Vector2(width,Mathf.Clamp(width*.075f,6,24));_shadow.enabled=_figure.enabled;
        }
        private static Sprite ShadowSprite()
        {
            if(_shadowSprite!=null)return _shadowSprite;
            var texture=new Texture2D(64,16,TextureFormat.RGBA32,false){name="CombatantContactShadow",hideFlags=HideFlags.HideAndDontSave};
            for(var y=0;y<16;y++)for(var x=0;x<64;x++)
            {
                var u=(x+.5f)/32-1;var v=(y+.5f)/8-1;var a=Mathf.Clamp01(1-u*u-v*v);
                texture.SetPixel(x,y,new Color(1,1,1,a*a));
            }
            texture.wrapMode=TextureWrapMode.Clamp;texture.filterMode=FilterMode.Bilinear;
            texture.Apply(false,true);_shadowSprite=Sprite.Create(texture,new Rect(0,0,64,16),new Vector2(.5f,.5f),100,0,SpriteMeshType.FullRect);
            _shadowSprite.name="CombatantContactShadow";_shadowSprite.hideFlags=HideFlags.HideAndDontSave;return _shadowSprite;
        }
        private void OnDisable() { if (_rect != null) _rect.anchoredPosition = _rest; }
        private void OnDestroy(){if(_shadow!=null){if(Application.isPlaying)Destroy(_shadow.gameObject);else DestroyImmediate(_shadow.gameObject);}}
    }
}
