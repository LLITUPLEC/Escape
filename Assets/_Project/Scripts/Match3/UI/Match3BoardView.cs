using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Renders the 6×6 match-3 board.
    /// The prefab must have a child Transform with a GridLayoutGroup assigned to
    /// <see cref="cellContainer"/>. The 36 cell GameObjects are created at runtime
    /// by <see cref="Build"/>, so you can freely adjust GridLayoutGroup settings
    /// (cell size, spacing, colours) in the prefab without touching code.
    /// </summary>
    public sealed class Match3BoardView : MonoBehaviour
    {
        [Header("Container with GridLayoutGroup")]
        [SerializeField] public Transform cellContainer;
        [Header("Piece Atlas (optional)")]
        [SerializeField] private Texture2D ballsAtlas;

        private const int Size = Match3BoardLogic.Size;
        private const string BallsSpriteRelativePath = "_Project/img/balls-sprite.png";

        private Image[,] _bg;   // piece colour
        private Image[,] _sel;  // selection overlay
        private RawImage[,] _ring; // selected cell ring
        private RectTransform[,] _ringRt;
        private RawImage[,] _icon; // piece texture from atlas
        private RectTransform[,] _iconRt;
        private Text[,]  _lbl;  // piece symbol
        private Image _inactiveOverlay;
        private Texture2D _selectionRingTexture;
        private Texture2D _gridBorderTexture;

        /// <summary>Fired when the player clicks a cell.</summary>
        public event Action<int, int> CellClicked;

        // ─── Build ────────────────────────────────────────────────────────────────

        /// <summary>
        /// Creates 36 cell GameObjects inside <see cref="cellContainer"/>.
        /// Must be called once after instantiation (DuelMatch3Manager calls it in Start).
        /// </summary>
        public void Build()
        {
            if (cellContainer == null)
            {
                Debug.LogError("[Match3BoardView] cellContainer is not assigned.");
                return;
            }

            TryLoadBallsAtlas();

            var glgExisting = cellContainer.GetComponent<GridLayoutGroup>();
            // Ensure GridLayoutGroup exists (fallback if the prefab doesn't have one)
            if (glgExisting == null)
            {
                var glg = cellContainer.gameObject.AddComponent<GridLayoutGroup>();
                glg.cellSize        = new Vector2(74, 74);
                glg.spacing         = new Vector2(-1, -1);
                glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = Size;
                glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
                glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
                glg.childAlignment  = TextAnchor.UpperLeft;
            }
            else
            {
                glgExisting.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                glgExisting.constraintCount = Size;
                glgExisting.spacing = new Vector2(-1, -1);
            }

            _bg  = new Image[Size, Size];
            _sel = new Image[Size, Size];
            _ring = new RawImage[Size, Size];
            _ringRt = new RectTransform[Size, Size];
            _icon = new RawImage[Size, Size];
            _iconRt = new RectTransform[Size, Size];
            _lbl = new Text [Size, Size];

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int cx = x, cy = y;

                // ── Cell root (transparent, handles clicks) ───────────────────────
                var cellGo = new GameObject($"Cell_{x}_{y}");
                cellGo.transform.SetParent(cellContainer, false);
                cellGo.AddComponent<RectTransform>();

                var hitImg = cellGo.AddComponent<Image>();
                hitImg.color = Color.clear;

                var btn = cellGo.AddComponent<Button>();
                btn.targetGraphic = hitImg;
                var cb = btn.colors;
                cb.normalColor = cb.highlightedColor = cb.pressedColor = cb.disabledColor = Color.clear;
                btn.colors = cb;
                btn.onClick.AddListener(() => CellClicked?.Invoke(cx, cy));

                // ── Piece colour background ────────────────────────────────────────
                var bgGo = new GameObject("Bg");
                bgGo.transform.SetParent(cellGo.transform, false);
                var bgRt = bgGo.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero; bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
                var bgImg = bgGo.AddComponent<Image>();
                bgImg.color = ColorOf(PieceType.None);
                bgImg.raycastTarget = false;
                _bg[x, y] = bgImg;

                // ── Grid border overlay ────────────────────────────────────────────
                var gridGo = new GameObject("GridBorder");
                gridGo.transform.SetParent(cellGo.transform, false);
                var gridRt = gridGo.AddComponent<RectTransform>();
                gridRt.anchorMin = Vector2.zero; gridRt.anchorMax = Vector2.one;
                gridRt.offsetMin = gridRt.offsetMax = Vector2.zero;
                var gridImg = gridGo.AddComponent<RawImage>();
                gridImg.texture = GetGridBorderTexture();
                gridImg.color = new Color(1f, 1f, 1f, 0.20f);
                gridImg.raycastTarget = false;

                // ── Piece icon sprite ──────────────────────────────────────────────
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(cellGo.transform, false);
                var iconRt = iconGo.AddComponent<RectTransform>();
                iconRt.anchorMin = Vector2.zero; iconRt.anchorMax = Vector2.one;
                iconRt.offsetMin = new Vector2(6f, 6f); iconRt.offsetMax = new Vector2(-6f, -6f);
                var iconImg = iconGo.AddComponent<RawImage>();
                iconImg.raycastTarget = false;
                iconImg.texture = ballsAtlas;
                iconImg.enabled = false;
                _icon[x, y] = iconImg;
                _iconRt[x, y] = iconRt;

                // ── Selection ring (rotating outline) ─────────────────────────────
                var ringGo = new GameObject("SelRing");
                ringGo.transform.SetParent(cellGo.transform, false);
                var ringRt = ringGo.AddComponent<RectTransform>();
                ringRt.anchorMin = Vector2.zero; ringRt.anchorMax = Vector2.one;
                ringRt.offsetMin = new Vector2(2f, 2f); ringRt.offsetMax = new Vector2(-2f, -2f);
                var ringImg = ringGo.AddComponent<RawImage>();
                ringImg.texture = GetSelectionRingTexture();
                ringImg.color = new Color(1f, 0.92f, 0.28f, 0.95f);
                ringImg.raycastTarget = false;
                ringGo.SetActive(false);
                _ring[x, y] = ringImg;
                _ringRt[x, y] = ringRt;

                // ── Symbol label ──────────────────────────────────────────────────
                var lblGo = new GameObject("Lbl");
                lblGo.transform.SetParent(cellGo.transform, false);
                var lblRt = lblGo.AddComponent<RectTransform>();
                lblRt.anchorMin = Vector2.zero; lblRt.anchorMax = Vector2.one;
                lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
                var lbl = lblGo.AddComponent<Text>();
                lbl.font      = GetFont();
                lbl.fontSize  = 28;
                lbl.color     = new Color(1f, 1f, 1f, 0.85f);
                lbl.alignment = TextAnchor.MiddleCenter;
                lbl.raycastTarget = false;
                _lbl[x, y] = lbl;

                // ── Selection overlay (white tint) ────────────────────────────────
                var selGo = new GameObject("Sel");
                selGo.transform.SetParent(cellGo.transform, false);
                var selRt = selGo.AddComponent<RectTransform>();
                selRt.anchorMin = Vector2.zero; selRt.anchorMax = Vector2.one;
                selRt.offsetMin = new Vector2(2, 2); selRt.offsetMax = new Vector2(-2, -2);
                var selImg = selGo.AddComponent<Image>();
                selImg.color = new Color(1f, 1f, 1f, 0f);
                selImg.raycastTarget = false;
                selGo.SetActive(false);
                _sel[x, y] = selImg;
            }

            EnsureInactiveOverlay();
        }

        // ─── Refresh ──────────────────────────────────────────────────────────────

        public void RefreshAll(Match3BoardLogic board)
        {
            if (_bg == null) return;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var t = board[x, y];
                bool useAtlas = ballsAtlas != null;
                bool hasIcon = useAtlas && t != PieceType.None;
                if (_bg [x, y] != null) _bg [x, y].color = useAtlas ? new Color(0f, 0f, 0f, 0.12f) : ColorOf(t);
                if (_icon[x, y] != null)
                {
                    _icon[x, y].texture = ballsAtlas;
                    _icon[x, y].uvRect = UvRectOf(t);
                    _icon[x, y].color = Color.white;
                    _icon[x, y].enabled = hasIcon;
                }
                if (_iconRt[x, y] != null) _iconRt[x, y].localScale = Vector3.one;
                if (_lbl[x, y] != null)
                {
                    _lbl[x, y].text = hasIcon ? "" : LabelOf(t);
                    _lbl[x, y].color = new Color(1f, 1f, 1f, 0.85f);
                }
            }
        }

        public IEnumerator AnimateClear(List<MatchResult> matches, float duration = 0.30f)
        {
            if (_icon == null || matches == null || matches.Count == 0 || duration <= 0f)
                yield break;

            var cells = new HashSet<int>();
            foreach (var m in matches)
                foreach (var (x, y) in m.cells)
                    cells.Add(y * Size + x);

            if (cells.Count == 0) yield break;

            var iconStart = new Dictionary<int, Color>();
            var lblStart = new Dictionary<int, Color>();

            foreach (var id in cells)
            {
                int x = id % Size;
                int y = id / Size;
                if (_icon[x, y] != null) iconStart[id] = _icon[x, y].color;
                if (_lbl[x, y] != null) lblStart[id] = _lbl[x, y].color;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float scale = Mathf.Lerp(1f, 0.15f, p);
                float alpha = 1f - p;

                foreach (var id in cells)
                {
                    int x = id % Size;
                    int y = id / Size;

                    if (_iconRt[x, y] != null)
                        _iconRt[x, y].localScale = new Vector3(scale, scale, 1f);

                    if (_icon[x, y] != null && iconStart.TryGetValue(id, out var c0))
                    {
                        c0.a *= alpha;
                        _icon[x, y].color = c0;
                    }

                    if (_lbl[x, y] != null && lblStart.TryGetValue(id, out var l0))
                    {
                        l0.a *= alpha;
                        _lbl[x, y].color = l0;
                    }
                }
                yield return null;
            }

            foreach (var id in cells)
            {
                int x = id % Size;
                int y = id / Size;
                if (_iconRt[x, y] != null) _iconRt[x, y].localScale = Vector3.one;
            }
        }

        public IEnumerator AnimateClearByBoardDiff(int[] beforeBoard, int[] afterBoard, float duration = 1.0f)
        {
            if (_icon == null || beforeBoard == null || afterBoard == null) yield break;
            if (beforeBoard.Length < Size * Size || afterBoard.Length < Size * Size) yield break;

            var cells = new List<(int x, int y)>();
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                int b = beforeBoard[y * Size + x];
                int a = afterBoard[y * Size + x];
                if (b != 0 && a == 0) cells.Add((x, y));
            }
            if (cells.Count == 0) yield break;

            var iconStart = new Dictionary<int, Color>();
            var lblStart = new Dictionary<int, Color>();
            foreach (var (x, y) in cells)
            {
                int id = y * Size + x;
                if (_icon[x, y] != null) iconStart[id] = _icon[x, y].color;
                if (_lbl[x, y] != null) lblStart[id] = _lbl[x, y].color;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float scale = Mathf.Lerp(1f, 0.15f, p);
                float alpha = 1f - p;
                foreach (var (x, y) in cells)
                {
                    int id = y * Size + x;
                    if (_iconRt[x, y] != null) _iconRt[x, y].localScale = new Vector3(scale, scale, 1f);
                    if (_icon[x, y] != null && iconStart.TryGetValue(id, out var c0))
                    {
                        c0.a *= alpha;
                        _icon[x, y].color = c0;
                    }
                    if (_lbl[x, y] != null && lblStart.TryGetValue(id, out var l0))
                    {
                        l0.a *= alpha;
                        _lbl[x, y].color = l0;
                    }
                }
                yield return null;
            }

            foreach (var (x, y) in cells)
                if (_iconRt[x, y] != null) _iconRt[x, y].localScale = Vector3.one;
        }

        public IEnumerator AnimateDrop(int[] beforeBoard, Match3BoardLogic afterBoard, float duration = 0.48f)
        {
            if (_icon == null || _iconRt == null || afterBoard == null || duration <= 0f)
                yield break;

            var glg = cellContainer != null ? cellContainer.GetComponent<GridLayoutGroup>() : null;
            float stepY = glg != null ? (glg.cellSize.y + glg.spacing.y) : 78f;

            var drop = new int[Size, Size];
            var spawn = new bool[Size, Size];
            if (beforeBoard != null && beforeBoard.Length >= Size * Size)
                BuildDropDistances(beforeBoard, afterBoard, drop, spawn);

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (_iconRt[x, y] != null)
                    _iconRt[x, y].anchoredPosition = new Vector2(0f, drop[x, y] * stepY);
                if (_icon[x, y] != null && spawn[x, y])
                {
                    var c = _icon[x, y].color;
                    c.a = 0f;
                    _icon[x, y].color = c;
                }
            }

            var startOffset = new float[Size, Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                startOffset[x, y] = _iconRt[x, y] != null ? _iconRt[x, y].anchoredPosition.y : 0f;

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                // "Heavier" gravity feel: accelerate down, no duration change.
                float eased = p * p;
                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (_iconRt[x, y] != null)
                        _iconRt[x, y].anchoredPosition = new Vector2(0f, startOffset[x, y] * (1f - eased));

                for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    if (_icon[x, y] == null || !spawn[x, y]) continue;
                    var c = _icon[x, y].color;
                    c.a = eased;
                    _icon[x, y].color = c;
                }
                yield return null;
            }

            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                if (_iconRt[x, y] != null)
                    _iconRt[x, y].anchoredPosition = Vector2.zero;
                if (_icon[x, y] != null)
                    _icon[x, y].color = Color.white;
            }
        }

        public IEnumerator AnimateSwap(int x1, int y1, int x2, int y2, float duration = 0.30f)
        {
            if (_iconRt == null || !InRange(x1, y1) || !InRange(x2, y2) || duration <= 0f)
                yield break;

            var rt1 = _iconRt[x1, y1];
            var rt2 = _iconRt[x2, y2];
            if (rt1 == null || rt2 == null) yield break;

            var p1 = rt1.anchoredPosition;
            var p2 = rt2.anchoredPosition;
            Vector2 d12 = CellOffset(x2 - x1, y2 - y1);
            Vector2 d21 = CellOffset(x1 - x2, y1 - y2);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float eased = 1f - Mathf.Pow(1f - p, 3f);
                rt1.anchoredPosition = p1 + d12 * eased;
                rt2.anchoredPosition = p2 + d21 * eased;
                yield return null;
            }

            rt1.anchoredPosition = p1;
            rt2.anchoredPosition = p2;
        }

        public IEnumerator AnimateAbilityArea(AbilityType ability, int cx, int cy, float duration = 0.24f)
        {
            if (_sel == null || duration <= 0f) yield break;

            var cells = CollectAbilityCells(ability, cx, cy);
            if (cells.Count == 0) yield break;

            foreach (var (x, y) in cells)
            {
                if (_sel[x, y] == null) continue;
                _sel[x, y].gameObject.SetActive(true);
                _sel[x, y].color = new Color(1f, 0.95f, 0.25f, 0.05f);
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float alpha = Mathf.Lerp(0.05f, 0.52f, p);
                foreach (var (x, y) in cells)
                    if (_sel[x, y] != null) _sel[x, y].color = new Color(1f, 0.95f, 0.25f, alpha);
                yield return null;
            }

            foreach (var (x, y) in cells)
                if (_sel[x, y] != null) _sel[x, y].gameObject.SetActive(false);
        }

        public IEnumerator AnimateBoardTransition(int[] beforeBoard, Match3BoardLogic afterBoard, float duration = 0.45f)
        {
            if (_icon == null || _iconRt == null || beforeBoard == null || beforeBoard.Length < Size * Size || afterBoard == null)
                yield break;

            var changed = new List<(int x, int y)>();
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var before = (PieceType)beforeBoard[y * Size + x];
                var after = afterBoard[x, y];
                if (before != after) changed.Add((x, y));
            }
            if (changed.Count == 0) yield break;

            foreach (var (x, y) in changed)
            {
                if (_icon[x, y] != null) _icon[x, y].color = new Color(1f, 1f, 1f, 0f);
                if (_iconRt[x, y] != null) _iconRt[x, y].anchoredPosition = new Vector2(0f, 14f);
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                float eased = 1f - Mathf.Pow(1f - p, 3f);
                foreach (var (x, y) in changed)
                {
                    if (_icon[x, y] != null)
                    {
                        var c = _icon[x, y].color;
                        c.a = eased;
                        _icon[x, y].color = c;
                    }
                    if (_iconRt[x, y] != null)
                        _iconRt[x, y].anchoredPosition = new Vector2(0f, Mathf.Lerp(14f, 0f, eased));
                }
                yield return null;
            }

            foreach (var (x, y) in changed)
            {
                if (_icon[x, y] != null) _icon[x, y].color = Color.white;
                if (_iconRt[x, y] != null) _iconRt[x, y].anchoredPosition = Vector2.zero;
            }
        }

        public void SetDimmed(bool dimmed)
        {
            if (_inactiveOverlay == null) EnsureInactiveOverlay();
            if (_inactiveOverlay != null)
                _inactiveOverlay.gameObject.SetActive(dimmed);
        }

        public void SetCellSelected(int x, int y, bool selected)
        {
            if (_ring == null || !InRange(x, y)) return;
            var s = _ring[x, y];
            if (s == null) return;
            s.gameObject.SetActive(selected);
            if (selected) s.color = new Color(1f, 0.92f, 0.28f, 0.95f);
        }

        public void ClearSelections()
        {
            if (_ring == null) return;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (_ring[x, y] != null) _ring[x, y].gameObject.SetActive(false);
        }

        private bool InRange(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;

        // ─── Piece Visuals (static helpers shared with PrefabCreator) ─────────────

        public static Color ColorOf(PieceType t) => t switch
        {
            PieceType.GemRed    => new Color(0.90f, 0.18f, 0.18f),
            PieceType.GemYellow => new Color(0.95f, 0.82f, 0.12f),
            PieceType.GemGreen  => new Color(0.18f, 0.80f, 0.18f),
            PieceType.Skull     => new Color(0.28f, 0.28f, 0.30f),
            PieceType.Ankh      => new Color(0.90f, 0.75f, 0.15f),
            _                   => new Color(0.14f, 0.14f, 0.18f),
        };

        public static string LabelOf(PieceType t) => t switch
        {
            PieceType.GemRed    => "♦",
            PieceType.GemYellow => "♦",
            PieceType.GemGreen  => "●",
            PieceType.Skull     => "☠",
            PieceType.Ankh      => "✝",
            _                   => "",
        };

        private static Rect UvRectOf(PieceType t) => t switch
        {
            // atlas order: cross, red, yellow, green, skull
            PieceType.Ankh      => new Rect(0f / 5f, 0f, 1f / 5f, 1f),
            PieceType.GemRed    => new Rect(1f / 5f, 0f, 1f / 5f, 1f),
            PieceType.GemYellow => new Rect(2f / 5f, 0f, 1f / 5f, 1f),
            PieceType.GemGreen  => new Rect(3f / 5f, 0f, 1f / 5f, 1f),
            PieceType.Skull     => new Rect(4f / 5f, 0f, 1f / 5f, 1f),
            _                   => new Rect(0f, 0f, 1f, 1f),
        };

        private void TryLoadBallsAtlas()
        {
            if (ballsAtlas != null) return;
            ballsAtlas = LoadTextureFromDisk();
            if (ballsAtlas == null)
                Debug.LogWarning($"[Match3BoardView] Unable to load texture: {BallsSpriteRelativePath}");
        }

        private static Texture2D LoadTextureFromDisk()
        {
            try
            {
                var fullPath = Path.Combine(Application.dataPath, BallsSpriteRelativePath);
                if (!File.Exists(fullPath)) return null;

                var bytes = File.ReadAllBytes(fullPath);
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes)) return null;

                tex.filterMode = FilterMode.Bilinear;
                tex.wrapMode = TextureWrapMode.Clamp;
                return tex;
            }
            catch
            {
                return null;
            }
        }

        private void EnsureInactiveOverlay()
        {
            if (_inactiveOverlay != null) return;

            var go = new GameObject("InactiveOverlay");
            var rt = go.AddComponent<RectTransform>();
            rt.SetParent(transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            _inactiveOverlay = go.AddComponent<Image>();
            _inactiveOverlay.color = new Color(0f, 0f, 0f, 0.42f);
            _inactiveOverlay.raycastTarget = false;
            go.SetActive(false);
        }

        private static void BuildDropDistances(int[] beforeBoard, Match3BoardLogic afterBoard, int[,] drop, bool[,] spawn)
        {
            for (int x = 0; x < Size; x++)
            {
                var survivors = new List<(PieceType type, int fromY)>();
                for (int y = Size - 1; y >= 0; y--)
                {
                    var t = (PieceType)beforeBoard[y * Size + x];
                    if (t != PieceType.None) survivors.Add((t, y));
                }

                int survivorsCount = survivors.Count;
                int finalBottomStart = Size - survivorsCount;

                for (int i = 0; i < survivorsCount; i++)
                {
                    int toY = Size - 1 - i;
                    int fromY = survivors[i].fromY;
                    drop[x, toY] = Mathf.Max(0, toY - fromY);
                    spawn[x, toY] = false;
                }

                for (int y = 0; y < finalBottomStart; y++)
                {
                    var afterType = afterBoard[x, y];
                    // Spawned pieces start above the board keeping fixed column spacing,
                    // which prevents visual "overtake" during long vertical falls.
                    drop[x, y] = afterType == PieceType.None ? 0 : finalBottomStart;
                    spawn[x, y] = afterType != PieceType.None;
                }
            }
        }

        private Vector2 CellOffset(int dx, int dy)
        {
            var glg = cellContainer != null ? cellContainer.GetComponent<GridLayoutGroup>() : null;
            if (glg == null) return new Vector2(dx * 78f, -dy * 78f);
            return new Vector2(dx * (glg.cellSize.x + glg.spacing.x), -dy * (glg.cellSize.y + glg.spacing.y));
        }

        private List<(int x, int y)> CollectAbilityCells(AbilityType ability, int cx, int cy)
        {
            var cells = new List<(int x, int y)>();
            if (!InRange(cx, cy)) return cells;

            if (ability == AbilityType.Cross)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int nx = cx + dx;
                    if (InRange(nx, cy)) cells.Add((nx, cy));
                }
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dy == 0) continue;
                    int ny = cy + dy;
                    if (InRange(cx, ny)) cells.Add((cx, ny));
                }
                return cells;
            }

            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if (InRange(nx, ny)) cells.Add((nx, ny));
            }
            return cells;
        }

        private void Update()
        {
            if (_ringRt == null) return;
            float delta = -165f * Time.deltaTime;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (_ring[x, y] != null && _ring[x, y].gameObject.activeSelf && _ringRt[x, y] != null)
                    _ringRt[x, y].Rotate(0f, 0f, delta);
        }

        private Texture2D GetSelectionRingTexture()
        {
            if (_selectionRingTexture != null) return _selectionRingTexture;
            _selectionRingTexture = CreateRingTexture(128, 0.72f, 0.92f);
            return _selectionRingTexture;
        }

        private Texture2D GetGridBorderTexture()
        {
            if (_gridBorderTexture != null) return _gridBorderTexture;
            _gridBorderTexture = CreateSquareBorderTexture(64, 1);
            return _gridBorderTexture;
        }

        private static Texture2D CreateRingTexture(int size, float innerRadius, float outerRadius)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;

            float half = (size - 1) * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float nx = (x - half) / half;
                float ny = (y - half) / half;
                float d = Mathf.Sqrt(nx * nx + ny * ny);
                float alpha = d >= innerRadius && d <= outerRadius ? 1f : 0f;
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
            tex.Apply(false, false);
            return tex;
        }

        private static Texture2D CreateSquareBorderTexture(int size, int thickness)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Point;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool border = x < thickness || y < thickness || x >= size - thickness || y >= size - thickness;
                tex.SetPixel(x, y, border ? Color.white : new Color(1f, 1f, 1f, 0f));
            }
            tex.Apply(false, false);
            return tex;
        }

        private static Font _font;
        internal static Font GetFont()
        {
            if (_font != null) return _font;
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (_font == null) _font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return _font;
        }
    }
}
