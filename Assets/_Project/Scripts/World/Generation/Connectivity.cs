using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// H8 - the room stays playable:
    ///  - furniture and walls are grown by the player radius; what remains is walkable
    ///  - every doorway (and the open living/kitchen edge, at least 0.9m of it) is in one connected region
    ///  - every interactable front (drawers, doors, seats, hiding spots) and access target is reachable
    /// </summary>
    public static class Connectivity
    {
        private static readonly Queue<int> Queue = new Queue<int>(4096);

        /// Per-room static data (walls, jambs, doorway seeds) - computed once per PlanContext.
        public sealed class Cache
        {
            public bool[] walls;
            public readonly List<(string name, List<int> seeds, bool open)> groups = new List<(string, List<int>, bool)>();
            public int[] kernel; // flattened (di, dj) offsets of the player-radius disc
            public bool[] skipObstacle;  // authored targets already unreachable before any furniture (can't be our fault)
            public bool skipGroups;      // authored stuff already disconnects the doorways - only access is checked
        }

        public static Cache Build(PlanContext ctx)
        {
            RoomGrid g = ctx.grid;
            float r = ctx.playerRadius;
            var c = new Cache { walls = new bool[g.nx * g.nz] };

            foreach (Side side in new[] { Side.South, Side.North, Side.West, Side.East })
            {
                for (int j = 0; j < g.nz; j++)
                {
                    for (int i = 0; i < g.nx; i++)
                    {
                        Vector2 p = g.CellCenter(i, j);
                        if (DistToSide(ctx.area.rect, side, p) >= r) continue;
                        float along = HouseShell.IsHorizontal(side) ? p.x : p.y;
                        if (!PassableAt(ctx.area, side, along, r)) c.walls[j * g.nx + i] = true;
                    }
                }
            }

            foreach (WallSegment s in ctx.area.segments)
            {
                if (s.IsOpening) c.groups.Add(($"{s.kind} {s.side} {s.from:F1}", Seeds(ctx, s.side, s.clearFrom + r, s.clearTo - r), false));
            }

            foreach (Side side in new[] { Side.South, Side.North, Side.West, Side.East })
            {
                if (!ctx.area.SideIsOpen(side)) continue;
                PlacementStrategies.SideRange(ctx.area, side, out float from, out float to);
                c.groups.Add(($"open {side}", Seeds(ctx, side, from, to), true));
            }

            int k = Mathf.CeilToInt(r / g.cell);
            float lim = r / g.cell; // marking is conservative already (up to a cell), so grow by r exactly
            lim *= lim;
            var ks = new List<int>();
            for (int dj = -k; dj <= k; dj++)
            {
                for (int di = -k; di <= k; di++)
                {
                    if (di * di + dj * dj >= lim) continue;
                    ks.Add(di);
                    ks.Add(dj);
                }
            }

            c.kernel = ks.ToArray();

            // baseline: what the authored room already fails is ignored from now on
            c.skipObstacle = new bool[ctx.obstacles.Count];
            ctx.connectivity = c;
            for (int guard = 0; guard < ctx.obstacles.Count + 2; guard++)
            {
                if (Check(ctx, out _, out int badObstacle, out bool badGroup)) break;
                if (badObstacle >= 0) c.skipObstacle[badObstacle] = true;
                else if (badGroup) c.skipGroups = true;
                else break;
            }

            return c;
        }

        public static bool Check(PlanContext ctx, out string reason) => Check(ctx, out reason, out _, out _);

        private static bool Check(PlanContext ctx, out string reason, out int badObstacle, out bool badGroup)
        {
            badObstacle = -1;
            badGroup = false;
            RoomGrid g = ctx.grid;
            int nx = g.nx, nz = g.nz;
            Cache cache = ctx.connectivity ??= Build(ctx);
            var blocked = (bool[])cache.walls.Clone();
            int[] kern = cache.kernel;

            // bodies, grown by the player radius (walls + jambs come pre-grown from the cache)
            for (int j = 0; j < nz; j++)
            {
                for (int i = 0; i < nx; i++)
                {
                    if ((g[i, j] & Cell.Low) == 0) continue;
                    for (int q = 0; q < kern.Length; q += 2)
                    {
                        int a = i + kern[q], b = j + kern[q + 1];
                        if (a >= 0 && b >= 0 && a < nx && b < nz) blocked[b * nx + a] = true;
                    }
                }
            }

            var groups = cache.groups;
            if (groups.Count == 0)
            {
                reason = null;
                return true;
            }

            var reached = new bool[nx * nz];
            Queue.Clear();
            for (int gi = 0; gi < (cache.skipGroups ? groups.Count : 1); gi++)
            {
                foreach (int s in groups[gi].seeds)
                {
                    if (blocked[s]) continue;
                    reached[s] = true;
                    Queue.Enqueue(s);
                }
            }

            if (Queue.Count == 0)
            {
                reason = "blocked: " + groups[0].name;
                badGroup = true;
                return false;
            }

            while (Queue.Count > 0)
            {
                int idx = Queue.Dequeue();
                int i = idx % nx, j = idx / nx;
                Visit(i + 1, j); Visit(i - 1, j); Visit(i, j + 1); Visit(i, j - 1);
            }

            void Visit(int a, int b)
            {
                if (a < 0 || b < 0 || a >= nx || b >= nz) return;
                int n = b * nx + a;
                if (reached[n] || blocked[n]) return;
                reached[n] = true;
                Queue.Enqueue(n);
            }

            for (int gi = 1; gi < groups.Count && !cache.skipGroups; gi++)
            {
                int hit = 0;
                foreach (int s in groups[gi].seeds)
                {
                    if (reached[s]) hit++;
                }

                int need = groups[gi].open ? 27 : 1; // open edge: ~0.9m wide stretch (3 rows deep)
                if (hit < need)
                {
                    reason = "disconnected: " + groups[gi].name;
                    badGroup = true;
                    return false;
                }
            }

            // interactable fronts and access targets
            foreach (Placement p in ctx.placed)
            {
                if (p.rug || p.onTop || p.baseY > 0.5f || (!p.interactable && !p.hasClearance)) continue;
                Rect probe = p.hasClearance ? p.clearance : Grow(p.body, 0.45f);
                if (!AnyReached(g, probe, reached))
                {
                    reason = "unreachable: " + p;
                    return false;
                }
            }

            for (int oi = 0; oi < ctx.obstacles.Count; oi++)
            {
                Obstacle o = ctx.obstacles[oi];
                if (!o.accessTarget || cache.skipObstacle[oi]) continue;
                Rect probe = o.hasClearance ? o.clearance : Grow(o.rect, 0.45f);
                if (!AnyReached(g, probe, reached))
                {
                    reason = "unreachable: " + o.name;
                    badObstacle = oi;
                    return false;
                }
            }

            reason = null;
            return true;
        }

        private static List<int> Seeds(PlanContext ctx, Side side, float from, float to)
        {
            var list = new List<int>();
            RoomGrid g = ctx.grid;
            float r = ctx.playerRadius;
            for (int j = 0; j < g.nz; j++)
            {
                for (int i = 0; i < g.nx; i++)
                {
                    Vector2 c = g.CellCenter(i, j);
                    float d = DistToSide(ctx.area.rect, side, c);
                    if (d < r || d > r + 0.3f) continue;
                    float along = HouseShell.IsHorizontal(side) ? c.x : c.y;
                    if (along < from || along > to) continue;
                    list.Add(j * g.nx + i);
                }
            }

            return list;
        }

        private static bool PassableAt(RoomArea area, Side side, float along, float r)
        {
            foreach (WallSegment s in area.On(side))
            {
                if (s.kind == EdgeKind.Open && along >= s.from && along <= s.to) return true;
                if (s.IsOpening && along >= s.clearFrom + r && along <= s.clearTo - r) return true;
            }

            return false;
        }

        private static float DistToSide(Rect rect, Side side, Vector2 c)
        {
            switch (side)
            {
                case Side.South: return c.y - rect.yMin;
                case Side.North: return rect.yMax - c.y;
                case Side.West: return c.x - rect.xMin;
                default: return rect.xMax - c.x;
            }
        }

        private static bool AnyReached(RoomGrid g, Rect probe, bool[] reached)
        {
            g.CellRange(probe, out int i0, out int i1, out int j0, out int j1);
            for (int j = j0; j <= j1; j++)
            {
                for (int i = i0; i <= i1; i++)
                {
                    if (reached[j * g.nx + i]) return true;
                }
            }

            return false;
        }

        public static Rect Grow(Rect r, float m) => Rect.MinMaxRect(r.xMin - m, r.yMin - m, r.xMax + m, r.yMax + m);
    }
}
