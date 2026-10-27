using UnityEngine;
using UnityEngine.UI;

namespace Project.Tutorial
{
    /// <summary>A lightweight native arrow. Never intercepts a touch on the highlighted control.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class TutorialFocusArrow : MaskableGraphic
    {
        public RectTransform From, To;
        private void Update() { SetVerticesDirty(); }
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear(); if (To == null) return;
            var end = (Vector2)rectTransform.InverseTransformPoint(To.TransformPoint(To.rect.center));
            var tailSide = end.x > rectTransform.rect.center.x ? -1 : 1;
            var start = From != null ? (Vector2)rectTransform.InverseTransformPoint(From.TransformPoint(From.rect.center)) : end + new Vector2(158 * tailSide, 112);
            var direction = (end - start).normalized;
            if (From != null) start -= direction * 62;
            end -= direction * 21;
            var side = new Vector2(-direction.y, direction.x);
            // Opaque green and a dark silhouette remain legible on pale gems,
            // dark panels and a busy frame; motion never fades the guidance.
            Draw(vh, start - direction * 5, end + direction * 5, direction, side, 13, 51, 31, new Color32(2, 22, 9, 255));
            Draw(vh, start, end, direction, side, 8, 43, 24, new Color32(47, 255, 36, 255));
        }
        private static void Draw(VertexHelper vh, Vector2 start, Vector2 end, Vector2 direction, Vector2 side, float halfWidth, float headLength, float headWidth, Color tint)
        {
            Quad(vh, start + side * halfWidth, start - side * halfWidth, end - direction * (headLength - 5) - side * halfWidth, end - direction * (headLength - 5) + side * halfWidth, tint);
            var index = vh.currentVertCount;
            vh.AddVert(end, tint, Vector2.zero); vh.AddVert(end - direction * headLength + side * headWidth, tint, Vector2.zero); vh.AddVert(end - direction * headLength - side * headWidth, tint, Vector2.zero);
            vh.AddTriangle(index, index + 1, index + 2);
        }
        private static void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color color)
        {
            var i = vh.currentVertCount; vh.AddVert(a, color, Vector2.zero); vh.AddVert(b, color, Vector2.zero); vh.AddVert(c, color, Vector2.zero); vh.AddVert(d, color, Vector2.zero);
            vh.AddTriangle(i, i + 1, i + 2); vh.AddTriangle(i, i + 2, i + 3);
        }
    }
}
