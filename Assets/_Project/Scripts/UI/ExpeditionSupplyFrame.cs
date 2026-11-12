using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    [RequireComponent(typeof(CanvasRenderer), typeof(RectTransform))]
    public sealed class ExpeditionSupplyFrame : MaskableGraphic
    {
        public enum Silhouette { Camp, Trophy, Claim, Compartment }

        private static readonly Vector2[] MapPin =
        {
            new(.50f, 0), new(.66f, .12f), new(.87f, .12f), new(1, .31f), new(1, .72f),
            new(.86f, .91f), new(.65f, .91f), new(.50f, 1), new(.35f, .91f), new(.14f, .91f),
            new(0, .72f), new(0, .31f), new(.13f, .12f), new(.34f, .12f),
        };
        private static readonly Vector2[] TrophySeal =
        {
            new(.50f, 0), new(.75f, .10f), new(.93f, .24f), new(1, .45f), new(.97f, .80f),
            new(.84f, .96f), new(.66f, .91f), new(.50f, 1), new(.34f, .91f), new(.16f, .96f),
            new(.03f, .80f), new(0, .45f), new(.07f, .24f), new(.25f, .10f),
        };
        private static readonly Vector2[] ClaimClasp =
        {
            new(.09f, 0), new(.91f, 0), new(.96f, .22f), new(1, .50f), new(.96f, .78f),
            new(.91f, 1), new(.09f, 1), new(.04f, .78f), new(0, .50f), new(.04f, .22f),
        };
        private static readonly Vector2[] ChestWell =
        {
            new(.12f, 0), new(.88f, 0), new(1, .26f), new(1, .74f),
            new(.88f, 1), new(.12f, 1), new(0, .74f), new(0, .26f),
        };
        private static readonly Color EdgeDark = new Color32(47, 32, 27, 255);
        private static readonly Color Shadow = new Color32(16, 12, 10, 255);
        private Color _metal;
        private bool _highlighted;
        private bool _collected;
        public Silhouette Shape { get; private set; }

        internal static ExpeditionSupplyFrame Install(Button button, Silhouette shape)
        {
            var image = button.image;
            image.enabled = false;
            image.raycastTarget = false;
            var frame = Build(button.transform, "SupplyFrame", shape, ExpeditionTheme.Amber, 0, 0, 1, 1);
            frame.transform.SetAsFirstSibling();
            frame.raycastTarget = true;
            button.targetGraphic = frame;
            return frame;
        }

        internal static ExpeditionSupplyFrame Build(Transform parent, string name, Silhouette shape, Color metal,
            float x0, float y0, float x1, float y1)
        {
            var rect = ExpeditionUi.Rect(parent, name, x0, y0, x1, y1);
            var frame = rect.gameObject.AddComponent<ExpeditionSupplyFrame>();
            frame.raycastTarget = false;
            frame.Shape = shape;
            frame._metal = metal;
            return frame;
        }

        internal void Present(bool highlighted, bool collected = false)
        {
            if (_highlighted == highlighted && _collected == collected) return;
            _highlighted = highlighted;
            _collected = collected;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            var rect = GetPixelAdjustedRect();
            if (rect.width < 4 || rect.height < 4) return;

            var outline = Shape switch
            {
                Silhouette.Camp => MapPin,
                Silhouette.Trophy => TrophySeal,
                Silhouette.Claim => ClaimClasp,
                _ => ChestWell,
            };
            var scale = Mathf.Clamp(Mathf.Min(rect.width, rect.height) * .011f, .5f, 1.8f);
            var metal = _collected ? (Color)new Color32(120, 148, 111, 255) : _metal;
            var edge = Color.Lerp(metal, EdgeDark, _highlighted ? .03f : .34f);
            var rimLight = Color.Lerp(edge, ExpeditionTheme.Ivory, _highlighted ? .42f : .20f);
            var faceTop = Color.Lerp(new Color32(27, 32, 28, 255), edge, _highlighted ? .46f : .20f);
            var faceBottom = Color.Lerp(Shadow, edge, _highlighted ? .31f : .10f);
            DrawFace(mesh, outline, rect, Shadow, Shadow);
            DrawRing(mesh, outline, Inset(rect, 1.1f * scale), Inset(rect, 4.4f * scale), rimLight, EdgeDark);
            DrawFace(mesh, outline, Inset(rect, 4.4f * scale), faceBottom, faceTop);
            DrawRing(mesh, outline, Inset(rect, 5.5f * scale), Inset(rect, 6.6f * scale),
                Color.Lerp(faceTop, rimLight, .62f), Color.Lerp(faceBottom, edge, .42f));

            var rivetRadius = (Shape == Silhouette.Claim ? 4f : 3f) * scale;
            var rivetX = Shape == Silhouette.Claim ? .05f : .115f;
            DrawRivet(mesh, Point(rect, new Vector2(rivetX, .53f)), rivetRadius, rimLight);
            DrawRivet(mesh, Point(rect, new Vector2(1 - rivetX, .53f)), rivetRadius, rimLight);
            if (Shape != Silhouette.Claim)
                DrawRivet(mesh, Point(rect, new Vector2(.5f, .115f)), rivetRadius * .65f, edge);
        }

        private void DrawFace(VertexHelper mesh, Vector2[] outline, Rect rect, Color bottom, Color top)
        {
            var start = mesh.currentVertCount;
            Add(mesh, rect.center, Color.Lerp(bottom, top, .5f));
            foreach (var vertex in outline)
                Add(mesh, Point(rect, vertex), Color.Lerp(bottom, top, vertex.y));
            for (var i = 0; i < outline.Length; i++)
                mesh.AddTriangle(start, start + i + 1, start + (i + 1) % outline.Length + 1);
        }

        private void DrawRing(VertexHelper mesh, Vector2[] outline, Rect outer, Rect inner, Color top, Color bottom)
        {
            var start = mesh.currentVertCount;
            foreach (var vertex in outline)
            {
                var tint = Color.Lerp(bottom, top, vertex.y);
                Add(mesh, Point(outer, vertex), tint);
                Add(mesh, Point(inner, vertex), tint);
            }
            for (var i = 0; i < outline.Length; i++)
            {
                var left = start + i * 2;
                var right = start + (i + 1) % outline.Length * 2;
                mesh.AddTriangle(left, right, right + 1);
                mesh.AddTriangle(left, right + 1, left + 1);
            }
        }

        private void DrawRivet(VertexHelper mesh, Vector2 center, float radius, Color tint)
        {
            var start = mesh.currentVertCount;
            Add(mesh, center + Vector2.left * radius, EdgeDark);
            Add(mesh, center - Vector2.up * radius, Shadow);
            Add(mesh, center + Vector2.right * radius, EdgeDark);
            Add(mesh, center + Vector2.up * radius, tint);
            mesh.AddTriangle(start, start + 1, start + 2);
            mesh.AddTriangle(start, start + 2, start + 3);
        }

        private void Add(VertexHelper mesh, Vector2 point, Color tint)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = point;
            vertex.color = tint * color;
            mesh.AddVert(vertex);
        }

        private static Rect Inset(Rect rect, float amount) =>
            new(rect.x + amount, rect.y + amount, Mathf.Max(0, rect.width - amount * 2), Mathf.Max(0, rect.height - amount * 2));

        private static Vector2 Point(Rect rect, Vector2 anchor) => rect.min + Vector2.Scale(rect.size, anchor);
    }
}
