using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>Clipped, non-interactive board impact. The clear and fall coroutines never wait for this graphic.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class Match3BoardImpactFx : MaskableGraphic
    {
        private const float Lifetime = .43f;
        private const int ShardsPerStone = 3;
        private static readonly Color CrossLight = new Color(.22f, .93f, 1f);
        private static readonly Color SquareLight = new Color(1f, .52f, .15f);

        private struct Shard
        {
            public Vector2 Origin;
            public Vector2 Velocity;
            public float Angle;
            public float Length;
            public float Duration;
            public Color Color;
        }

        private readonly Shard[] _shards = new Shard[9 * ShardsPerStone];
        private RectTransform _layer;
        private AbilityType _ability;
        private int _shardCount;
        private float _age;
        private float _cellSize;
        private Vector2 _center;
        private Rect _bounds;

        internal static Match3BoardImpactFx Create(Transform parent)
        {
            var layer = new GameObject("BoardAbilityEffects", typeof(RectTransform), typeof(RectMask2D), typeof(LayoutElement));
            var layerRect = (RectTransform)layer.transform;
            layerRect.SetParent(parent, false);
            layer.GetComponent<LayoutElement>().ignoreLayout = true;

            var graphic = new GameObject("Fracture", typeof(RectTransform), typeof(CanvasRenderer), typeof(Match3BoardImpactFx));
            var graphicRect = (RectTransform)graphic.transform;
            graphicRect.SetParent(layerRect, false);
            graphicRect.anchorMin = Vector2.zero;
            graphicRect.anchorMax = Vector2.one;
            graphicRect.offsetMin = graphicRect.offsetMax = Vector2.zero;
            var impact = graphic.GetComponent<Match3BoardImpactFx>();
            impact.raycastTarget = false;
            impact._layer = layerRect;
            layer.SetActive(false);
            return impact;
        }

        internal bool Play(AbilityType ability, int cx, int cy, RectTransform viewport, RectTransform[,] stones,
            IReadOnlyList<(int x, int y, PieceType type)> cleared, Transform dimmer)
        {
            Stop();
            if (!(stones[cx, cy]?.parent is RectTransform target) || !(_layer.parent is RectTransform parent))
                return false;
            var viewportRect = LocalRect(parent, viewport);
            if (viewportRect.width <= 0f || viewportRect.height <= 0f) return false;
            _layer.anchorMin = _layer.anchorMax = parent.pivot;
            _layer.anchoredPosition = viewportRect.center;
            _layer.sizeDelta = viewportRect.size;
            if (dimmer != null && dimmer.parent == parent)
                _layer.SetSiblingIndex(dimmer.GetSiblingIndex() + 1);

            var targetRect = LocalRect(rectTransform, target);
            _center = targetRect.center;
            _cellSize = Mathf.Min(targetRect.width, targetRect.height);
            if (_cellSize <= 0f) return false;
            _ability = ability;
            _age = 0f;
            var low = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var high = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            foreach (var (x, y, type) in cleared)
            {
                int dx = Mathf.Abs(x - cx), dy = Mathf.Abs(y - cy);
                bool inShape = ability == AbilityType.Cross ? dx + dy <= 2 && (dx == 0 || dy == 0) : dx <= 1 && dy <= 1;
                if (!inShape || !(stones[x, y]?.parent is RectTransform cell)) continue;
                var area = LocalRect(rectTransform, cell);
                low = Vector2.Min(low, area.min);
                high = Vector2.Max(high, area.max);
                SeedShards(area.center, Match3BoardView.ColorOf(type), x + y * Match3BoardLogic.Size);
            }
            if (_shardCount == 0) return false;
            float inset = _cellSize * .075f;
            _bounds = Rect.MinMaxRect(low.x + inset, low.y + inset, high.x - inset, high.y - inset);
            _layer.gameObject.SetActive(true);
            SetVerticesDirty();
            return true;
        }

        private void SeedShards(Vector2 origin, Color stoneColor, int seed)
        {
            var radial = (origin - _center).normalized;
            var accent = _ability == AbilityType.Cross ? CrossLight : SquareLight;
            for (int i = 0; i < ShardsPerStone; i++)
            {
                // Keep particles deterministic and independent of the gameplay random stream.
                float angle = seed * 2.399963f + i * 2.094395f;
                var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                var outward = (direction + radial * .65f).normalized;
                _shards[_shardCount++] = new Shard
                {
                    Origin = origin + direction * (_cellSize * .10f),
                    Velocity = outward * (_cellSize * (1.4f + i * .36f)) + Vector2.up * (_cellSize * .28f),
                    Angle = angle,
                    Length = _cellSize * (.065f + .022f * i),
                    Duration = .28f + .033f * i + .014f * (seed % 5),
                    Color = Color.Lerp(stoneColor, accent, i == 2 ? .84f : .42f)
                };
            }
        }

        internal void Stop()
        {
            _shardCount = 0;
            if (_layer != null && _layer.gameObject.activeSelf) _layer.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            _age += Time.deltaTime;
            if (_age >= Lifetime) Stop();
            else SetVerticesDirty();
        }

        protected override void OnDisable()
        {
            _shardCount = 0;
            base.OnDisable();
        }

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (_shardCount == 0) return;
            bool cross = _ability == AbilityType.Cross;
            var accent = cross ? CrossLight : SquareLight;
            float lineAlpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.055f, cross ? .235f : .255f, _age));
            float spread = Mathf.SmoothStep(cross ? .22f : .46f, 1f, _age / .086f);
            if (lineAlpha > 0f)
            {
                if (cross)
                {
                    DrawTrace(mesh, _center, new Vector2(_bounds.xMin, _center.y), spread, lineAlpha, accent, 0);
                    DrawTrace(mesh, _center, new Vector2(_bounds.xMax, _center.y), spread, lineAlpha, accent, 1);
                    DrawTrace(mesh, _center, new Vector2(_center.x, _bounds.yMin), spread, lineAlpha, accent, 2);
                    DrawTrace(mesh, _center, new Vector2(_center.x, _bounds.yMax), spread, lineAlpha, accent, 3);
                }
                else
                {
                    var lowerLeft = _bounds.min;
                    var upperRight = _bounds.max;
                    var upperLeft = new Vector2(lowerLeft.x, upperRight.y);
                    var lowerRight = new Vector2(upperRight.x, lowerLeft.y);
                    DrawTrace(mesh, lowerLeft, upperLeft, spread, lineAlpha, accent, 4);
                    DrawTrace(mesh, upperLeft, upperRight, spread, lineAlpha, accent, 5);
                    DrawTrace(mesh, upperRight, lowerRight, spread, lineAlpha, accent, 6);
                    DrawTrace(mesh, lowerRight, lowerLeft, spread, lineAlpha, accent, 7);
                }

                float flare = (1f - Mathf.Clamp01(_age / .15f)) * _cellSize;
                var glow = WithAlpha(accent, .48f * lineAlpha);
                var core = WithAlpha(Color.Lerp(accent, Color.white, .76f), .85f * lineAlpha);
                if (cross)
                {
                    DrawLine(mesh, _center - Vector2.right * flare * .34f, _center + Vector2.right * flare * .34f, flare * .06f, glow);
                    DrawLine(mesh, _center - Vector2.up * flare * .34f, _center + Vector2.up * flare * .34f, flare * .06f, glow);
                }
                DrawTriangle(mesh, _center + Vector2.up * flare * .19f, _center + Vector2.left * flare * .105f,
                    _center + Vector2.down * flare * .19f, core);
                DrawTriangle(mesh, _center + Vector2.up * flare * .19f, _center + Vector2.down * flare * .19f,
                    _center + Vector2.right * flare * .105f, core);
            }

            for (int i = 0; i < _shardCount; i++)
                DrawShard(mesh, _shards[i], i);
        }

        private void DrawTrace(VertexHelper mesh, Vector2 from, Vector2 to, float extent, float alpha, Color accent, int seed)
        {
            const int segments = 7;
            var normal = Vector2.Perpendicular(to - from).normalized;
            var previous = from;
            for (int segment = 1; segment <= segments; segment++)
            {
                float p = segment / (float)segments;
                float deviation = (segment % 2 == 0 ? 1f : -1f) * (.046f + .018f * ((segment + seed) % 3));
                var point = Vector2.Lerp(from, to, p) + normal * (_cellSize * deviation * Mathf.Sin(p * Mathf.PI));
                var visible = Vector2.Lerp(previous, point, (extent - p + 1f / segments) * segments);
                var core = Color.Lerp(accent, Color.white, .79f);
                DrawLine(mesh, previous, visible, _cellSize * .15f, WithAlpha(accent, alpha * .11f));
                DrawLine(mesh, previous, visible, _cellSize * .069f, WithAlpha(accent, alpha * .42f));
                DrawLine(mesh, previous, visible, _cellSize * .021f, WithAlpha(core, alpha * .94f));
                if ((segment == 2 || segment == 5) && p < extent)
                {
                    var spur = point + normal * (deviation > 0f ? _cellSize * .19f : -_cellSize * .19f) + (to - from) * .045f;
                    DrawLine(mesh, point, spur, _cellSize * .021f, WithAlpha(accent, alpha * .69f));
                }
                if (p >= extent) break;
                previous = point;
            }
        }

        private void DrawShard(VertexHelper mesh, Shard shard, int index)
        {
            float t = _age / shard.Duration;
            if (t >= 1f) return;
            float alpha = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(.28f, 1f, t));
            var position = shard.Origin + shard.Velocity * (_age * (1f - .30f * t))
                           + Vector2.down * (_cellSize * .28f * t * t);
            float rotation = shard.Angle + _age * (index % 2 == 0 ? 8f : -10f);
            var axis = new Vector2(Mathf.Cos(rotation), Mathf.Sin(rotation)) * (shard.Length * (1f - .46f * t));
            var side = Vector2.Perpendicular(axis) * .52f;
            var tip = position + axis;
            var left = position + side - axis * .39f;
            var right = position - side - axis * .44f;
            DrawTriangle(mesh, tip, left, position, WithAlpha(Color.Lerp(shard.Color, Color.white, .46f), alpha));
            DrawTriangle(mesh, tip, position, right, WithAlpha(shard.Color, alpha * .85f));
        }

        private static Rect LocalRect(RectTransform parent, RectTransform child)
        {
            var a = parent.InverseTransformPoint(child.TransformPoint(child.rect.min));
            var b = parent.InverseTransformPoint(child.TransformPoint(child.rect.max));
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static Color WithAlpha(Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        private static void DrawLine(VertexHelper mesh, Vector2 start, Vector2 end, float width, Color color)
        {
            if ((start - end).sqrMagnitude < .01f || width <= 0f) return;
            var side = Vector2.Perpendicular(end - start).normalized * (width * .5f);
            int v = mesh.currentVertCount;
            mesh.AddVert(start - side, color, Vector2.zero);
            mesh.AddVert(start + side, color, Vector2.zero);
            mesh.AddVert(end + side * .7f, color, Vector2.zero);
            mesh.AddVert(end - side * .7f, color, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
            mesh.AddTriangle(v + 2, v + 3, v);
        }

        private static void DrawTriangle(VertexHelper mesh, Vector2 a, Vector2 b, Vector2 c, Color color)
        {
            int v = mesh.currentVertCount;
            mesh.AddVert(a, color, Vector2.zero);
            mesh.AddVert(b, color, Vector2.zero);
            mesh.AddVert(c, color, Vector2.zero);
            mesh.AddTriangle(v, v + 1, v + 2);
        }
    }
}
