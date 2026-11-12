using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>A stationary seismograph with an animated pulse crossing its instrument glass.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionAuraSignal : MaskableGraphic
    {
        private static readonly Color32 GridInk = new(30, 72, 69, 255);
        private static readonly Color32 DarkGlass = new(9, 28, 28, 255);
        private static readonly Color32 TraceInk = new(100, 229, 189, 255);
        private float _nextTick;
        private float _phase = .57f;

        private void Update()
        {
            var now = Time.unscaledTime;
            if (now < _nextTick) return;
            _nextTick = now + .05f;
            _phase = Mathf.Repeat(now * .49f, 1f);
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width < 4 || rect.height < 4) return;
            Quad(mesh, rect, new Color32(102, 147, 130, 255), new Color32(48, 67, 59, 255));
            var inset = Mathf.Min(3f, Mathf.Min(rect.width, rect.height) * .1f);
            rect = Rect.MinMaxRect(rect.xMin + inset, rect.yMin + inset, rect.xMax - inset, rect.yMax - inset);
            Quad(mesh, rect, DarkGlass, new Color32(14, 45, 40, 255));

            for (var i = 1; i < 5; i++)
                Segment(mesh, At(rect, i * .2f, .08f), At(rect, i * .2f, .92f), 1f, GridInk);
            for (var i = 1; i < 4; i++)
                Segment(mesh, At(rect, .035f, i * .25f), At(rect, .965f, i * .25f), 1f, GridInk);

            var x = Mathf.Lerp(.08f, .92f, _phase);
            Quad(mesh, Rect.MinMaxRect(rect.xMin + x * rect.width - 1, rect.yMin, rect.xMin + x * rect.width + 1, rect.yMax),
                new Color(TraceInk.r / 255f, TraceInk.g / 255f, TraceInk.b / 255f, .02f),
                new Color(TraceInk.r / 255f, TraceInk.g / 255f, TraceInk.b / 255f, .38f));
            const int samples = 29;
            var last = At(rect, .05f, Sample(.05f, x));
            for (var i = 1; i <= samples; i++)
            {
                var position = Mathf.Lerp(.05f, .95f, i / (float)samples);
                var point = At(rect, position, Sample(position, x));
                var brightness = Mathf.Clamp01(1f - Mathf.Abs(position - x) * 2.4f);
                var tint = Color.Lerp(GridInk, TraceInk, .48f + brightness * .52f);
                Segment(mesh, last, point, 2.25f, tint);
                last = point;
            }
        }

        private static float Sample(float x, float pulse)
        {
            var distance = x - pulse;
            var envelope = 1f - Mathf.Clamp01(Mathf.Abs(distance) * 5.5f);
            return .5f + Mathf.Sin(x * 36f) * .047f + Mathf.Sin(distance * 39f) * .36f * envelope;
        }

        private void Segment(VertexHelper mesh, Vector2 a, Vector2 b, float width, Color tint)
        {
            var direction = (b - a).normalized;
            var offset = new Vector2(-direction.y, direction.x) * (width * .5f);
            AddQuad(mesh, a - offset, a + offset, b + offset, b - offset, tint, tint);
        }

        private void Quad(VertexHelper mesh, Rect r, Color top, Color bottom) =>
            AddQuad(mesh, new(r.xMin, r.yMin), new(r.xMin, r.yMax), new(r.xMax, r.yMax), new(r.xMax, r.yMin), top, bottom);

        private void AddQuad(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color top, Color bottom)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(a, bottom * color, Vector2.zero);
            mesh.AddVert(b, top * color, Vector2.zero);
            mesh.AddVert(c, top * color, Vector2.zero);
            mesh.AddVert(d, bottom * color, Vector2.zero);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }

        private static Vector2 At(Rect rect, float x, float y) =>
            new(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
    }
}
