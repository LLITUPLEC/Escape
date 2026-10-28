using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Texture-free circular portrait mask or thin metal rim.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionCircleGraphic : MaskableGraphic
    {
        public bool Ring;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear(); var rect = rectTransform.rect; var center = rect.center;
            var radius = Mathf.Min(rect.width, rect.height) * .5f;
            const int sides = 64;
            if (!Ring) mesh.AddVert(center, color, Vector2.zero);
            for (var i = 0; i <= sides; i++)
            {
                var angle = i * Mathf.PI * 2 / sides; var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                if (Ring)
                {
                    mesh.AddVert(center + direction * radius, color, Vector2.zero);
                    mesh.AddVert(center + direction * (radius - Mathf.Max(2, radius * .035f)), color, Vector2.zero);
                    if (i > 0) { var n = i * 2; mesh.AddTriangle(n - 2, n, n + 1); mesh.AddTriangle(n - 2, n + 1, n - 1); }
                }
                else
                {
                    mesh.AddVert(center + direction * radius, color, Vector2.zero);
                    if (i > 0) mesh.AddTriangle(0, i, i + 1);
                }
            }
        }
    }
}
