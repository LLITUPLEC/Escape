using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace Project.Match3
{
    /// <summary>
    /// Visual-only overlay for two extra top rows (6x8 view for whitelisted players).
    /// Overlay has no input: swapping/using abilities in these rows is impossible on client.
    /// </summary>
    public sealed class Match3CheatRowsOverlayView : MonoBehaviour
    {
        private const int TopGhostRows = 2;
        private const int Cols = Match3BoardLogic.Size;

        [Header("Container (optional)")]
        [Tooltip("Optional container with GridLayoutGroup. If null, created at runtime.")]
        [SerializeField] private Transform cellContainer;
        [Tooltip("Optional prefab for CheatRowsOverlayCells container.")]
        [SerializeField] private Transform cellContainerPrefab;

        [Header("Piece Atlas (optional)")]
        [SerializeField] private Texture2D ballsAtlas;

        [Header("Ghost Row Look")]
        [SerializeField] private Color ghostBgColorTint = new Color(0.35f, 0.80f, 0.20f, 100f / 255f);

        [Header("Layout Overrides")]
        [Tooltip("Force CheatRowsOverlayCells anchoredPosition.")]
        [SerializeField] private bool forceContainerAnchoredPosition = true;
        [SerializeField] private Vector2 forcedContainerAnchoredPosition = new Vector2(-12.5f, 301f);

        [Header("Ability Highlight")]
        [SerializeField] private Color abilitySelColor = new Color(1f, 0.95f, 0.25f, 0.05f);
        [SerializeField] private Color abilitySelColorMax = new Color(1f, 0.95f, 0.25f, 0.52f);

        private Image[,] _bg;        // [x, yGhost]
        private RawImage[,] _icon;   // [x, yGhost]
        private Text[,] _lbl;       // [x, yGhost]
        private Image[,] _sel;      // [x, yGhost]

        private RectTransform _overlayContainerRt;
        private bool _built;

        private Texture2D _gridBorderTexture;

        private int[] _ghostBoard = Array.Empty<int>();

        private static readonly string[] BallsAtlasResourceCandidates =
        {
            "_Project/img/balls-sprite",
            "balls-sprite",
        };

        private static readonly string[] SelectionRingResourceCandidates =
        {
            "_Project/img/border",
            "border",
        };

        public bool IsBuilt => _built;
        public bool IsVisible => _overlayContainerRt != null && _overlayContainerRt.gameObject.activeSelf;

        public void SetCellContainerPrefab(Transform prefab)
        {
            if (_built) return;
            cellContainerPrefab = prefab;
        }

        public void SetVisible(bool visible)
        {
            if (_overlayContainerRt != null)
            {
                _overlayContainerRt.gameObject.SetActive(visible);
                return;
            }

            if (cellContainer != null)
                cellContainer.gameObject.SetActive(visible);
        }

        public void Build(Match3BoardView baseBoardView)
        {
            if (_built) return;
            if (baseBoardView == null || baseBoardView.cellContainer == null)
            {
                Debug.LogError("[CheatOverlay] baseBoardView or cellContainer is null.");
                return;
            }

            var glg = baseBoardView.cellContainer != null
                ? baseBoardView.cellContainer.GetComponent<GridLayoutGroup>()
                : null;

            var baseCellRt = baseBoardView.cellContainer as RectTransform;
            float baseAnchorX = baseCellRt != null ? baseCellRt.anchoredPosition.x : 0f;
            float baseAnchorY = baseCellRt != null ? baseCellRt.anchoredPosition.y : 0f;

            float cellW = glg != null ? glg.cellSize.x : 74f;
            float cellH = glg != null ? glg.cellSize.y : 74f;
            var spacing = glg != null ? glg.spacing : new Vector2(-1, -1);

            float overlayW = cellW * Cols + spacing.x * (Cols - 1);
            float overlayH = cellH * TopGhostRows + spacing.y * (TopGhostRows - 1);

            // Position overlay so its bottom edge touches the top edge of the base 6x6 grid.
            // We avoid relying on rect.height (can be 0 before layout rebuild) and instead use grid math.
            float stepY = cellH + spacing.y;
            float overlayCenterY = baseAnchorY + (stepY * TopGhostRows) / 2f + (stepY * Cols) / 2f;

            // Unity "fake null" safety: treat destroyed references as null.
            Transform containerTr = cellContainer;
            if (!containerTr)
            {
                if (cellContainerPrefab != null)
                {
                    containerTr = Instantiate(cellContainerPrefab, baseBoardView.cellContainer.parent, false);
                    containerTr.name = "CheatRowsOverlayCells";
                }
                else
                {
                    var overlayGo = new GameObject("CheatRowsOverlayCells");
                    overlayGo.transform.SetParent(baseBoardView.cellContainer.parent, false);
                    containerTr = overlayGo.transform;
                }
                cellContainer = containerTr;
            }
            else
            {
                // Ensure we're under the same parent as the main grid so alignment is stable.
                if (!baseBoardView.cellContainer || !baseBoardView.cellContainer.parent)
                {
                    Debug.LogWarning("[CheatOverlay] baseBoardView container is missing during Build.");
                    return;
                }
                containerTr.SetParent(baseBoardView.cellContainer.parent, false);
                containerTr.name = "CheatRowsOverlayCells";
            }

            if (!containerTr)
            {
                Debug.LogWarning("[CheatOverlay] Overlay container was destroyed during Build.");
                return;
            }

            _overlayContainerRt = containerTr as RectTransform;
            if (_overlayContainerRt == null)
                _overlayContainerRt = containerTr.gameObject.AddComponent<RectTransform>();
            _overlayContainerRt.anchorMin = new Vector2(0.5f, 0.5f);
            _overlayContainerRt.anchorMax = new Vector2(0.5f, 0.5f);
            _overlayContainerRt.pivot = new Vector2(0.5f, 0.5f);
            _overlayContainerRt.sizeDelta = new Vector2(overlayW, overlayH);
            if (forceContainerAnchoredPosition)
                _overlayContainerRt.anchoredPosition = forcedContainerAnchoredPosition;
            else
                _overlayContainerRt.anchoredPosition = new Vector2(baseAnchorX, overlayCenterY);

            var overlayGlg = containerTr.GetComponent<GridLayoutGroup>();
            if (overlayGlg == null) overlayGlg = containerTr.gameObject.AddComponent<GridLayoutGroup>();
            overlayGlg.cellSize = new Vector2(cellW, cellH);
            overlayGlg.spacing = spacing;
            overlayGlg.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            overlayGlg.constraintCount = Cols;
            overlayGlg.startCorner = GridLayoutGroup.Corner.UpperLeft;
            overlayGlg.startAxis = GridLayoutGroup.Axis.Horizontal;
            overlayGlg.childAlignment = TextAnchor.UpperLeft;

            // Prefer using the same atlas as the main board (ensures identical visuals).
            if (ballsAtlas == null && baseBoardView != null)
                ballsAtlas = baseBoardView.GetBallsAtlasTexture();
            TryLoadBallsAtlas();

            _bg = new Image[Cols, TopGhostRows];
            _icon = new RawImage[Cols, TopGhostRows];
            _lbl = new Text[Cols, TopGhostRows];
            _sel = new Image[Cols, TopGhostRows];

            for (int yGhost = 0; yGhost < TopGhostRows; yGhost++)
            for (int x = 0; x < Cols; x++)
            {
                var cellGo = new GameObject($"GhostCell_{x}_{yGhost}");
                cellGo.transform.SetParent(containerTr, false);
                cellGo.AddComponent<RectTransform>();

                // Bg
                var bgGo = new GameObject("Bg");
                bgGo.transform.SetParent(cellGo.transform, false);
                var bgRt = bgGo.AddComponent<RectTransform>();
                bgRt.anchorMin = Vector2.zero;
                bgRt.anchorMax = Vector2.one;
                bgRt.offsetMin = bgRt.offsetMax = Vector2.zero;
                var bgImg = bgGo.AddComponent<Image>();
                bgImg.color = ghostBgColorTint; // alpha defaults to 100/255
                bgImg.raycastTarget = false;
                _bg[x, yGhost] = bgImg;

                // Grid border overlay (helps match the main board look).
                var gridGo = new GameObject("GridBorder");
                gridGo.transform.SetParent(cellGo.transform, false);
                var gridRt = gridGo.AddComponent<RectTransform>();
                gridRt.anchorMin = Vector2.zero;
                gridRt.anchorMax = Vector2.one;
                gridRt.offsetMin = gridRt.offsetMax = Vector2.zero;
                var gridImg = gridGo.AddComponent<RawImage>();
                gridImg.texture = GetGridBorderTexture();
                gridImg.color = new Color(1f, 1f, 1f, 0.20f);
                gridImg.raycastTarget = false;

                // Icon
                var iconGo = new GameObject("Icon");
                iconGo.transform.SetParent(cellGo.transform, false);
                var iconRt = iconGo.AddComponent<RectTransform>();
                iconRt.anchorMin = Vector2.zero;
                iconRt.anchorMax = Vector2.one;
                iconRt.offsetMin = new Vector2(6f, 6f);
                iconRt.offsetMax = new Vector2(-6f, -6f);
                var iconImg = iconGo.AddComponent<RawImage>();
                iconImg.raycastTarget = false;
                iconImg.texture = ballsAtlas;
                iconImg.color = Color.white;
                iconImg.enabled = false;
                _icon[x, yGhost] = iconImg;

                // Label
                var lblGo = new GameObject("Lbl");
                lblGo.transform.SetParent(cellGo.transform, false);
                var lblRt = lblGo.AddComponent<RectTransform>();
                lblRt.anchorMin = Vector2.zero;
                lblRt.anchorMax = Vector2.one;
                lblRt.offsetMin = lblRt.offsetMax = Vector2.zero;
                var lbl = lblGo.AddComponent<Text>();
                lbl.font = Match3BoardView.GetFont();
                lbl.fontSize = 26;
                lbl.color = new Color(1f, 1f, 1f, 0.85f);
                lbl.alignment = TextAnchor.MiddleCenter;
                lbl.raycastTarget = false;
                lbl.text = string.Empty;
                _lbl[x, yGhost] = lbl;

                // Ability highlight overlay
                var selGo = new GameObject("Sel");
                selGo.transform.SetParent(cellGo.transform, false);
                var selRt = selGo.AddComponent<RectTransform>();
                selRt.anchorMin = Vector2.zero;
                selRt.anchorMax = Vector2.one;
                selRt.offsetMin = new Vector2(2, 2);
                selRt.offsetMax = new Vector2(-2, -2);
                var selImg = selGo.AddComponent<Image>();
                selImg.color = abilitySelColor;
                selImg.raycastTarget = false;
                selGo.SetActive(false);
                _sel[x, yGhost] = selImg;
            }

            _built = true;
        }

        public void RefreshAll(int[] ghostRows)
        {
            if (!_built || _bg == null) return;
            _ghostBoard = ghostRows ?? Array.Empty<int>();

            for (int yGhost = 0; yGhost < TopGhostRows; yGhost++)
            for (int x = 0; x < Cols; x++)
            {
                int idx = yGhost * Cols + x; // yGhost 0 => logical y=-2, yGhost 1 => logical y=-1
                int t = idx >= 0 && idx < _ghostBoard.Length ? _ghostBoard[idx] : 0;
                var pt = (PieceType)t;

                bool useAtlas = ballsAtlas != null;
                bool hasIcon = useAtlas && pt != PieceType.None;

                if (_bg[x, yGhost] != null)
                {
                    // Distinct look for ghost rows (so screenshot matches easily).
                    _bg[x, yGhost].color = useAtlas
                        ? ghostBgColorTint
                        : new Color(Match3BoardView.ColorOf(pt).r, Match3BoardView.ColorOf(pt).g, Match3BoardView.ColorOf(pt).b, 0.22f);
                }

                if (_icon[x, yGhost] != null)
                {
                    _icon[x, yGhost].texture = ballsAtlas;
                    _icon[x, yGhost].uvRect = UvRectOf(pt);
                    _icon[x, yGhost].color = Color.white;
                    _icon[x, yGhost].enabled = hasIcon;
                }

                if (_lbl[x, yGhost] != null)
                {
                    _lbl[x, yGhost].text = hasIcon ? string.Empty : Match3BoardView.LabelOf(pt);
                    _lbl[x, yGhost].color = new Color(1f, 1f, 1f, 0.85f);
                }
            }
        }

        public IEnumerator AnimateAbilityArea(AbilityType ability, int cx, int cy, float duration = 0.24f)
        {
            if (!_built || _sel == null || duration <= 0f) yield break;

            var cells = CollectGhostCellsForAbility(ability, cx, cy);
            if (cells.Count == 0) yield break;

            foreach (var (x, yGhost) in cells)
            {
                if (_sel[x, yGhost] == null) continue;
                _sel[x, yGhost].gameObject.SetActive(true);
                _sel[x, yGhost].color = abilitySelColor;
            }

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                float p = Mathf.Clamp01(t / duration);
                var alpha = Mathf.Lerp(abilitySelColor.a, abilitySelColorMax.a, p);
                foreach (var (x, yGhost) in cells)
                    if (_sel[x, yGhost] != null)
                        _sel[x, yGhost].color = new Color(abilitySelColor.r, abilitySelColor.g, abilitySelColor.b, alpha);
                yield return null;
            }

            foreach (var (x, yGhost) in cells)
                if (_sel[x, yGhost] != null) _sel[x, yGhost].gameObject.SetActive(false);
        }

        private System.Collections.Generic.List<(int x, int yGhost)> CollectGhostCellsForAbility(AbilityType ability, int cx, int cy)
        {
            var cells = new System.Collections.Generic.List<(int x, int yGhost)>();
            if (cx < 0 || cx >= Cols || cy < 0 || cy >= Cols) return cells;

            // Ghost logical rows:
            // yGhost 0 => logical y=-2
            // yGhost 1 => logical y=-1
            // We highlight only cells that belong to these ghost rows.

            if (ability == AbilityType.Cross)
            {
                // Vertical line: dy = -2..2, dy != 0
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dy == 0) continue;
                    int ny = cy + dy; // may become -2 or -1
                    if (ny == -2)
                        cells.Add((cx, 0));
                    else if (ny == -1)
                        cells.Add((cx, 1));
                }
            }
            else if (ability == AbilityType.Square)
            {
                // Square is 3x3 centered on (cx,cy): dy=-1..1
                // Only dy=-1 makes ny=-1 when cy==0.
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx;
                    if (nx < 0 || nx >= Cols) continue;

                    int nyLogical = cy - 1; // dy=-1
                    if (nyLogical == -1)
                        cells.Add((nx, 1));
                }
            }

            // De-dup
            if (cells.Count <= 1) return cells;
            var used = new bool[Cols, TopGhostRows];
            var uniq = new System.Collections.Generic.List<(int, int)>();
            foreach (var c in cells)
            {
                int x = c.x, yGhost = c.yGhost;
                if (x < 0 || x >= Cols || yGhost < 0 || yGhost >= TopGhostRows) continue;
                if (used[x, yGhost]) continue;
                used[x, yGhost] = true;
                uniq.Add((x, yGhost));
            }
            return uniq;
        }

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
            for (var i = 0; i < BallsAtlasResourceCandidates.Length; i++)
            {
                var path = BallsAtlasResourceCandidates[i];
                if (string.IsNullOrWhiteSpace(path)) continue;
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null)
                {
                    ballsAtlas = tex;
                    return;
                }
            }
        }

        private Texture2D GetGridBorderTexture()
        {
            if (_gridBorderTexture != null) return _gridBorderTexture;
            _gridBorderTexture = CreateSquareBorderTexture(64, 1);
            return _gridBorderTexture;
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
    }
}

