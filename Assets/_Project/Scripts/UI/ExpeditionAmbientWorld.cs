using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>A handful of slow forge embers in a separate world canvas. No particle systems or dynamic lights.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionAmbientWorld : MaskableGraphic
    {
        private float _nextFrame;
        private void Update()
        {
            if (Time.unscaledTime < _nextFrame) return;
            _nextFrame = Time.unscaledTime + .1f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var rect = rectTransform.rect;
            for (var i = 0; i < 7; i++)
            {
                var phase = Mathf.Repeat(Time.unscaledTime * (.1f + i * .007f) + i * .173f, 1);
                var alpha = Mathf.Sin(phase * Mathf.PI) * .32f;
                var x = rect.xMin + rect.width * (.62f + (i % 4) * .026f + Mathf.Sin(phase * 4 + i) * .008f);
                var y = rect.yMin + rect.height * (.688f + phase * .105f);
                var size = rect.width * (.0015f + (i % 3) * .0006f);
                var index = vh.currentVertCount;
                var tint = new Color(1, .7f, .27f, alpha);
                vh.AddVert(new Vector3(x, y - size), tint, Vector2.zero);
                vh.AddVert(new Vector3(x - size, y), tint, Vector2.zero);
                vh.AddVert(new Vector3(x, y + size), tint, Vector2.zero);
                vh.AddVert(new Vector3(x + size, y), tint, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }
}
