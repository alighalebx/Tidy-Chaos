using System;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    [Flags]
    public enum Cell : byte
    {
        None = 0,
        Low = 1 << 0,      // a body in the walking band
        High = 1 << 1,     // a body in the upper band (tall furniture, wall-mounted)
        Clear = 1 << 2,    // must stay free in front of something
        Door = 1 << 3,     // doorway approach zone
        Reserved = 1 << 4, // spawn, hazards
        Window = 1 << 5,   // in front of a window opening (blocks the upper band)
        Rug = 1 << 6,
        Prop = 1 << 7      // a loose prop (pushable: never blocks walking, but props do not stack on each other)
    }

    /// <summary>
    /// Occupancy grid for one room (10cm cells). A cell is "inside" a rect when its center is.
    /// Two height bands let low pieces sit under windows and wall-mounted pieces hang above counters.
    /// </summary>
    public sealed class RoomGrid
    {
        public readonly Rect area;
        public readonly float cell;
        public readonly int nx, nz;
        private byte[] _cells;

        public RoomGrid(Rect area, float cellSize = 0.1f)
        {
            this.area = area;
            cell = cellSize;
            nx = Mathf.Max(1, Mathf.RoundToInt(area.width / cell));
            nz = Mathf.Max(1, Mathf.RoundToInt(area.height / cell));
            _cells = new byte[nx * nz];
        }

        public Cell this[int i, int j] => (Cell)_cells[j * nx + i];

        public bool InBounds(int i, int j) => i >= 0 && j >= 0 && i < nx && j < nz;

        public Vector2 CellCenter(int i, int j) => new Vector2(area.xMin + (i + 0.5f) * cell, area.yMin + (j + 0.5f) * cell);

        public bool WorldToCell(Vector2 p, out int i, out int j)
        {
            i = Mathf.FloorToInt((p.x - area.xMin) / cell);
            j = Mathf.FloorToInt((p.y - area.yMin) / cell);
            return InBounds(i, j);
        }

        public void CellRange(Rect r, out int i0, out int i1, out int j0, out int j1)
        {
            const float eps = 1e-4f;
            i0 = Mathf.Max(0, Mathf.FloorToInt((r.xMin - area.xMin) / cell - 0.5f + eps) + 1);
            i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((r.xMax - area.xMin) / cell - 0.5f - eps) - 1);
            j0 = Mathf.Max(0, Mathf.FloorToInt((r.yMin - area.yMin) / cell - 0.5f + eps) + 1);
            j1 = Mathf.Min(nz - 1, Mathf.CeilToInt((r.yMax - area.yMin) / cell - 0.5f - eps) - 1);
        }

        public bool Any(Rect r, Cell mask)
        {
            CellRange(r, out int i0, out int i1, out int j0, out int j1);
            byte m = (byte)mask;
            for (int j = j0; j <= j1; j++)
            {
                int row = j * nx;
                for (int i = i0; i <= i1; i++)
                {
                    if ((_cells[row + i] & m) != 0) return true;
                }
            }

            return false;
        }

        public int Count(Rect r, Cell mask)
        {
            CellRange(r, out int i0, out int i1, out int j0, out int j1);
            byte m = (byte)mask;
            int n = 0;
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    if ((_cells[j * nx + i] & m) != 0) n++;
                }
            }

            return n;
        }

        /// Cells the rect touches at all (conservative). Marking is conservative and testing is by cell
        /// centers, so a missed overlap is always under half a cell (5cm).
        public void CoverRange(Rect r, out int i0, out int i1, out int j0, out int j1)
        {
            const float eps = 1e-4f;
            i0 = Mathf.Max(0, Mathf.FloorToInt((r.xMin - area.xMin) / cell + eps));
            i1 = Mathf.Min(nx - 1, Mathf.CeilToInt((r.xMax - area.xMin) / cell - eps) - 1);
            j0 = Mathf.Max(0, Mathf.FloorToInt((r.yMin - area.yMin) / cell + eps));
            j1 = Mathf.Min(nz - 1, Mathf.CeilToInt((r.yMax - area.yMin) / cell - eps) - 1);
        }

        public void Mark(Rect r, Cell flag)
        {
            CoverRange(r, out int i0, out int i1, out int j0, out int j1);
            byte f = (byte)flag;
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    _cells[j * nx + i] |= f;
                }
            }
        }

        public byte[] Snapshot() => (byte[])_cells.Clone();

        public void Restore(byte[] snapshot) => _cells = (byte[])snapshot.Clone();
    }
}
