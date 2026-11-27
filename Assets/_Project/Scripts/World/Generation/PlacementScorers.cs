using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// A soft preference. Scores add up; the planner picks among the best candidates with some randomness.
    /// </summary>
    public interface IPlacementScorer
    {
        float Score(PlanContext ctx, Placement p);
    }

    public static class PlacementScorers
    {
        public static List<IPlacementScorer> Default() => new List<IPlacementScorer>
        {
            new CornerScorer(),
            new WindowScorer(),
            new DoorFacingScorer(),
            new NearDoorwayScorer(),
            new CenterLineScorer(),
            new WallSpreadScorer(),
            new PrivacyScorer(),
            new CenteredScorer(),
            new LaneScorer(),
            new EdgeScorer(),
            new NookScorer(),
            new SightlineScorer(),
            new AboveFurnitureScorer(),
            new HazardLaneScorer()
        };

        /// Is the strip beside a body blocked (outside the room = wall, or a furniture body)?
        public static bool Blocked(PlanContext ctx, Rect strip)
        {
            Rect r = ctx.area.rect;
            if (strip.xMin < r.xMin || strip.xMax > r.xMax || strip.yMin < r.yMin || strip.yMax > r.yMax) return true;
            return ctx.grid.Any(strip, Cell.Low);
        }

        /// Number of the body's four sides that have a wall or furniture within 'reach'.
        public static int BlockedSides(PlanContext ctx, Rect b, float reach)
        {
            int n = 0;
            if (Blocked(ctx, Rect.MinMaxRect(b.xMin - reach, b.yMin, b.xMin, b.yMax))) n++;
            if (Blocked(ctx, Rect.MinMaxRect(b.xMax, b.yMin, b.xMax + reach, b.yMax))) n++;
            if (Blocked(ctx, Rect.MinMaxRect(b.xMin, b.yMin - reach, b.xMax, b.yMin))) n++;
            if (Blocked(ctx, Rect.MinMaxRect(b.xMin, b.yMax, b.xMax, b.yMax + reach))) n++;
            return n;
        }

        public static bool IsWallAt(RoomArea area, Side side, float coord)
        {
            foreach (WallSegment s in area.On(side))
            {
                if (coord >= s.from - 0.01f && coord <= s.to + 0.01f) return s.IsWall;
            }

            return false;
        }
    }

    /// Tall storage and tubs like corners.
    public sealed class CornerScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferCorner) return 0f;
            Rect r = ctx.area.rect;
            Rect b = p.body;
            const float t = 0.15f;
            bool w = b.xMin - r.xMin < t && PlacementScorers.IsWallAt(ctx.area, Side.West, b.center.y);
            bool e = r.xMax - b.xMax < t && PlacementScorers.IsWallAt(ctx.area, Side.East, b.center.y);
            bool s = b.yMin - r.yMin < t && PlacementScorers.IsWallAt(ctx.area, Side.South, b.center.x);
            bool n = r.yMax - b.yMax < t && PlacementScorers.IsWallAt(ctx.area, Side.North, b.center.x);
            return (w || e) && (s || n) ? 2.5f : 0f;
        }
    }

    /// Desks like windows; beds/TVs would rather not sit right under one.
    public sealed class WindowScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || (!p.entry.preferWindow && !p.entry.avoidWindow)) return 0f;
            if (!ctx.AgainstWall(p, out Side side)) return 0f;
            bool horizontal = HouseShell.IsHorizontal(side);
            float a = horizontal ? p.body.xMin : p.body.yMin;
            float b = horizontal ? p.body.xMax : p.body.yMax;
            bool underWindow = false;
            foreach (WallSegment s in ctx.SegmentsBehind(p, side))
            {
                if (s.kind == EdgeKind.Window && Mathf.Min(b, s.clearTo) - Mathf.Max(a, s.clearFrom) > 0.2f) underWindow = true;
            }

            if (!underWindow) return 0f;
            return p.entry.preferWindow ? 1.5f : -1.5f;
        }
    }

    /// Toilets shouldn't stare straight out of the doorway.
    public sealed class DoorFacingScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.avoidFacingDoorway) return 0f;
            Vector2 f = p.Forward;
            Side facing = f.y > 0.5f ? Side.North : f.y < -0.5f ? Side.South : f.x > 0.5f ? Side.East : Side.West;
            bool horizontal = HouseShell.IsHorizontal(facing);
            float c = horizontal ? p.center.x : p.center.y;
            foreach (WallSegment s in ctx.area.On(facing))
            {
                if (s.IsOpening && c > s.clearFrom - 0.4f && c < s.clearTo + 0.4f) return -3f;
            }

            return 0f;
        }
    }

    /// Shoe benches near a doorway.
    public sealed class NearDoorwayScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferNearDoorway) return 0f;
            float best = float.MaxValue;
            foreach (WallSegment s in ctx.area.segments)
            {
                if (!s.IsOpening) continue;
                float mid = (s.clearFrom + s.clearTo) * 0.5f;
                Vector2 c = HouseShell.IsHorizontal(s.side) ? new Vector2(mid, s.line) : new Vector2(s.line, mid);
                best = Mathf.Min(best, Vector2.Distance(c, p.center));
            }

            return best == float.MaxValue ? 0f : 1.5f * (1f - Mathf.Clamp01(best / 5f));
        }
    }

    /// Runners down the middle of the hallway.
    public sealed class CenterLineScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferCenterLine) return 0f;
            Rect r = ctx.area.rect;
            bool longX = r.width >= r.height;
            float off = longX ? Mathf.Abs(p.center.y - r.center.y) / (r.height * 0.5f) : Mathf.Abs(p.center.x - r.center.x) / (r.width * 0.5f);
            return -3f * off;
        }
    }

    /// Privacy gradient: toilets/beds away from the main entrance, sinks/shoe benches close to it.
    public sealed class PrivacyScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (Mathf.Abs(p.def.privacy) < 0.01f) return 0f;
            // strong enough that "near the entrance" vs "far end" is never inside the random tolerance
            return p.def.privacy * 7f * (ctx.analysis.Remoteness(p.center) - 0.5f);
        }
    }

    /// Big pieces look placed by a person when they're centred: on their free stretch of wall,
    /// or exactly under a window. Half-under-a-window looks accidental.
    public sealed class CenteredScorer : IPlacementScorer
    {
        private readonly List<(float, float)> _n = new List<(float, float)>();

        public float Score(PlanContext ctx, Placement p)
        {
            if (!p.def.preferCentered || !ctx.AgainstWall(p, out Side side)) return 0f;
            bool h = HouseShell.IsHorizontal(side);
            float a = h ? p.body.xMin : p.body.yMin;
            float b = h ? p.body.xMax : p.body.yMax;
            float c = (a + b) * 0.5f;
            float score = 0f;

            // centred on the free stretch between neighbours / corners / doors
            ctx.WallNeighbours(p, side, _n, windowsForTall: true);
            ctx.EndGaps(p, side, _n, out float left, out float right);
            if (left < 50f && right < 50f)
            {
                float off = Mathf.Abs(left - right) * 0.5f;
                score += 2.5f * (1f - Mathf.Clamp01(off / 0.6f));
            }

            // windows behind a low piece: exactly centred is good, partly covered is bad
            if (!p.high)
            {
                foreach (WallSegment s in ctx.SegmentsBehind(p, side))
                {
                    if (s.kind != EdgeKind.Window) continue;
                    float overlap = Mathf.Min(b, s.clearTo) - Mathf.Max(a, s.clearFrom);
                    if (overlap <= 0.05f) continue;
                    float wc = (s.clearFrom + s.clearTo) * 0.5f;
                    score += Mathf.Abs(c - wc) < 0.12f ? 2f : -2f;
                }
            }

            return score;
        }
    }

    /// Seating groups stay off the walking lanes between doorways. For a piece facing its anchor
    /// (sofa facing the TV) the whole zone between them counts.
    public sealed class LaneScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            bool group = p.entry != null && p.entry.relation == RelationKind.FacingAcross && p.anchor != null;
            if (!p.def.keepOffLanes && !group) return 0f;
            Rect zone = p.body;
            if (group)
            {
                Rect a = p.anchor.body;
                zone = Rect.MinMaxRect(Mathf.Min(zone.xMin, a.xMin), Mathf.Min(zone.yMin, a.yMin), Mathf.Max(zone.xMax, a.xMax), Mathf.Max(zone.yMax, a.yMax));
            }

            return -2.5f * ctx.analysis.LanesCrossed(zone);
        }
    }

    /// Clutter piles up along walls and beside furniture, not in the middle of the floor or a walking lane.
    public sealed class EdgeScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferEdges) return 0f;
            float s = PlacementScorers.BlockedSides(ctx, p.body, 0.35f) > 0 ? 2f : 0f;
            return s - 1.5f * ctx.analysis.LanesCrossed(p.body);
        }
    }

    /// Tucked away: the more sides closed in by walls/furniture, the better (beside a wardrobe, behind the sofa).
    public sealed class NookScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferNook) return 0f;
            return 1.2f * PlacementScorers.BlockedSides(ctx, p.body, 0.3f) - (ctx.analysis.InSightline(p.body) ? 2f : 0f);
        }
    }

    /// Hazards belong where people sprint: on the walking lanes between doorways.
    public sealed class HazardLaneScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferLanes) return 0f;
            return 2.5f * Mathf.Min(2, ctx.analysis.LanesCrossed(p.body));
        }
    }

    /// Obvious decoys sit in plain view from a doorway.
    public sealed class SightlineScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferSightline) return 0f;
            return ctx.analysis.InSightline(p.body) ? 2.5f : 0f;
        }
    }

    /// Wall decor centred above a low piece on the same wall (poster over the sofa, bed or desk).
    public sealed class AboveFurnitureScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.entry == null || !p.entry.preferAboveFurniture || !ctx.AgainstWall(p, out Side side)) return 0f;
            bool h = HouseShell.IsHorizontal(side);
            float pc = h ? p.center.x : p.center.y;
            float best = 0f;
            foreach (Placement q in ctx.placed)
            {
                if (!q.low || q.high || !ctx.AgainstWall(q, out Side qs) || qs != side) continue;
                float a = h ? q.body.xMin : q.body.yMin, b = h ? q.body.xMax : q.body.yMax;
                if (pc < a || pc > b) continue;
                float off = Mathf.Abs(pc - (a + b) * 0.5f);
                best = Mathf.Max(best, 2f + 1.5f * (1f - Mathf.Clamp01(off / 0.5f)));
            }

            return best;
        }
    }

    /// Spread anchors over different walls instead of lining one wall with everything.
    public sealed class WallSpreadScorer : IPlacementScorer
    {
        public float Score(PlanContext ctx, Placement p)
        {
            if (p.rug || p.onTop || p.anchor != null || !ctx.AgainstWall(p, out Side side)) return 0f;
            int same = 0;
            foreach (Placement o in ctx.placed)
            {
                if (o.rug || o.onTop || o.anchor != null) continue;
                if (ctx.AgainstWall(o, out Side os) && os == side) same++;
            }

            return -0.6f * same;
        }
    }
}
