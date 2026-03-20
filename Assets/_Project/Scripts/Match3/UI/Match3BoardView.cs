using System;
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

        private const int Size = Match3BoardLogic.Size;

        private Image[,] _bg;   // piece colour
        private Image[,] _sel;  // selection overlay
        private Text[,]  _lbl;  // piece symbol

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

            // Ensure GridLayoutGroup exists (fallback if the prefab doesn't have one)
            if (cellContainer.GetComponent<GridLayoutGroup>() == null)
            {
                var glg = cellContainer.gameObject.AddComponent<GridLayoutGroup>();
                glg.cellSize        = new Vector2(74, 74);
                glg.spacing         = new Vector2(4, 4);
                glg.constraint      = GridLayoutGroup.Constraint.FixedColumnCount;
                glg.constraintCount = Size;
                glg.startCorner     = GridLayoutGroup.Corner.UpperLeft;
                glg.startAxis       = GridLayoutGroup.Axis.Horizontal;
                glg.childAlignment  = TextAnchor.UpperLeft;
            }

            _bg  = new Image[Size, Size];
            _sel = new Image[Size, Size];
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
        }

        // ─── Refresh ──────────────────────────────────────────────────────────────

        public void RefreshAll(Match3BoardLogic board)
        {
            if (_bg == null) return;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var t = board[x, y];
                if (_bg [x, y] != null) _bg [x, y].color = ColorOf(t);
                if (_lbl[x, y] != null) _lbl[x, y].text  = LabelOf(t);
            }
        }

        public void SetCellSelected(int x, int y, bool selected)
        {
            if (_sel == null || !InRange(x, y)) return;
            var s = _sel[x, y];
            if (s == null) return;
            s.gameObject.SetActive(selected);
            if (selected) s.color = new Color(1f, 1f, 1f, 0.45f);
        }

        public void ClearSelections()
        {
            if (_sel == null) return;
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (_sel[x, y] != null) _sel[x, y].gameObject.SetActive(false);
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
