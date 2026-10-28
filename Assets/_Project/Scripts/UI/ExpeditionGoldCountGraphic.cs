using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Compact gold count plate, with the same bevel palette as the approved resource controls and no baked symbol.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionGoldCountGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect; var edge = Mathf.Min(rect.width, rect.height);
            Add(mesh, rect, edge * .17f, new Color32(105, 65, 24, 255), new Color32(226, 166, 70, 255));
            Add(mesh, new Rect(rect.xMin + edge * .06f, rect.yMin + edge * .06f, rect.width - edge * .12f, rect.height - edge * .12f),
                edge * .14f, new Color32(204, 137, 47, 255), new Color32(255, 204, 112, 255));
        }
        private static void Add(VertexHelper mesh, Rect r, float cut, Color32 bottom, Color32 top)
        {
            var start = mesh.currentVertCount; mesh.AddVert(r.center, Color32.Lerp(bottom, top, .5f), Vector2.zero);
            var points = new[] { new Vector2(r.xMin + cut, r.yMin), new Vector2(r.xMax - cut, r.yMin),
                new Vector2(r.xMax, r.yMin + cut), new Vector2(r.xMax, r.yMax - cut), new Vector2(r.xMax - cut, r.yMax),
                new Vector2(r.xMin + cut, r.yMax), new Vector2(r.xMin, r.yMax - cut), new Vector2(r.xMin, r.yMin + cut) };
            foreach (var p in points) mesh.AddVert(p, Color32.Lerp(bottom, top, Mathf.InverseLerp(r.yMin, r.yMax, p.y)), Vector2.zero);
            for (var i = 0; i < 8; i++) mesh.AddTriangle(start, start + i + 1, start + 1 + (i + 1) % 8);
        }
    }
}
