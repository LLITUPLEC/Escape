using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Resolution-independent metal radio ring or amber selection dot.</summary>
    [RequireComponent(typeof(CanvasRenderer), typeof(RectTransform))]
    public sealed class ExpeditionArenaRadioGraphic : MaskableGraphic
    {
        public bool DotOnly { get; private set; }
        public bool Selected { get; private set; }
        public bool Available { get; private set; } = true;
        public bool Focused { get; private set; }

        public void Configure(bool dotOnly, bool selected, bool available, bool focused)
        {
            if (DotOnly == dotOnly && Selected == selected && Available == available && Focused == focused) return;
            DotOnly = dotOnly; Selected = selected; Available = available; Focused = focused;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            var radius = Mathf.Min(rect.width, rect.height) * .5f;
            if (radius <= 0) return;
            if (DotOnly)
            {
                Disc(mesh, rect.center, radius * .45f, Available ? ExpeditionTheme.Amber : new Color32(119, 130, 125, 200));
                Disc(mesh, rect.center + new Vector2(-.065f, .065f) * radius, radius * .20f,
                    Available ? new Color32(255, 219, 149, 255) : new Color32(153, 164, 155, 200));
                return;
            }
            Disc(mesh, rect.center, radius * .97f, new Color32(5, 16, 19, 250));
            Annulus(mesh, rect.center, radius * .94f, radius * .67f);
            Disc(mesh, rect.center, radius * .63f, Available ? new Color32(14, 32, 37, 255) : new Color32(18, 28, 31, 255));
        }

        private void Annulus(VertexHelper mesh, Vector2 center, float outer, float inner)
        {
            const int segments = 48;
            for (var i = 0; i < segments; i++)
            {
                var a = i * Mathf.PI * 2 / segments; var b = (i + 1) * Mathf.PI * 2 / segments;
                var da = new Vector2(Mathf.Cos(a), Mathf.Sin(a)); var db = new Vector2(Mathf.Cos(b), Mathf.Sin(b));
                var start = mesh.currentVertCount;
                Add(mesh, center + da * outer, Metal(da.y)); Add(mesh, center + db * outer, Metal(db.y));
                Add(mesh, center + db * inner, Metal(db.y)); Add(mesh, center + da * inner, Metal(da.y));
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
        private Color Metal(float y)
        {
            var dark = new Color32(55, 76, 81, 255); var light = new Color32(197, 205, 191, 255);
            var result = Color.Lerp(dark, light, Mathf.Clamp01(.5f + y * .5f));
            if (Selected || Focused) result = Color.Lerp(result, ExpeditionTheme.Amber, Focused ? .85f : .5f);
            if (!Available) { result = Color.Lerp(new Color32(47, 59, 61, 255), result, .35f); result.a = .72f; }
            return result * color;
        }
        private void Disc(VertexHelper mesh, Vector2 center, float radius, Color tint)
        {
            const int segments = 48; var start = mesh.currentVertCount;
            Add(mesh, center, tint * color);
            for (var i = 0; i <= segments; i++)
            {
                var angle = i * Mathf.PI * 2 / segments;
                Add(mesh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint * color);
                if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
        private static void Add(VertexHelper mesh, Vector2 point, Color tint)
        {
            var vertex = UIVertex.simpleVert; vertex.position = point; vertex.color = tint; mesh.AddVert(vertex);
        }
    }
}
