using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Cuts one right-side corner of a UI Graphic to make a trapezoid-like shape.
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    public sealed class RightCornerCutEffect : BaseMeshEffect
    {
        public enum CutCorner
        {
            TopRight = 0,
            BottomRight = 1,
        }

        [SerializeField] private CutCorner corner = CutCorner.TopRight;
        [SerializeField] [Range(0f, 0.45f)] private float cutRatio = 0.10f;

        private readonly List<UIVertex> _vertices = new List<UIVertex>(8);

        public void Configure(CutCorner cutCorner, float ratio)
        {
            corner = cutCorner;
            cutRatio = Mathf.Clamp(ratio, 0f, 0.45f);
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount < 4)
                return;

            _vertices.Clear();
            vh.GetUIVertexStream(_vertices);
            if (_vertices.Count < 4)
                return;

            var minX = float.MaxValue;
            var maxX = float.MinValue;
            var minY = float.MaxValue;
            var maxY = float.MinValue;

            for (var i = 0; i < _vertices.Count; i++)
            {
                var p = _vertices[i].position;
                if (p.x < minX) minX = p.x;
                if (p.x > maxX) maxX = p.x;
                if (p.y < minY) minY = p.y;
                if (p.y > maxY) maxY = p.y;
            }

            var width = maxX - minX;
            if (width <= 0.0001f)
                return;

            var cutAmount = width * Mathf.Clamp(cutRatio, 0f, 0.45f);
            const float epsilon = 0.01f;

            for (var i = 0; i < _vertices.Count; i++)
            {
                var vertex = _vertices[i];
                var p = vertex.position;
                var isRight = Mathf.Abs(p.x - maxX) <= epsilon;
                if (!isRight) continue;

                var isTop = Mathf.Abs(p.y - maxY) <= epsilon;
                var isBottom = Mathf.Abs(p.y - minY) <= epsilon;
                if ((corner == CutCorner.TopRight && isTop) ||
                    (corner == CutCorner.BottomRight && isBottom))
                {
                    p.x -= cutAmount;
                    vertex.position = p;
                    _vertices[i] = vertex;
                }
            }

            vh.Clear();
            vh.AddUIVertexTriangleStream(_vertices);
        }
    }
}
