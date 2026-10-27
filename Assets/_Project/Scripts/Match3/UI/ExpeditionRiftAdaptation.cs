using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Ascending red crystal sparks on the boss body. No game rules, stats or input ownership.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionRiftAdaptation : MaskableGraphic
    {
        public const float Lifetime=1.7f;
        private float _started;
        public static ExpeditionRiftAdaptation Play(Match3PlayerPanel boss)
        {
            if(boss==null)return null;
            var canvas=boss.GetComponentInParent<Canvas>();
            var body=canvas!=null?Project.UI.ExpeditionUi.Find(canvas.transform,"ExpeditionOpponentFigure") as RectTransform:null;
            var anchor=body!=null?body:boss.avatarImage!=null?boss.avatarImage.rectTransform:null;
            if(anchor==null)return null;
            var root=Project.UI.ExpeditionUi.Rect(anchor,"RiftAdaptationEmbers",-.18f,-.08f,1.18f,1.12f);
            var fx=root.gameObject.AddComponent<ExpeditionRiftAdaptation>();fx.raycastTarget=false;fx._started=Time.unscaledTime;return fx;
        }
        private void Update(){if(Time.unscaledTime-_started>Lifetime){Destroy(gameObject);return;}SetVerticesDirty();}
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();var time=Time.unscaledTime-_started;var bounds=rectTransform.rect;
            for(var i=0;i<42;i++)
            {
                var delay=(i%7)*.065f;var progress=(time-delay)/1.17f;if(progress<=0||progress>=1)continue;
                var x=bounds.xMin+bounds.width*(.12f+.76f*((i*.618034f)%1));
                x+=Mathf.Sin(progress*5+i*.7f)*bounds.width*.038f;
                var y=Mathf.Lerp(bounds.yMin,bounds.yMax,progress);
                var alpha=Mathf.Sin(progress*Mathf.PI)*.94f;
                var width=2.4f+(i%4)*1.0f;var length=7+(i%5)*4;
                Quad(vh,x,y-length,width*.42f,length,new Color(1,.035f,.075f,alpha*.25f));
                Diamond(vh,x,y,width,length*.32f,new Color(1,.12f,.10f,alpha));
                Diamond(vh,x,y,width*.38f,length*.13f,new Color(1,.78f,.40f,alpha));
            }
        }
        private static void Quad(VertexHelper vh,float x,float y,float width,float height,Color color)
        {
            var n=vh.currentVertCount;var v=UIVertex.simpleVert;v.color=color;
            v.position=new Vector2(x-width,y);vh.AddVert(v);v.position=new Vector2(x+width,y);vh.AddVert(v);
            v.position=new Vector2(x+width,y+height);vh.AddVert(v);v.position=new Vector2(x-width,y+height);vh.AddVert(v);
            vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
        }
        private static void Diamond(VertexHelper vh,float x,float y,float width,float height,Color color)
        {
            var n=vh.currentVertCount;var v=UIVertex.simpleVert;v.color=color;
            v.position=new Vector2(x-width,y);vh.AddVert(v);v.position=new Vector2(x,y-height);vh.AddVert(v);
            v.position=new Vector2(x+width,y);vh.AddVert(v);v.position=new Vector2(x,y+height);vh.AddVert(v);
            vh.AddTriangle(n,n+1,n+2);vh.AddTriangle(n,n+2,n+3);
        }
    }
}
