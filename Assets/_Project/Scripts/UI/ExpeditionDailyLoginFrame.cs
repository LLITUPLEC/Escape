using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Compact reward plates keep their bevels intact at any card aspect ratio.</summary>
    [RequireComponent(typeof(CanvasRenderer), typeof(RectTransform))]
    public sealed class ExpeditionDailyLoginFrame : MaskableGraphic
    {
        public bool Highlighted { get; private set; }
        public bool Claimed { get; private set; }
        public bool Checkmark { get; private set; }

        public void Configure(bool highlighted, bool claimed, bool checkmark = false)
        {
            if (Highlighted == highlighted && Claimed == claimed && Checkmark == checkmark) return;
            Highlighted = highlighted; Claimed = claimed; Checkmark = checkmark; SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = GetPixelAdjustedRect();
            if (rect.width < 2 || rect.height < 2) return;
            if (Checkmark) { DrawCheck(mesh, rect); return; }
            var corner = Mathf.Clamp(Mathf.Min(rect.width, rect.height) * .09f, 7, 14);
            var accent = Claimed ? new Color32(119, 190, 143, 255) : Highlighted ? ExpeditionTheme.Amber : new Color32(79, 125, 132, 255);
            Polygon(mesh, Outline(rect, corner), new Color32(3, 11, 15, 255));
            Ring(mesh, Outline(Inset(rect, 1), corner - 1), Outline(Inset(rect, 4), corner - 3),
                new Color32(123, 149, 148, 255), new Color32(31, 60, 69, 255));
            Polygon(mesh, Outline(Inset(rect, 4), corner - 3), new Color32(7, 23, 28, 255));
            Ring(mesh, Outline(Inset(rect, 6), corner - 4), Outline(Inset(rect, 7.2f), corner - 5),
                accent, Color.Lerp(accent, new Color32(11, 32, 37, 255), .62f));
            var top = new Rect(rect.xMin + corner + 5, rect.yMax - 4, Mathf.Max(0, rect.width - corner * 2 - 10), 1);
            Quad(mesh, top, Color.Lerp(accent, ExpeditionTheme.Ivory, .48f));
            var bolt = Mathf.Clamp(Mathf.Min(rect.width, rect.height) * .022f, 2, 3.5f);
            foreach (var point in new[] { new Vector2(rect.xMin + corner, rect.yMax - 5), new Vector2(rect.xMax - corner, rect.yMax - 5),
                         new Vector2(rect.xMin + corner, rect.yMin + 5), new Vector2(rect.xMax - corner, rect.yMin + 5) })
            {
                Disc(mesh, point, bolt, new Color32(9, 25, 31, 255));
                Disc(mesh, point + new Vector2(-.4f, .4f), bolt * .55f, new Color32(144, 166, 159, 255));
            }
        }

        private void DrawCheck(VertexHelper mesh, Rect rect)
        {
            var radius = Mathf.Min(rect.width, rect.height) * .49f; var center = rect.center;
            Disc(mesh, center, radius, new Color32(8, 36, 30, 255));
            Disc(mesh, center, radius * .86f, new Color32(31, 87, 62, 255));
            Disc(mesh, center, radius * .70f, new Color32(13, 49, 37, 255));
            var tint = new Color32(164, 229, 172, 255); var thickness = radius * .19f;
            Segment(mesh, center + new Vector2(-.43f, .01f) * radius, center + new Vector2(-.10f, -.29f) * radius, thickness, tint);
            Segment(mesh, center + new Vector2(-.10f, -.29f) * radius, center + new Vector2(.46f, .34f) * radius, thickness, tint);
        }
        private static Rect Inset(Rect rect, float value) => new Rect(rect.x + value, rect.y + value, Mathf.Max(0, rect.width - value * 2), Mathf.Max(0, rect.height - value * 2));
        private static Vector2[] Outline(Rect r, float c)
        {
            c = Mathf.Clamp(c, 1, Mathf.Min(r.width, r.height) * .25f);
            return new[] { new Vector2(r.xMin + c, r.yMin), new Vector2(r.xMax - c, r.yMin), new Vector2(r.xMax, r.yMin + c), new Vector2(r.xMax, r.yMax - c),
                new Vector2(r.xMax - c, r.yMax), new Vector2(r.xMin + c, r.yMax), new Vector2(r.xMin, r.yMax - c), new Vector2(r.xMin, r.yMin + c) };
        }
        private void Polygon(VertexHelper mesh, Vector2[] points, Color tint)
        {
            var center = Vector2.zero; foreach (var point in points) center += point; center /= points.Length;
            var start = mesh.currentVertCount; Add(mesh, center, tint);
            foreach (var point in points) Add(mesh, point, tint);
            for (var i = 0; i < points.Length; i++) mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % points.Length);
        }
        private void Ring(VertexHelper mesh, Vector2[] outer, Vector2[] inner, Color top, Color bottom)
        {
            var low = outer[0].y; var high = outer[4].y;
            for (var i = 0; i < outer.Length; i++)
            {
                var j = (i + 1) % outer.Length; var start = mesh.currentVertCount;
                Add(mesh, outer[i], Color.Lerp(bottom, top, Mathf.InverseLerp(low, high, outer[i].y)));
                Add(mesh, outer[j], Color.Lerp(bottom, top, Mathf.InverseLerp(low, high, outer[j].y)));
                Add(mesh, inner[j], Color.Lerp(bottom, top, Mathf.InverseLerp(low, high, inner[j].y)));
                Add(mesh, inner[i], Color.Lerp(bottom, top, Mathf.InverseLerp(low, high, inner[i].y)));
                mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
            }
        }
        private void Disc(VertexHelper mesh, Vector2 center, float radius, Color tint)
        {
            const int count = 24; var start = mesh.currentVertCount; Add(mesh, center, tint);
            for (var i = 0; i <= count; i++)
            {
                var angle = i * Mathf.PI * 2 / count; Add(mesh, center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius, tint);
                if (i > 0) mesh.AddTriangle(start, start + i, start + i + 1);
            }
        }
        private void Segment(VertexHelper mesh, Vector2 from, Vector2 to, float thickness, Color tint)
        {
            var direction = (to - from).normalized; var normal = new Vector2(-direction.y, direction.x) * thickness * .5f; var start = mesh.currentVertCount;
            Add(mesh, from - normal, tint); Add(mesh, from + normal, tint); Add(mesh, to + normal, tint); Add(mesh, to - normal, tint);
            mesh.AddTriangle(start, start + 1, start + 2); mesh.AddTriangle(start, start + 2, start + 3);
        }
        private void Quad(VertexHelper mesh, Rect rect, Color tint) => Polygon(mesh, new[] { new Vector2(rect.xMin, rect.yMin), new Vector2(rect.xMax, rect.yMin), new Vector2(rect.xMax, rect.yMax), new Vector2(rect.xMin, rect.yMax) }, tint);
        private void Add(VertexHelper mesh, Vector2 point, Color tint)
        { var vertex = UIVertex.simpleVert; vertex.position = point; vertex.color = tint * color; mesh.AddVert(vertex); }
    }
}
