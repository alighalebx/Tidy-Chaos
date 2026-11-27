using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Proposes candidate poses for one entry. Strategies only generate - rules decide.
    /// </summary>
    public interface IPlacementStrategy
    {
        IEnumerable<Placement> Candidates(PlanContext ctx, RecipeEntry entry, FurnitureDef def);
    }

    public static class PlacementStrategies
    {
        private static readonly WallStrategy Wall = new WallStrategy();
        private static readonly FreeStrategy Free = new FreeStrategy();
        private static readonly RelativeStrategy Relative = new RelativeStrategy();
        private static readonly AtOpeningStrategy Opening = new AtOpeningStrategy();

        public static IPlacementStrategy For(PlacementKind kind)
        {
            switch (kind)
            {
                case PlacementKind.Wall: return Wall;
                case PlacementKind.Free: return Free;
                case PlacementKind.Relative: return Relative;
                case PlacementKind.AtOpening: return Opening;
                default: return null; // CounterRun is composite, see CounterRunStrategy
            }
        }

        /// Pose with the piece's back flush against a wall line on 'side', centered at 'along'.
        public static Placement AgainstWall(FurnitureDef def, RoomArea area, Side side, float along)
        {
            Vector2 inward = HouseShell.Inward(side);
            float line = LineOf(area, side);
            float depthOff = def.size.z * 0.5f + HouseShell.WallInset;
            Vector2 c = HouseShell.IsHorizontal(side)
                ? new Vector2(along, line + inward.y * depthOff)
                : new Vector2(line + inward.x * depthOff, along);
            Placement p = Placement.Create(def, c, HouseShell.InwardYaw(side));
            p.requiresWall = true;
            return p;
        }

        public static float LineOf(RoomArea area, Side side)
        {
            Rect r = area.rect;
            switch (side)
            {
                case Side.South: return r.yMin;
                case Side.North: return r.yMax;
                case Side.West: return r.xMin;
                default: return r.xMax;
            }
        }

        public static void SideRange(RoomArea area, Side side, out float from, out float to)
        {
            Rect r = area.rect;
            if (HouseShell.IsHorizontal(side)) { from = r.xMin; to = r.xMax; }
            else { from = r.yMin; to = r.yMax; }
        }

        /// Wall-mounted above an anchor with backs aligned (mirror over the sink, upper over a base cabinet).
        public static Placement Above(Placement anchor, FurnitureDef def)
        {
            Vector2 f = anchor.Forward;
            Vector2 c = anchor.center + f * (-anchor.Depth * 0.5f + def.size.z * 0.5f);
            Placement p = Placement.Create(def, c, anchor.yaw, def.baseHeight);
            p.requiresWall = anchor.requiresWall;
            return p;
        }

        /// Standing on an anchor's top surface.
        public static Placement OnTop(Placement anchor, FurnitureDef def, float alongOffset = 0f)
        {
            Vector2 c = anchor.center + anchor.Right * alongOffset;
            return Placement.Create(def, c, anchor.yaw, anchor.baseY + anchor.def.size.y, onTop: true);
        }
    }

    /// Back flush with a wall: sliding along every wall in 10cm steps, plus positions snapped flush against
    /// each neighbour on that wall (a 10cm lattice alone almost never lands exactly flush).
    public sealed class WallStrategy : IPlacementStrategy
    {
        private const float Step = 0.1f;
        public const float SnapGap = 0.05f; // clears the grid's conservative cell marking, still "flush"
        private readonly List<(float, float)> _n = new List<(float, float)>();

        public IEnumerable<Placement> Candidates(PlanContext ctx, RecipeEntry entry, FurnitureDef def)
        {
            foreach (Side side in new[] { Side.South, Side.North, Side.West, Side.East })
            {
                if (ctx.area.SideIsOpen(side)) continue;
                PlacementStrategies.SideRange(ctx.area, side, out float from, out float to);
                float half = def.size.x * 0.5f;
                float lo = from + half + PlanContext.WallMargin, hi = to - half - PlanContext.WallMargin;
                for (float t = lo; t <= hi + 0.001f; t += Step)
                {
                    yield return PlacementStrategies.AgainstWall(def, ctx.area, side, t);
                }

                // snapped flush to the neighbours along this wall
                Placement probe = PlacementStrategies.AgainstWall(def, ctx.area, side, (lo + hi) * 0.5f);
                ctx.WallNeighbours(probe, side, _n, windowsForTall: true);
                var snaps = new List<float>();
                foreach (var (f, t) in _n)
                {
                    if (t < float.MaxValue * 0.5f && t > float.MinValue * 0.5f) snaps.Add(t + SnapGap + half);
                    if (f > float.MinValue * 0.5f && f < float.MaxValue * 0.5f) snaps.Add(f - SnapGap - half);
                }

                foreach (float s in snaps)
                {
                    if (s >= lo - 0.001f && s <= hi + 0.001f) yield return PlacementStrategies.AgainstWall(def, ctx.area, side, s);
                }
            }
        }
    }

    /// Centred just inside an opening of the given kind, facing into the room (doormat at the front door).
    public sealed class AtOpeningStrategy : IPlacementStrategy
    {
        public IEnumerable<Placement> Candidates(PlanContext ctx, RecipeEntry entry, FurnitureDef def)
        {
            foreach (WallSegment s in ctx.area.segments)
            {
                if (s.kind != entry.openingKind) continue;
                Vector2 c = HouseShell.OpeningCenter(s) + HouseShell.Inward(s.side) * (def.size.z * 0.5f + 0.05f);
                yield return Placement.Create(def, c, HouseShell.InwardYaw(s.side));
            }
        }
    }

    /// Anywhere on the floor, in 4 orientations (or along the room's long axis).
    public sealed class FreeStrategy : IPlacementStrategy
    {
        public IEnumerable<Placement> Candidates(PlanContext ctx, RecipeEntry entry, FurnitureDef def)
        {
            float Step = def.isLoose ? 0.3f : 0.2f; // props: coarser lattice, they get a random spin anyway
            Rect r = ctx.inner;
            bool longX = ctx.area.rect.width >= ctx.area.rect.height;
            float[] yaws = def.isLoose ? new[] { 0f } :
                entry.alignLongAxis ? (longX ? new[] { 0f, 180f } : new[] { 90f, 270f }) : new[] { 0f, 90f, 180f, 270f };

            // centered lattice, so the exact room center line is always a candidate
            int kx = Mathf.FloorToInt(r.width * 0.5f / Step);
            int kz = Mathf.FloorToInt(r.height * 0.5f / Step);

            if (def.isLoose)
            {
                // props: a random sample of the lattice is plenty (and reads less gridded), at a fraction of the cost
                const int Sample = 140;
                int total = (2 * kx + 1) * (2 * kz + 1);
                for (int n = 0; n < Mathf.Min(Sample, total); n++)
                {
                    int idx = ctx.rng.Next(total);
                    int i = idx % (2 * kx + 1) - kx, j = idx / (2 * kx + 1) - kz;
                    yield return Placement.Create(def, r.center + new Vector2(i * Step, j * Step), 0f);
                }

                yield break;
            }

            for (int i = -kx; i <= kx; i++)
            {
                for (int j = -kz; j <= kz; j++)
                {
                    Vector2 c = r.center + new Vector2(i * Step, j * Step);
                    foreach (float yaw in yaws)
                    {
                        yield return Placement.Create(def, c, yaw);
                    }
                }
            }
        }
    }

    /// Positioned from an already placed anchor.
    public sealed class RelativeStrategy : IPlacementStrategy
    {
        public IEnumerable<Placement> Candidates(PlanContext ctx, RecipeEntry entry, FurnitureDef def)
        {
            Placement a = ctx.Find(entry.anchorKey);
            if (a == null) yield break;
            Vector2 f = a.Forward;
            Vector2 r = a.Right;
            float mid = (entry.minDistance + entry.maxDistance) * 0.5f;

            foreach (Placement p in Raw(ctx, entry, def, a, f, r, mid))
            {
                p.anchor = a;
                yield return p;
            }
        }

        private static IEnumerable<Placement> Raw(PlanContext ctx, RecipeEntry entry, FurnitureDef def, Placement a, Vector2 f, Vector2 r, float mid)
        {
            switch (entry.relation)
            {
                case RelationKind.BesideFlush:
                    foreach (int s in new[] { -1, 1 })
                    {
                        float gap = Mathf.Max(0.02f, entry.minDistance);
                        Vector2 c = a.center + r * s * (a.Width * 0.5f + def.size.x * 0.5f + gap) + f * (-a.Depth * 0.5f + def.size.z * 0.5f);
                        Placement p = Placement.Create(def, c, a.yaw);
                        p.requiresWall = a.requiresWall;
                        yield return p;
                    }

                    break;

                case RelationKind.InFront:
                    // small sideways shifts let a wide rug clear a wall when its anchor sits in a corner
                    for (float d = entry.minDistance; d <= entry.maxDistance + 0.001f; d += 0.05f)
                    {
                        for (float lat = -0.6f; lat <= 0.601f; lat += 0.3f)
                        {
                            Vector2 c = a.center + f * (a.Depth * 0.5f + d + def.size.z * 0.5f) + r * lat;
                            Placement p = Placement.Create(def, c, a.yaw);
                            p.bias = -Mathf.Abs(d - mid) - 1.5f * Mathf.Abs(lat);
                            yield return p;
                        }
                    }

                    break;

                case RelationKind.FacingAcross:
                    for (float d = entry.minDistance; d <= entry.maxDistance + 0.001f; d += 0.1f)
                    {
                        for (float lat = -0.4f; lat <= 0.401f; lat += 0.2f)
                        {
                            Vector2 c = a.center + f * (a.Depth * 0.5f + d + def.size.z * 0.5f) + r * lat;
                            Placement p = Placement.Create(def, c, a.yaw + 180f);
                            p.bias = -1.2f * Mathf.Abs(lat) - 0.3f * Mathf.Abs(d - mid);
                            yield return p;
                        }
                    }

                    break;

                case RelationKind.SideOf:
                    foreach (int s in new[] { -1, 1 })
                    {
                        for (float d = entry.minDistance; d <= entry.maxDistance + 0.001f; d += 0.05f)
                        {
                            foreach (float along in new[] { -0.15f, 0f, 0.15f })
                            {
                                Vector2 c = a.center + r * s * (a.Width * 0.5f + d + def.size.z * 0.5f) + f * along;
                                Vector2 face = -r * s;
                                float yaw = Mathf.Atan2(face.x, face.y) * Mathf.Rad2Deg;
                                Placement p = Placement.Create(def, c, yaw);
                                p.bias = -Mathf.Abs(d - mid) - Mathf.Abs(along);
                                yield return p;
                            }
                        }
                    }

                    break;

                case RelationKind.OnTop:
                    if (def.size.x <= a.Width + 0.05f) yield return PlacementStrategies.OnTop(a, def);
                    break;

                case RelationKind.Above:
                    yield return PlacementStrategies.Above(a, def);
                    break;

                case RelationKind.Under:
                    yield return Placement.Create(def, a.center, a.yaw);
                    yield return Placement.Create(def, a.center, a.yaw + 90f);
                    break;

                case RelationKind.ScatterAround:
                {
                    // strewn around the anchor's footprint: random direction, min..max beyond its edge
                    float reach = Mathf.Max(a.body.width, a.body.height) * 0.5f;
                    for (int n = 0; n < 32; n++)
                    {
                        float ang = (float)ctx.rng.NextDouble() * Mathf.PI * 2f;
                        float d = reach + Mathf.Lerp(entry.minDistance, entry.maxDistance, (float)ctx.rng.NextDouble());
                        Vector2 c = a.center + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * d;
                        yield return Placement.Create(def, c, 0f);
                    }

                    break;
                }
            }
        }
    }
}
