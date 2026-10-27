using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Native Venus/Mars pictograms with a square drawing region, independent of fonts and layout fitters.</summary>
    [ExecuteAlways]
    [RequireComponent(typeof(CanvasRenderer))]
    [DisallowMultipleComponent]
    public sealed class AccountGenderSymbol : MaskableGraphic
    {
        [SerializeField] private bool female;
        public bool Female
        {
            get => female;
            set { if (female == value) return; female = value; SetVerticesDirty(); }
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var center = female ? new Vector2(.5f, .65f) : new Vector2(.40f, .40f);
            const float radius = .245f, thickness = .065f;
            for (var i = 0; i < 40; i++)
            {
                var a = i * Mathf.PI * 2 / 40; var b = (i + 1) * Mathf.PI * 2 / 40;
                var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                Quad(vh, center + da * (radius - thickness), center + da * radius,
                    center + db * radius, center + db * (radius - thickness));
            }
            if (female)
            {
                Line(vh, new Vector2(.5f, .44f), new Vector2(.5f, .06f), thickness);
                Line(vh, new Vector2(.32f, .22f), new Vector2(.68f, .22f), thickness);
            }
            else
            {
                Line(vh, new Vector2(.55f, .55f), new Vector2(.87f, .87f), thickness);
                Line(vh, new Vector2(.66f, .87f), new Vector2(.9f, .87f), thickness);
                Line(vh, new Vector2(.87f, .66f), new Vector2(.87f, .9f), thickness);
            }
        }

        private void Line(VertexHelper vh, Vector2 a, Vector2 b, float thickness)
        {
            var direction = (b - a).normalized;
            var normal = new Vector2(-direction.y, direction.x) * thickness * .5f;
            Quad(vh, a - normal, a + normal, b + normal, b - normal);
        }

        private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            var rect = rectTransform.rect;
            var side = Mathf.Min(rect.width, rect.height);
            var origin = rect.center - Vector2.one * (side * .5f);
            var index = vh.currentVertCount;
            Vertex(vh, origin, side, a); Vertex(vh, origin, side, b);
            Vertex(vh, origin, side, c); Vertex(vh, origin, side, d);
            vh.AddTriangle(index, index + 1, index + 2); vh.AddTriangle(index + 2, index + 3, index);
        }

        private void Vertex(VertexHelper vh, Vector2 origin, float side, Vector2 point) =>
            vh.AddVert(new Vector3(origin.x + point.x * side, origin.y + point.y * side), color, Vector2.zero);
    }
}
