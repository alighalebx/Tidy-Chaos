using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// A hard constraint. A candidate that fails any rule is never placed.
    /// Add a rule = add a class and register it in <see cref="PlacementRules.Default"/>.
    /// </summary>
    public interface IPlacementRule
    {
        string Name { get; }
        bool Allows(PlanContext ctx, Placement p);
    }

    public static class PlacementRules
    {
        public static List<IPlacementRule> Default() => new List<IPlacementRule>
        {
            new RoomWhitelistRule(),
            new HouseCountRule(),
            new InsideRoomRule(),
            new NoOverlapRule(),
            new DoorwayRule(),
            new WindowRule(),
            new ReservedZoneRule(),
            new FrontClearanceRule(),
            new WallContactRule(),
            new SupportRule(),
            new SpacingRule(),
            new SideAccessRule(),
            new SightlineRule(),
            new DoorWallRule(),
            new AlignmentRule()
        };

        /// Pieces that are meant to touch: anchor + dependent, siblings of one anchor, modules of one counter run.
        public static bool Family(Placement a, Placement b)
        {
            if (a.anchor == b || b.anchor == a) return true;
            if (a.anchor != null && a.anchor == b.anchor) return true;
            return a.entry != null && b.entry != null && a.entry == b.entry && a.entry.placement == PlacementKind.CounterRun;
        }

        public static bool Overlaps(Rect a, Rect b, float tolerance = 0.01f)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > tolerance && h > tolerance;
        }
    }

    /// H11 - breathing room: unrelated pieces keep a gap (toilet 0.3m, kitchen table 0.6m for its chairs...).
    /// Exact rect test against placed pieces and authored obstacles - no grid rounding.
    public sealed class SpacingRule : IPlacementRule
    {
        public string Name => "H11 spacing";

        public bool Allows(PlanContext ctx, Placement p)
        {
            if (!p.low) return true;
            foreach (Placement q in ctx.placed)
            {
                if (!q.low || PlacementRules.Family(p, q)) continue;
                if (p.def.fitted && q.def.fitted && FlushOnSameWall(ctx, p, q.body)) continue;
                float gap = Mathf.Max(p.def.sideGap, q.def.sideGap);
                if (gap > 0f && PlacementRules.Overlaps(Connectivity.Grow(p.body, gap), q.body)) return false;
            }

            float own = Mathf.Max(p.def.sideGap, 0.1f);
            foreach (Obstacle o in ctx.obstacles)
            {
                if (!o.low || o.reserved) continue;
                if (p.def.fitted && FlushOnSameWall(ctx, p, o.rect)) continue; // fitted next to authored cabinets/appliances
                if (PlacementRules.Overlaps(Connectivity.Grow(p.body, own), o.rect)) return false;
            }

            return true;
        }

        /// Side by side along the same wall with (almost) no gap - a fitted run, not a squeeze.
        private static bool FlushOnSameWall(PlanContext ctx, Placement p, Rect other)
        {
            if (!ctx.AgainstWall(p, out Side side)) return false;
            bool h = HouseShell.IsHorizontal(side);
            float line = PlacementStrategies.LineOf(ctx.area, side);
            float otherBack = h ? (side == Side.South ? other.yMin : other.yMax) : (side == Side.West ? other.xMin : other.xMax);
            if (Mathf.Abs(otherBack - line) > 0.2f) return false; // the other piece isn't against this wall
            float a = h ? p.body.xMin : p.body.yMin, b = h ? p.body.xMax : p.body.yMax;
            float oa = h ? other.xMin : other.yMin, ob = h ? other.xMax : other.yMax;
            float gap = Mathf.Max(oa - b, a - ob);
            return gap > -0.02f && gap <= AlignmentRule.Flush;
        }
    }

    /// A1 - private fixtures (toilet) are never in view straight through a doorway.
    public sealed class SightlineRule : IPlacementRule
    {
        public string Name => "A1 sightline";
        public bool Allows(PlanContext ctx, Placement p) => !p.def.hideFromDoors || !ctx.analysis.InSightline(p.body);
    }

    /// A2 - keep away from doorways on the same wall (no toilet beside the door, no headboard by the door frame).
    public sealed class DoorWallRule : IPlacementRule
    {
        public string Name => "A2 door wall";

        public bool Allows(PlanContext ctx, Placement p)
        {
            float min = p.def.doorWallMinDistance;
            if (min <= 0f || !p.low || !ctx.AgainstWall(p, out Side side)) return true;
            bool h = HouseShell.IsHorizontal(side);
            float a = h ? p.body.xMin : p.body.yMin;
            float b = h ? p.body.xMax : p.body.yMax;
            foreach (WallSegment s in ctx.area.On(side))
            {
                if (!s.IsOpening) continue;
                float gap = Mathf.Max(s.clearFrom - b, a - s.clearTo);
                if (gap < min) return false;
            }

            return true;
        }
    }

    /// A3 - no awkward gaps along a wall: a piece sits flush against its neighbour/corner (<= 10cm)
    /// or leaves a usable gap (>= 40cm). Random 10-40cm slivers are what make a room look generated.
    public sealed class AlignmentRule : IPlacementRule
    {
        public const float Flush = 0.1f;
        public const float Usable = 0.4f;
        private readonly List<(float, float)> _n = new List<(float, float)>();

        public string Name => "A3 alignment";

        public bool Allows(PlanContext ctx, Placement p)
        {
            if (!p.low || p.rug || !ctx.AgainstWall(p, out Side side)) return true;
            ctx.WallNeighbours(p, side, _n);
            ctx.EndGaps(p, side, _n, out float left, out float right);
            return !Awkward(left) && !Awkward(right);
        }

        public static bool Awkward(float gap) => gap > Flush && gap < Usable;
    }

    /// H12 - beds keep at least one long side free to walk up to (that side is then reserved).
    public sealed class SideAccessRule : IPlacementRule
    {
        public string Name => "H12 side access";

        public bool Allows(PlanContext ctx, Placement p)
        {
            if (p.sideStrips == null) return true;
            foreach (Rect s in p.sideStrips)
            {
                if (IsFree(ctx, s)) return true;
            }

            return false;
        }

        public static bool IsFree(PlanContext ctx, Rect strip)
        {
            Rect r = ctx.area.rect;
            bool inside = strip.xMin >= r.xMin + 0.05f && strip.xMax <= r.xMax - 0.05f && strip.yMin >= r.yMin + 0.05f && strip.yMax <= r.yMax - 0.05f;
            return inside && !ctx.grid.Any(strip, Cell.Low | Cell.Door | Cell.Reserved);
        }
    }

    /// H1 - a piece only goes in rooms its def allows (no bed in the bathroom).
    public sealed class RoomWhitelistRule : IPlacementRule
    {
        public string Name => "H1 room whitelist";
        public bool Allows(PlanContext ctx, Placement p) => RoomMaskUtil.Allows(p.def.rooms, ctx.Room);
    }

    /// H10 - per-house limits (exactly one bed, toilet, fridge, stove...).
    public sealed class HouseCountRule : IPlacementRule
    {
        public string Name => "H10 house count";
        public bool Allows(PlanContext ctx, Placement p) => ctx.CountOf(p.def.id) < p.def.maxPerHouse;
    }

    /// H2 - the footprint stays inside the room (and away from the open living/kitchen edge).
    public sealed class InsideRoomRule : IPlacementRule
    {
        public string Name => "H2 inside room";

        public bool Allows(PlanContext ctx, Placement p)
        {
            const float e = 0.001f;
            Rect i = ctx.inner;
            Rect b = p.body;
            if (b.xMin < i.xMin - e || b.xMax > i.xMax + e || b.yMin < i.yMin - e || b.yMax > i.yMax + e) return false;
            if (!p.hasClearance) return true;

            Rect r = ctx.area.rect;
            Rect c = p.clearance;
            return c.xMin >= r.xMin - e && c.xMax <= r.xMax + e && c.yMin >= r.yMin - e && c.yMax <= r.yMax + e;
        }
    }

    /// H3 - no two bodies in the same height band; rugs don't stack.
    public sealed class NoOverlapRule : IPlacementRule
    {
        public string Name => "H3 overlap";

        public bool Allows(PlanContext ctx, Placement p)
        {
            if (p.rug) return !ctx.grid.Any(p.body, p.def.isHazard ? Cell.Rug | Cell.Low | Cell.Prop : Cell.Rug);
            if (p.loose) return !ctx.grid.Any(p.body, Cell.Low | Cell.Prop); // on rugs is fine, on furniture/props is not
            if (p.low && ctx.grid.Any(p.body, Cell.Low | Cell.Prop)) return false;
            if (p.high && !WithinTallAnchor(p) && ctx.grid.Any(p.body, Cell.High)) return false;
            return true;
        }

        /// A TV on its cabinet / mirror above a tall sink lives inside the band its anchor already reserved.
        private static bool WithinTallAnchor(Placement p)
        {
            if (p.anchor == null || !p.anchor.high) return false;
            Rect a = p.anchor.body;
            Rect b = p.body;
            const float e = 0.06f;
            return b.xMin >= a.xMin - e && b.xMax <= a.xMax + e && b.yMin >= a.yMin - e && b.yMax <= a.yMax + e;
        }
    }

    /// H4 - doorway approach zones stay empty (rugs allowed).
    public sealed class DoorwayRule : IPlacementRule
    {
        public string Name => "H4 doorway";
        public bool Allows(PlanContext ctx, Placement p) => (p.rug && !p.def.isHazard) || p.onTop || !ctx.grid.Any(p.body, Cell.Door);
    }

    /// H7 - nothing in the upper band in front of a window (tall pieces, wall-mounted pieces, TVs).
    public sealed class WindowRule : IPlacementRule
    {
        public string Name => "H7 window";
        public bool Allows(PlanContext ctx, Placement p) => !p.high || !ctx.grid.Any(p.body, Cell.Window);
    }

    /// H9 - reserved zones (player spawn, hazards) stay empty, rugs included.
    public sealed class ReservedZoneRule : IPlacementRule
    {
        public string Name => "H9 reserved";
        public bool Allows(PlanContext ctx, Placement p) => p.onTop || p.baseY > 0.5f || !ctx.grid.Any(p.body, Cell.Reserved);
    }

    /// H5 - a body may not stand in someone's front clearance, and its own clearance must be free.
    public sealed class FrontClearanceRule : IPlacementRule
    {
        public string Name => "H5 front clearance";

        public bool Allows(PlanContext ctx, Placement p)
        {
            if ((p.low || p.loose) && ctx.grid.Any(p.body, Cell.Clear)) return false;
            if (p.hasClearance && ctx.grid.Any(p.clearance, Cell.Low | Cell.Reserved)) return false;
            return true;
        }
    }

    /// H6 - wall pieces have a real wall behind their whole width (never a doorway or the open edge).
    public sealed class WallContactRule : IPlacementRule
    {
        public string Name => "H6 wall contact";

        public bool Allows(PlanContext ctx, Placement p)
        {
            if (!p.requiresWall) return true;
            if (!ctx.AgainstWall(p, out Side side)) return false;
            foreach (WallSegment s in ctx.SegmentsBehind(p, side))
            {
                if (!s.IsWall) return false;
            }

            return true;
        }
    }

    /// Things standing on top of another piece must fit on it.
    public sealed class SupportRule : IPlacementRule
    {
        public string Name => "support";

        public bool Allows(PlanContext ctx, Placement p)
        {
            if (!p.onTop || p.anchor == null) return true;
            Rect a = p.anchor.body;
            Rect b = p.body;
            const float e = 0.06f;
            return b.xMin >= a.xMin - e && b.xMax <= a.xMax + e && b.yMin >= a.yMin - e && b.yMax <= a.yMax + e;
        }
    }
}
