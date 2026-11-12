using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Fitted stone, mine iron and tournament shield silhouettes for the arena entrances.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionArenaGateFrame : MaskableGraphic
    {
        public enum Shape { Arch, Shaft, Shield }

        private static readonly Vector2[] Arch =
        {
            new(.055f, .025f), new(.945f, .025f), new(.985f, .11f), new(.985f, .60f),
            new(.950f, .735f), new(.862f, .850f), new(.752f, .933f), new(.626f, .982f),
            new(.50f, .998f), new(.374f, .982f), new(.248f, .933f), new(.138f, .850f),
            new(.050f, .735f), new(.015f, .60f), new(.015f, .11f),
        };

        private static readonly Vector2[] Shaft =
        {
            new(.08f, .025f), new(.92f, .025f), new(.987f, .10f), new(.987f, .68f),
            new(.74f, .987f), new(.26f, .987f), new(.013f, .68f), new(.013f, .10f),
        };

        private static readonly Vector2[] Shield =
        {
            new(.50f, .01f), new(.82f, .073f), new(.986f, .20f), new(.986f, .79f),
            new(.80f, .913f), new(.50f, .998f), new(.20f, .913f), new(.014f, .79f),
            new(.014f, .20f), new(.18f, .073f),
        };

        private Color _metal;
        public Shape Silhouette { get; private set; }

        public void Configure(Shape silhouette, Color metal)
        {
            Silhouette = silhouette;
            _metal = metal;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width < 4f || rect.height < 4f) return;
            var shape = Silhouette == Shape.Arch ? Arch : Silhouette == Shape.Shaft ? Shaft : Shield;
            var shadow = new Color32(12, 14, 17, 255);
            var edge = Color.Lerp(_metal, new Color32(238, 218, 184, 255), .40f);
            var patina = Color.Lerp(_metal, shadow, .68f);
            var recess = Color.Lerp(new Color32(11, 20, 24, 255), _metal, .16f);
            var face = Color.Lerp(recess, shadow, .48f);
            Fill(mesh, shape, rect, 0f, shadow, shadow);
            Fill(mesh, shape, rect, 3f, edge, patina);
            Fill(mesh, shape, rect, 10f, patina, Color.Lerp(patina, shadow, .5f));
            Fill(mesh, shape, rect, 17f, edge, patina);
            Fill(mesh, shape, rect, 19f, recess, face);

            // Fasteners sit on the frame, leaving the legend and crest unobstructed.
            var bolt = Mathf.Clamp(rect.width * .012f, 2f, 5f);
            var high = Silhouette == Shape.Arch ? .64f : Silhouette == Shape.Shaft ? .61f : .73f;
            Rivet(mesh, At(rect, .052f, high), bolt, shadow, edge);
            Rivet(mesh, At(rect, .948f, high), bolt, shadow, edge);
            Rivet(mesh, At(rect, .072f, .22f), bolt, shadow, edge);
            Rivet(mesh, At(rect, .928f, .22f), bolt, shadow, edge);
        }

        private void Fill(VertexHelper mesh, Vector2[] shape, Rect rect, float inset, Color top, Color bottom)
        {
            inset = Mathf.Min(inset, Mathf.Min(rect.width, rect.height) * .20f);
            rect = Rect.MinMaxRect(rect.xMin + inset, rect.yMin + inset, rect.xMax - inset, rect.yMax - inset);
            var start = mesh.currentVertCount;
            mesh.AddVert(rect.center, Color.Lerp(bottom, top, .5f) * color, Vector2.zero);
            for (var i = 0; i < shape.Length; i++)
            {
                var point = shape[i];
                mesh.AddVert(At(rect, point.x, point.y), Color.Lerp(bottom, top, point.y) * color, Vector2.zero);
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % shape.Length);
            }
        }

        private void Rivet(VertexHelper mesh, Vector2 center, float radius, Color rim, Color glint)
        {
            var start = mesh.currentVertCount;
            mesh.AddVert(center, glint * color, Vector2.zero);
            const int sides = 8;
            for (var i = 0; i < sides; i++)
            {
                var angle = i * 2f * Mathf.PI / sides;
                var point = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
                mesh.AddVert(point, rim * color, Vector2.zero);
                mesh.AddTriangle(start, start + 1 + i, start + 1 + (i + 1) % sides);
            }
        }

        private static Vector2 At(Rect rect, float x, float y) =>
            new(rect.xMin + x * rect.width, rect.yMin + y * rect.height);
    }
}
