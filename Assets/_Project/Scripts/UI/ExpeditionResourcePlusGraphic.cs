using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Small bevelled gold resource control with a solid ink plus; independent of font fallback and panel slicing.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionResourcePlusGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var r = rectTransform.rect; var edge = Mathf.Min(r.width, r.height);
            Polygon(mesh, r, edge * .17f, new Color32(105, 65, 24, 255), new Color32(226, 166, 70, 255));
            var inner = new Rect(r.xMin + edge * .06f, r.yMin + edge * .06f, r.width - edge * .12f, r.height - edge * .12f);
            Polygon(mesh, inner, edge * .14f, new Color32(204, 137, 47, 255), new Color32(255, 204, 112, 255));
            var center = r.center; var half = edge * .285f; var stroke = edge * .105f;
            var ink = new Color32(53, 39, 22, 255);
            Quad(mesh, new Rect(center.x - half, center.y - stroke * .5f, half * 2, stroke), ink);
            Quad(mesh, new Rect(center.x - stroke * .5f, center.y - half, stroke, half * 2), ink);
        }

        private static void Polygon(VertexHelper mesh, Rect r, float cut, Color32 bottom, Color32 top)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(r.center, Color32.Lerp(bottom, top, .5f), Vector2.zero);
            var points = new[] { new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax - cut, r.yMin),
                new Vector2(r.xMax, r.yMin + cut), new Vector2(r.xMax, r.yMax - cut), new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMin + cut, r.yMax), new Vector2(r.xMin, r.yMax - cut), new Vector2(r.xMin, r.yMin + cut) };
            foreach (var p in points) mesh.AddVert(p, Color32.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, p.y)), Vector2.zero);
            for (var i = 0; i < 8; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % 8);
        }

        private static void Quad(VertexHelper mesh, Rect r, Color32 color)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(new Vector2(r.xMin, r.yMin), color, Vector2.zero); mesh.AddVert(new Vector2(r.xMin, r.yMax), color, Vector2.zero);
            mesh.AddVert(new Vector2(r.xMax, r.yMax), color, Vector2.zero); mesh.AddVert(new Vector2(r.xMax, r.yMin), color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
    }
}
