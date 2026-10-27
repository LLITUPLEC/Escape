using UnityEngine;
using UnityEngine.UI;

namespace Project.UI
{
    /// <summary>Small native UI pictograms. No extra textures, materials or sprite atlases.</summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ExpeditionNavigationIcon : MaskableGraphic
    {
        public int Kind;
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            switch (Kind)
            {
                case 5: Trophy(vh); break;
                case 6: Gear(vh); break;
                case 7:
                {
                    // Brushed-metal shackle and beveled body, with a dark keyhole.
                    // All coordinates stay inside the existing icon rect.
                    var center = new Vector2(.5f, .64f);
                    for (var i = 0; i < 12; i++)
                    {
                        var a0 = i * Mathf.PI / 12; var a1 = (i + 1) * Mathf.PI / 12;
                        var d0 = new Vector2(Mathf.Cos(a0), Mathf.Sin(a0));
                        var d1 = new Vector2(Mathf.Cos(a1), Mathf.Sin(a1));
                        QuadShade(vh, center + d0 * .13f, center + d0 * .23f,
                            center + d1 * .23f, center + d1 * .13f, color * (i < 6 ? .72f : 1.1f));
                    }
                    Box(vh, .27f, .49f, .37f, .65f); Box(vh, .63f, .49f, .73f, .65f);
                    QuadShade(vh, new Vector2(.17f,.16f), new Vector2(.17f,.54f), new Vector2(.23f,.54f), new Vector2(.23f,.10f), color * .74f);
                    QuadShade(vh, new Vector2(.77f,.10f), new Vector2(.77f,.54f), new Vector2(.83f,.54f), new Vector2(.83f,.16f), color * .47f);
                    QuadShade(vh, new Vector2(.17f,.16f), new Vector2(.23f,.10f), new Vector2(.77f,.10f), new Vector2(.83f,.16f), color * .57f);
                    QuadShade(vh, new Vector2(.17f,.54f), new Vector2(.23f,.60f), new Vector2(.77f,.60f), new Vector2(.83f,.54f), color * 1.15f);
                    QuadShade(vh, new Vector2(.23f,.16f), new Vector2(.23f,.54f), new Vector2(.77f,.54f), new Vector2(.77f,.16f), color * .93f);
                    var hole = new Vector2(.5f, .38f);
                    for (var i = 0; i < 10; i++)
                    {
                        var a0 = i * Mathf.PI * 2 / 10; var a1 = (i + 1) * Mathf.PI * 2 / 10;
                        QuadShade(vh, hole, hole + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * .056f,
                            hole + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * .056f, hole, color * .18f);
                    }
                    QuadShade(vh, new Vector2(.469f,.24f), new Vector2(.478f,.37f), new Vector2(.522f,.37f), new Vector2(.531f,.24f), color * .18f);
                    break;
                }
                case 0: Line(vh, .08f, .48f, .5f, .89f, .14f); Line(vh, .5f, .89f, .92f, .48f, .14f); Box(vh, .2f, .1f, .44f, .56f); Box(vh, .63f, .1f, .8f, .56f); Box(vh, .2f, .1f, .8f, .22f); break;
                case 1: Line(vh, .2f, .13f, .76f, .83f, .14f); Line(vh, .28f, .8f, .84f, .59f, .18f); break;
                case 2: Box(vh, .13f, .48f, .9f, .68f); Box(vh, .3f, .22f, .65f, .49f); Box(vh, .17f, .1f, .79f, .25f); Line(vh, .07f, .75f, .37f, .58f, .15f); break;
                case 3: Line(vh, .16f, .13f, .83f, .88f, .15f); Line(vh, .16f, .88f, .83f, .13f, .15f); Line(vh, .1f, .32f, .38f, .07f, .1f); Line(vh, .6f, .07f, .91f, .33f, .1f); break;
                default: Line(vh, .15f, .3f, .35f, .85f, .16f); Line(vh, .35f, .85f, .66f, .85f, .16f); Line(vh, .66f, .85f, .87f, .3f, .16f); Line(vh, .16f, .27f, .49f, .07f, .13f); Line(vh, .49f, .07f, .85f, .27f, .13f); Box(vh, .35f, .35f, .66f, .59f); break;
            }
        }
        private void Trophy(VertexHelper vh)
        {
            Line(vh, .23f, .72f, .1f, .72f, .09f); Line(vh, .1f, .72f, .13f, .49f, .08f);
            Line(vh, .13f, .49f, .32f, .42f, .08f);
            Line(vh, .77f, .72f, .9f, .72f, .09f); Line(vh, .9f, .72f, .87f, .49f, .08f);
            Line(vh, .87f, .49f, .68f, .42f, .08f);
            Box(vh, .44f, .18f, .56f, .42f); Box(vh, .28f, .09f, .72f, .19f);
            QuadShade(vh, new Vector2(.22f,.82f), new Vector2(.33f,.40f), new Vector2(.5f,.34f), new Vector2(.5f,.82f), color * 1.1f);
            QuadShade(vh, new Vector2(.5f,.82f), new Vector2(.5f,.34f), new Vector2(.67f,.40f), new Vector2(.78f,.82f), color * .65f);
            Box(vh, .21f, .79f, .79f, .87f);
        }
        private void Gear(VertexHelper vh)
        {
            const int sides = 64;
            for (var i = 0; i < sides; i++)
            {
                var angle0 = i * Mathf.PI * 2 / sides;
                var angle1 = (i + 1) * Mathf.PI * 2 / sides;
                var outer0 = i % 8 < 4 ? .43f : .34f;
                var outer1 = (i + 1) % 8 < 4 ? .43f : .34f;
                var direction0 = new Vector2(Mathf.Cos(angle0), Mathf.Sin(angle0));
                var direction1 = new Vector2(Mathf.Cos(angle1), Mathf.Sin(angle1));
                var center = new Vector2(.5f,.5f);
                QuadShade(vh, center + direction0 * .16f, center + direction0 * outer0, center + direction1 * outer1, center + direction1 * .16f, color * (i < sides / 2 ? 1f : .68f));
            }
        }
        private void Box(VertexHelper vh, float x0, float y0, float x1, float y1) => Quad(vh, new Vector2(x0, y0), new Vector2(x0, y1), new Vector2(x1, y1), new Vector2(x1, y0));
        private void Line(VertexHelper vh, float x0, float y0, float x1, float y1, float width)
        {
            var a = new Vector2(x0, y0); var b = new Vector2(x1, y1);
            var normal = new Vector2(-(b - a).y, (b - a).x).normalized * width * .5f;
            Quad(vh, a - normal, a + normal, b + normal, b - normal);
        }
        private void Quad(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d) => QuadShade(vh, a, b, c, d, color);
        private void QuadShade(VertexHelper vh, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            var r = rectTransform.rect;
            var start = vh.currentVertCount;
            tint.a = color.a;
            foreach (var point in new[] { a, b, c, d }) vh.AddVert(new Vector3(r.xMin + r.width * point.x, r.yMin + r.height * point.y), tint, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
