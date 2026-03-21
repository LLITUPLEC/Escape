using System;
using System.Collections.Generic;

namespace Project.Match3
{
    /// <summary>
    /// Pure-C# board logic for the 6×6 match-3 field.
    /// No Unity dependencies — safe to use from any thread.
    /// </summary>
    public class Match3BoardLogic
    {
        public const int Size = 6;

        private readonly PieceType[,] _board = new PieceType[Size, Size];
        private Random _rng;

        private static readonly PieceType[] SpawnPool =
            { PieceType.GemRed, PieceType.GemYellow, PieceType.GemGreen, PieceType.Skull, PieceType.Ankh };

        // ─── Accessors ────────────────────────────────────────────────────────────

        public PieceType this[int x, int y] => _board[x, y];

        // ─── Initialise ───────────────────────────────────────────────────────────

        public void Init(int seed)
        {
            _rng = new Random(seed);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                PieceType t;
                int tries = 0;
                do
                {
                    t = SpawnPool[_rng.Next(SpawnPool.Length)];
                    tries++;
                } while (tries < 20 && WouldCreateMatch(x, y, t));
                _board[x, y] = t;
            }
        }

        private bool WouldCreateMatch(int x, int y, PieceType t)
        {
            if (x >= 2 && _board[x - 1, y] == t && _board[x - 2, y] == t) return true;
            if (y >= 2 && _board[x, y - 1] == t && _board[x, y - 2] == t) return true;
            return false;
        }

        // ─── Serialisation ────────────────────────────────────────────────────────

        public int[] ToArray()
        {
            var arr = new int[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                arr[y * Size + x] = (int)_board[x, y];
            return arr;
        }

        public void FromArray(int[] arr)
        {
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                _board[x, y] = (PieceType)arr[y * Size + x];
        }

        // ─── Swap ─────────────────────────────────────────────────────────────────

        /// <summary>
        /// Swaps two adjacent cells. Returns true if the swap creates matches
        /// (the swap stays applied; caller should call ClearMatchedCells then ApplyGravityAndRefill).
        /// Returns false and reverts the swap if no matches form.
        /// </summary>
        public bool TrySwap(int x1, int y1, int x2, int y2, out List<MatchResult> matches)
        {
            matches = null;
            if (!InBounds(x1, y1) || !InBounds(x2, y2)) return false;
            if (Math.Abs(x1 - x2) + Math.Abs(y1 - y2) != 1) return false;

            DoSwap(x1, y1, x2, y2);
            matches = FindMatches();
            if (matches.Count > 0) return true;

            DoSwap(x1, y1, x2, y2); // revert
            matches = null;
            return false;
        }

        private void DoSwap(int x1, int y1, int x2, int y2)
        {
            var t = _board[x1, y1];
            _board[x1, y1] = _board[x2, y2];
            _board[x2, y2] = t;
        }

        // ─── Match Detection ──────────────────────────────────────────────────────

        public List<MatchResult> FindMatches()
        {
            var results = new List<MatchResult>();

            // Horizontal
            for (int y = 0; y < Size; y++)
            {
                int x = 0;
                while (x < Size)
                {
                    var t = _board[x, y];
                    if (t == PieceType.None) { x++; continue; }
                    int len = 1;
                    while (x + len < Size && _board[x + len, y] == t) len++;
                    if (len >= 3)
                    {
                        var r = new MatchResult { type = t, count = len, cells = new List<(int, int)>() };
                        for (int i = 0; i < len; i++) r.cells.Add((x + i, y));
                        results.Add(r);
                    }
                    x += len;
                }
            }

            // Vertical
            for (int x = 0; x < Size; x++)
            {
                int y = 0;
                while (y < Size)
                {
                    var t = _board[x, y];
                    if (t == PieceType.None) { y++; continue; }
                    int len = 1;
                    while (y + len < Size && _board[x, y + len] == t) len++;
                    if (len >= 3)
                    {
                        var r = new MatchResult { type = t, count = len, cells = new List<(int, int)>() };
                        for (int i = 0; i < len; i++) r.cells.Add((x, y + i));
                        results.Add(r);
                    }
                    y += len;
                }
            }

            return results;
        }

        // ─── Clear / Gravity / Refill ─────────────────────────────────────────────

        /// <summary>Sets all cells in matched groups to None.</summary>
        public void ClearMatchedCells(List<MatchResult> matches)
        {
            foreach (var m in matches)
                foreach (var (x, y) in m.cells)
                    _board[x, y] = PieceType.None;
        }

        /// <summary>
        /// Applies gravity (pieces fall downward, y increases) then fills
        /// empty top cells with new random pieces.
        /// Returns any NEW matches that formed after the refill (cascade).
        /// </summary>
        public List<MatchResult> ApplyGravityAndRefill()
        {
            // Gravity: for each column, compact pieces to the bottom
            for (int x = 0; x < Size; x++)
            {
                int writeY = Size - 1;
                for (int y = Size - 1; y >= 0; y--)
                {
                    if (_board[x, y] != PieceType.None)
                    {
                        _board[x, writeY] = _board[x, y];
                        if (writeY != y) _board[x, y] = PieceType.None;
                        writeY--;
                    }
                }
                // Clear remaining top cells
                for (int y = writeY; y >= 0; y--)
                    _board[x, y] = PieceType.None;
            }

            // Refill empty cells with new pieces
            if (_rng == null) _rng = new Random(0);
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                if (_board[x, y] == PieceType.None)
                    _board[x, y] = SpawnPool[_rng.Next(SpawnPool.Length)];

            return FindMatches();
        }

        // ─── Abilities ────────────────────────────────────────────────────────────

        /// <summary>
        /// Destroys pieces in the ability area (sets to None).
        /// Cross: 5-wide horizontal + 5-tall vertical centred on (cx, cy).
        /// Square: 3×3 area centred on (cx, cy).
        /// Returns the list of destroyed cell coordinates.
        /// </summary>
        public List<(int x, int y)> ApplyAbility(AbilityType ability, int cx, int cy)
        {
            var cells = new List<(int x, int y)>();

            if (ability == AbilityType.Cross)
            {
                for (int dx = -2; dx <= 2; dx++)
                {
                    int nx = cx + dx;
                    if (InBounds(nx, cy)) cells.Add((nx, cy));
                }
                for (int dy = -2; dy <= 2; dy++)
                {
                    if (dy == 0) continue;
                    int ny = cy + dy;
                    if (InBounds(cx, ny)) cells.Add((cx, ny));
                }
            }
            else if (ability == AbilityType.Square) // Square 3×3
            {
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    if (InBounds(nx, ny)) cells.Add((nx, ny));
                }
            }
            else if (InBounds(cx, cy)) // Petard: single target cell
            {
                cells.Add((cx, cy));
            }

            foreach (var (x, y) in cells)
                _board[x, y] = PieceType.None;

            return cells;
        }

        // ─── Helpers ──────────────────────────────────────────────────────────────

        private bool InBounds(int x, int y) => x >= 0 && x < Size && y >= 0 && y < Size;
    }
}
