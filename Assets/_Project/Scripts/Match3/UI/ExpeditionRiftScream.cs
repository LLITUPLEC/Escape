using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>A violet sound front from the boss's mouth, followed by pressure rings at the hero. No damage calculation.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionRiftScream : MaskableGraphic
    {
        public const float Lifetime=1.35f;
        private float _started;
        private Vector2 _source,_target;
        public static ExpeditionRiftScream Play(Match3PlayerPanel source,Match3PlayerPanel target)
        {
            if(target?.avatarImage==null)return null;
            var canvas=target.GetComponentInParent<Canvas>();
            var safe=canvas!=null?Project.UI.ExpeditionUi.Find(canvas.transform,"ExpeditionBattleSafeArea") as RectTransform:null;
            if(safe==null)return null;
            var root=Project.UI.ExpeditionUi.Rect(safe,"RiftScreamPressure",0,0,1,1);
            var fx=root.gameObject.AddComponent<ExpeditionRiftScream>();fx.raycastTarget=false;fx._started=Time.unscaledTime;
            var bodySource=Project.UI.ExpeditionUi.Find(safe,"ExpeditionOpponentFigure") as RectTransform;
            var bodyTarget=Project.UI.ExpeditionUi.Find(safe,"ExpeditionPlayerFigure") as RectTransform;
            Vector3 Mouth(RectTransform body,RectTransform fallback)
            {
                var owner=body!=null?body:fallback;
                if(owner==null)return Vector3.zero;
                return owner.TransformPoint(new Vector2(Mathf.Lerp(owner.rect.xMin,owner.rect.xMax,.57f),Mathf.Lerp(owner.rect.yMin,owner.rect.yMax,.82f)));
            }
            fx._source=root.InverseTransformPoint(Mouth(bodySource,source?.avatarImage?.rectTransform));
            fx._target=root.InverseTransformPoint(Mouth(bodyTarget,target.avatarImage.rectTransform));
            fx.color=new Color32(203,129,255,255);return fx;
        }
        private void Update(){if(Time.unscaledTime-_started>Lifetime){Destroy(gameObject);return;}SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var elapsed=Time.unscaledTime-_started;
            var direction=(_target-_source).normalized;if(direction.sqrMagnitude<.01f)direction=Vector2.left;
            var side=new Vector2(-direction.y,direction.x);
            for(var wave=0;wave<4;wave++)
            {
                var progress=(elapsed-wave*.10f)/.65f;if(progress<=0||progress>=1)continue;
                var center=Vector2.Lerp(_source,_target,progress);var radius=13+46*progress;
                var tint=color;tint.a*=Mathf.Sin(progress*Mathf.PI)*.82f;
                for(var segment=0;segment<32;segment++)
                {
                    var a=Mathf.Lerp(-1.35f,1.35f,segment/32f);var b=Mathf.Lerp(-1.35f,1.35f,(segment+1)/32f);
                    var p=center+direction*Mathf.Cos(a)*radius*.35f+side*Mathf.Sin(a)*radius;
                    var q=center+direction*Mathf.Cos(b)*radius*.35f+side*Mathf.Sin(b)*radius;
                    Segment(vh,p,q,2.5f+3*(1-progress),tint);
                }
            }
            for(var ring=0;ring<3;ring++)
            {
                var progress=(elapsed-.48f-ring*.11f)/.62f;if(progress<=0||progress>=1)continue;
                var radius=15+65*progress;var tint=color;tint.a*=(1-progress)*.72f;
                for(var segment=0;segment<64;segment++)
                {
                    var a=segment*Mathf.PI*2/64;var b=(segment+1)*Mathf.PI*2/64;
                    Segment(vh,_target+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*radius,_target+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*radius,2+5*(1-progress),tint);
                }
            }
        }
        private static void Segment(VertexHelper vh,Vector2 from,Vector2 to,float width,Color tint)
        {
            var normal=new Vector2(-(to-from).y,(to-from).x).normalized*width;var index=vh.currentVertCount;var vertex=UIVertex.simpleVert;vertex.color=tint;
            vertex.position=from-normal;vh.AddVert(vertex);vertex.position=from+normal;vh.AddVert(vertex);
            vertex.position=to+normal;vh.AddVert(vertex);vertex.position=to-normal;vh.AddVert(vertex);
            vh.AddTriangle(index,index+1,index+2);vh.AddTriangle(index,index+2,index+3);
        }
    }
}
