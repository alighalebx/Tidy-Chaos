using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Everything a rule, scorer or strategy needs while one room is being planned.
    /// </summary>
    public sealed class PlanContext
    {
        public const float DoorZoneDepth = 1.1f;
        public const float FrontDoorZoneDepth = 1.5f;
        public const float DoorZoneMargin = 0.4f;
        public const float WindowZoneDepth = 0.6f;
        public const float OpenEdgeMargin = 0.3f;
        public const float WallMargin = 0.04f;

        public readonly RoomArea area;
        public readonly RoomGrid grid;
        public readonly System.Random rng;
        public readonly List<Placement> placed = new List<Placement>();
        public readonly IList<Obstacle> obstacles;
        public readonly Dictionary<string, int> houseCounts;
        public readonly HousePlan stats;
        public readonly float playerRadius;
        public readonly Rect inner; // area minus wall / open-edge margins
        public Connectivity.Cache connectivity; // built lazily by Connectivity.Check

        public Room Room => area.room;

        public PlanContext(RoomArea area, IList<Obstacle> obstacles, Dictionary<string, int> houseCounts, System.Random rng,
            HousePlan stats, float playerRadius)
        {
            this.area = area;
            this.obstacles = obstacles ?? new List<Obstacle>();
            this.houseCounts = houseCounts;
            this.rng = rng;
            this.stats = stats;
            this.playerRadius = playerRadius;
            grid = new RoomGrid(area.rect);

            Rect r = area.rect;
            float w = area.SideIsOpen(Side.West) ? OpenEdgeMargin : WallMargin;
            float e = area.SideIsOpen(Side.East) ? OpenEdgeMargin : WallMargin;
            float s = area.SideIsOpen(Side.South) ? OpenEdgeMargin : WallMargin;
            float n = area.SideIsOpen(Side.North) ? OpenEdgeMargin : WallMargin;
            inner = Rect.MinMaxRect(r.xMin + w, r.yMin + s, r.xMax - e, r.yMax - n);

            MarkStaticZones();
            Connectivity.Build(this); // baseline on the empty room (authored obstacles only)
            analysis = new RoomAnalysis(this);
        }

        public readonly RoomAnalysis analysis;

        /// Along-wall intervals blocked next to a wall piece: room corners, door jambs, and any low body
        /// standing within the piece's depth band of that wall (placed or authored).
        public void WallNeighbours(Placement p, Side side, List<(float from, float to)> into, bool windowsForTall = false)
        {
            into.Clear();
            bool h = HouseShell.IsHorizontal(side);
            Rect r = area.rect;
            PlacementStrategies.SideRange(area, side, out float sFrom, out float sTo);

            // room corners, unless the perpendicular side is open there
            Side lowSide = h ? Side.West : Side.South;
            Side highSide = h ? Side.East : Side.North;
            float line = PlacementStrategies.LineOf(area, side);
            float probe = line + (h ? HouseShell.Inward(side).y : HouseShell.Inward(side).x) * 0.5f;
            if (PlacementScorers.IsWallAt(area, lowSide, probe)) into.Add((float.MinValue, sFrom));
            if (PlacementScorers.IsWallAt(area, highSide, probe)) into.Add((sTo, float.MaxValue));

            // door jambs on this wall - and, for tall pieces, window openings (they can't stand there)
            foreach (WallSegment s in area.On(side))
            {
                if (s.IsOpening) into.Add((s.clearFrom, s.clearTo));
                else if (windowsForTall && p.high && s.kind == EdgeKind.Window) into.Add((s.clearFrom - 0.05f, s.clearTo + 0.05f));
            }

            // bodies in the band in front of this wall
            Vector2 n = HouseShell.Inward(side);
            float d0 = line, d1 = line + (h ? n.y : n.x) * (p.Depth + HouseShell.WallInset + 0.05f);
            Rect band = h ? Rect.MinMaxRect(r.xMin, Mathf.Min(d0, d1), r.xMax, Mathf.Max(d0, d1))
                          : Rect.MinMaxRect(Mathf.Min(d0, d1), r.yMin, Mathf.Max(d0, d1), r.yMax);
            foreach (Placement q in placed)
            {
                if (q == p || !q.low) continue;
                if (PlacementRules.Overlaps(q.body, band, 0.01f)) into.Add(h ? (q.body.xMin, q.body.xMax) : (q.body.yMin, q.body.yMax));
            }

            foreach (Obstacle o in obstacles)
            {
                if (!o.low || o.reserved) continue;
                if (PlacementRules.Overlaps(o.rect, band, 0.01f)) into.Add(h ? (o.rect.xMin, o.rect.xMax) : (o.rect.yMin, o.rect.yMax));
            }
        }

        /// Gaps from the piece's two ends to the nearest neighbour along the wall (MaxValue when open-ended).
        public void EndGaps(Placement p, Side side, List<(float from, float to)> neighbours, out float left, out float right)
        {
            bool h = HouseShell.IsHorizontal(side);
            float a = h ? p.body.xMin : p.body.yMin;
            float b = h ? p.body.xMax : p.body.yMax;
            left = right = float.MaxValue;
            foreach (var (f, t) in neighbours)
            {
                if (t <= a + 0.001f) left = Mathf.Min(left, a - t);
                else if (f >= b - 0.001f) right = Mathf.Min(right, f - b);
            }
        }

        private void MarkStaticZones()
        {
            foreach (WallSegment s in area.segments)
            {
                if (s.IsOpening)
                {
                    float depth = s.kind == EdgeKind.FrontDoor ? FrontDoorZoneDepth : DoorZoneDepth;
                    grid.Mark(Strip(s.side, s.line, s.clearFrom - DoorZoneMargin, s.clearTo + DoorZoneMargin, depth), Cell.Door);
                }
                else if (s.kind == EdgeKind.Window)
                {
                    grid.Mark(Strip(s.side, s.line, s.clearFrom - 0.05f, s.clearTo + 0.05f, WindowZoneDepth), Cell.Window);
                }
            }

            foreach (Obstacle o in obstacles)
            {
                if (o.low) grid.Mark(o.rect, Cell.Low);
                if (o.high) grid.Mark(o.rect, Cell.High);
                if (o.reserved) grid.Mark(o.rect, Cell.Reserved);
                if (o.hasClearance) grid.Mark(o.clearance, Cell.Clear);
            }
        }

        /// A strip along a room side, reaching 'depth' into the room.
        public Rect Strip(Side side, float line, float from, float to, float depth)
        {
            switch (side)
            {
                case Side.South: return Rect.MinMaxRect(from, line, to, line + depth);
                case Side.North: return Rect.MinMaxRect(from, line - depth, to, line);
                case Side.West: return Rect.MinMaxRect(line, from, line + depth, to);
                default: return Rect.MinMaxRect(line - depth, from, line, to);
            }
        }

        public Placement Find(string key)
        {
            foreach (Placement p in placed)
            {
                if (p.key == key) return p;
            }

            return null;
        }

        public int CountOf(string defId)
        {
            houseCounts.TryGetValue(defId, out int n);
            return n;
        }

        public float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        /// The room side behind a piece (opposite its front).
        public static Side BackSide(Placement p)
        {
            Vector2 f = p.Forward;
            if (f.y > 0.5f) return Side.South;
            if (f.y < -0.5f) return Side.North;
            return f.x > 0.5f ? Side.West : Side.East;
        }

        /// Distance between the piece's back edge and the wall line behind it.
        public float BackGap(Placement p, out Side side)
        {
            side = BackSide(p);
            Rect r = area.rect;
            switch (side)
            {
                case Side.South: return p.body.yMin - r.yMin;
                case Side.North: return r.yMax - p.body.yMax;
                case Side.West: return p.body.xMin - r.xMin;
                default: return r.xMax - p.body.xMax;
            }
        }

        /// Segments on a side that overlap the piece's span along that side.
        public IEnumerable<WallSegment> SegmentsBehind(Placement p, Side side)
        {
            bool horizontal = HouseShell.IsHorizontal(side);
            float a = horizontal ? p.body.xMin : p.body.yMin;
            float b = horizontal ? p.body.xMax : p.body.yMax;
            foreach (WallSegment s in area.On(side))
            {
                if (Mathf.Min(b, s.to) - Mathf.Max(a, s.from) > 0.01f) yield return s;
            }
        }

        /// Is the piece's back against a wall (within tolerance)?
        public bool AgainstWall(Placement p, out Side side) => Mathf.Abs(BackGap(p, out side) - HouseShell.WallInset) < 0.08f;
    }
}
