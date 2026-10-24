using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>A single low vertex UI mesh, refreshed at 10 Hz; no particles, lights or gameplay work.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionLocalAtmosphere : MaskableGraphic
    {
        public bool forge;
        private float _nextTick;

        public static void Install(Transform parent, bool isForge)
        {
            var rect = ExpeditionUi.Rect(parent, "LocalAtmosphere", 0, 0, 1, 1);
            var graphic = rect.gameObject.AddComponent<ExpeditionLocalAtmosphere>();
            graphic.forge = isForge;
            graphic.raycastTarget = false;
        }

        private void Update()
        {
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + .1f;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var r = rectTransform.rect;
            var t = Time.unscaledTime;
            for (var i = 0; i < 19; i++)
            {
                var x = Mathf.Repeat(i * .618034f + Mathf.Sin(t * .13f + i) * .013f, 1f);
                var y = Mathf.Repeat(i * .381966f + t * (forge ? .045f : .012f), 1f);
                if (forge) { x = .12f + x * .49f; y = .3f + y * .57f; }
                var c = forge ? new Color(1f, .52f, .12f, .25f + .22f * Mathf.Sin(t * 2f + i))
                    : new Color(.62f, .83f, .79f, .12f + .09f * Mathf.Sin(t + i));
                Glow(vh, new Vector2(r.xMin + x * r.width, r.yMin + y * r.height), forge ? 4f : 2.6f, c);
            }
            if (forge)
                Glow(vh, new Vector2(r.xMin + .25f * r.width, r.yMin + .48f * r.height),
                    r.width * .18f, new Color(1f, .35f, .025f, .05f + .015f * Mathf.Sin(t * 4.1f)));
        }

        private static void Glow(VertexHelper vh, Vector2 p, float radius, Color color)
        {
            var start = vh.currentVertCount;
            vh.AddVert(p, color, Vector2.zero);
            var clear = new Color(color.r, color.g, color.b, 0f);
            vh.AddVert(p + Vector2.left * radius, clear, Vector2.zero);
            vh.AddVert(p + Vector2.up * radius, clear, Vector2.zero);
            vh.AddVert(p + Vector2.right * radius, clear, Vector2.zero);
            vh.AddVert(p + Vector2.down * radius, clear, Vector2.zero);
            for (var i = 0; i < 4; i++) vh.AddTriangle(start, start + i + 1, start + (i + 1) % 4 + 1);
        }
    }
}
