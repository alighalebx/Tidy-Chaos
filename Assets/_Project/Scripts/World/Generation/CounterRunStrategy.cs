using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Kitchen run rules (R6):
    ///  - a continuous run of base modules along one wall, starting in a corner when there is one
    ///  - optional L: the first module becomes a blind corner and a short leg runs along the other wall
    ///  - the fridge sits at an end (never in the blind corner), the stove is at least 2 modules from it,
    ///    the sink is never next to the stove when avoidable and likes windows
    ///  - hood above the stove, upper cabinets only above base modules (dropped where a window is),
    ///    microwave on top of a plain cabinet
    /// Output is a composite: all pieces are validated together and either all base modules fit or none.
    /// </summary>
    public sealed class CounterRunStrategy
    {
        public IEnumerable<List<Placement>> Runs(PlanContext ctx, RecipeEntry entry)
        {
            CounterRunSettings cs = entry.counter;
            if (cs == null || cs.fridge == null || cs.stove == null || cs.sink == null || cs.cabinets == null || cs.cabinets.Length == 0) yield break;

            foreach (Side side in new[] { Side.South, Side.North, Side.West, Side.East })
            {
                if (ctx.area.SideIsOpen(side)) continue;
                foreach ((float a, float b) in WallIntervals(ctx.area, side))
                {
                    PlacementStrategies.SideRange(ctx.area, side, out float sideFrom, out float sideTo);
                    foreach (int dir in new[] { 1, -1 })
                    {
                        float start = dir > 0 ? a : b;
                        bool atCorner = Mathf.Abs(start - (dir > 0 ? sideFrom : sideTo)) < 0.01f;
                        Side perp = Perpendicular(side, dir > 0);
                        float cornerCoord = CornerCoordOnPerp(ctx.area, side);
                        bool corner = atCorner && !ctx.area.SideIsOpen(perp) &&
                                      PlacementScorers.IsWallAt(ctx.area, perp, cornerCoord + Mathf.Sign(InwardAlong(side)) * 0.5f);

                        float avail = (b - a) - HouseShell.WallInset - PlanContext.WallMargin;
                        int fit = Mathf.FloorToInt((avail - (cs.fridge.size.x - cs.cabinets[0].size.x)) / cs.cabinets[0].size.x + 0.001f);
                        int hi = Mathf.Min(cs.maxModules, fit);
                        if (hi < cs.minModules) continue;

                        for (int k = 0; k < 3; k++)
                        {
                            int n = ctx.rng.Next(cs.minModules, hi + 1);
                            bool l = corner && ctx.rng.NextDouble() < cs.lShapeChance;
                            List<Placement> run = Build(ctx, entry, cs, side, start, dir, n, l, perp);
                            if (run != null) yield return run;
                        }
                    }
                }
            }
        }

        private List<Placement> Build(PlanContext ctx, RecipeEntry entry, CounterRunSettings cs, Side side, float start, int dir, int n, bool lShape, Side perp)
        {
            var slots = new FurnitureDef[n];

            // fridge at the end of the run nearest the kitchen's entrance (you grab milk on the way in)
            float runLength = n * cs.cabinets[0].size.x;
            Vector2 startPt = PointOn(ctx.area, side, start);
            Vector2 endPt = PointOn(ctx.area, side, start + dir * runLength);
            bool endNearer = Vector2.Distance(endPt, ctx.analysis.mainEntrance) <= Vector2.Distance(startPt, ctx.analysis.mainEntrance);
            int fridgeIdx = lShape ? n - 1 : (endNearer ? n - 1 : 0);
            float fridgeBias = lShape && !endNearer ? -1f : 0.5f;
            slots[fridgeIdx] = cs.fridge;
            int first = lShape ? 1 : 0;

            // stove: counter on both sides (never at a run end), at least 2 modules from the fridge
            var stoveOptions = new List<int>();
            for (int i = Mathf.Max(first, 1); i < n - 1; i++)
            {
                if (i != fridgeIdx && Mathf.Abs(i - fridgeIdx) >= 2) stoveOptions.Add(i);
            }

            if (stoveOptions.Count == 0) return null;
            int stoveIdx = stoveOptions[ctx.rng.Next(stoveOptions.Count)];
            slots[stoveIdx] = cs.stove;

            bool sinkOnLeg = lShape && ctx.rng.NextDouble() < 0.3;
            int sinkIdx = -1;
            if (!sinkOnLeg)
            {
                var good = new List<int>();
                var ok = new List<int>();
                for (int i = first; i < n; i++)
                {
                    if (slots[i] != null) continue;
                    bool atEnd = i == 0 || i == n - 1;
                    (Mathf.Abs(i - stoveIdx) >= 2 && !atEnd ? good : ok).Add(i);
                }

                List<int> pool = good.Count > 0 ? good : ok;
                if (pool.Count == 0) return null;
                sinkIdx = pool[ctx.rng.Next(pool.Count)];
                slots[sinkIdx] = cs.sink;
            }

            for (int i = 0; i < n; i++)
            {
                if (slots[i] == null) slots[i] = cs.cabinets[ctx.rng.Next(cs.cabinets.Length)];
            }

            var bases = new List<Placement>();
            float cursor = start + dir * HouseShell.WallInset;
            for (int i = 0; i < n; i++)
            {
                FurnitureDef d = slots[i];
                Placement p = PlacementStrategies.AgainstWall(d, ctx.area, side, cursor + dir * d.size.x * 0.5f);
                cursor += dir * d.size.x;
                p.key = entry.key;
                p.entry = entry;
                p.interactable = true;
                if (lShape && i == 0)
                {
                    p.hasClearance = false; // blind corner: the leg covers its front
                    p.interactable = false;
                    p.noSpots = true;
                }

                bases.Add(p);
            }

            Placement stove = bases[stoveIdx];
            Placement sink = sinkIdx >= 0 ? bases[sinkIdx] : null;

            if (lShape)
            {
                int m = ctx.rng.Next(1, cs.legMax + 1);
                float cornerDepth = slots[0].size.z;
                float legStart = CornerLineOnPerp(ctx.area, side) + InwardAlong(side) * (HouseShell.WallInset + cornerDepth);
                float legDir = InwardAlong(side);
                float c2 = legStart;
                for (int j = 0; j < m; j++)
                {
                    FurnitureDef d = sinkOnLeg && j == m - 1 ? cs.sink : cs.cabinets[ctx.rng.Next(cs.cabinets.Length)];
                    Placement p = PlacementStrategies.AgainstWall(d, ctx.area, perp, c2 + legDir * d.size.x * 0.5f);
                    c2 += legDir * d.size.x;
                    p.key = entry.key;
                    p.entry = entry;
                    p.interactable = true;
                    bases.Add(p);
                    if (d == cs.sink) sink = p;
                }
            }

            if (sink == null) return null;

            var run = new List<Placement>(bases);

            // hood above the stove - required, so a stove never ends up under a window
            if (cs.hood != null)
            {
                Placement hood = Child(PlacementStrategies.Above(stove, cs.hood), stove, entry);
                hood.optional = false;
                run.Add(hood);
            }

            // uppers above plain modules (never above the fridge or stove)
            if (cs.uppers != null && cs.uppers.Length > 0)
            {
                foreach (Placement b in bases)
                {
                    if (b.def == cs.fridge || b.def == cs.stove) continue;
                    if (ctx.rng.NextDouble() >= cs.upperChance) continue;
                    FurnitureDef u = cs.uppers[ctx.rng.Next(cs.uppers.Length)];
                    run.Add(Child(PlacementStrategies.Above(b, u), b, entry));
                }
            }

            // microwave on a plain cabinet
            if (cs.microwave != null && ctx.rng.NextDouble() < cs.microwaveChance)
            {
                var plain = bases.FindAll(b => b.def != cs.fridge && b.def != cs.stove && b.def != cs.sink && !b.noSpots);
                if (plain.Count > 0)
                {
                    Placement host = plain[ctx.rng.Next(plain.Count)];
                    run.Add(Child(PlacementStrategies.OnTop(host, cs.microwave), host, entry));
                }
            }

            // composite preference: sink under a window, longer runs, the occasional L
            float bias = 0.08f * bases.Count + (lShape ? 0.3f : 0f) + fridgeBias;
            if (SinkUnderWindow(ctx, sink)) bias += 1.5f;
            run[0].bias = bias;
            return run;
        }

        private static Placement Child(Placement p, Placement anchor, RecipeEntry entry)
        {
            p.anchor = anchor;
            p.key = entry.key;
            p.entry = entry;
            p.optional = true;
            p.interactable = false;
            return p;
        }

        private static bool SinkUnderWindow(PlanContext ctx, Placement sink)
        {
            if (!ctx.AgainstWall(sink, out Side side)) return false;
            bool h = HouseShell.IsHorizontal(side);
            float a = h ? sink.body.xMin : sink.body.yMin;
            float b = h ? sink.body.xMax : sink.body.yMax;
            foreach (WallSegment s in ctx.SegmentsBehind(sink, side))
            {
                if (s.kind == EdgeKind.Window && Mathf.Min(b, s.clearTo) - Mathf.Max(a, s.clearFrom) > 0.3f) return true;
            }

            return false;
        }

        private static Vector2 PointOn(RoomArea area, Side side, float along)
        {
            float line = PlacementStrategies.LineOf(area, side);
            return HouseShell.IsHorizontal(side) ? new Vector2(along, line) : new Vector2(line, along);
        }

        /// Contiguous stretches of real wall (solid or window) on a side.
        private static IEnumerable<(float, float)> WallIntervals(RoomArea area, Side side)
        {
            var segs = new List<WallSegment>(area.On(side));
            segs.Sort((x, y) => x.from.CompareTo(y.from));
            float start = float.NaN, end = float.NaN;
            foreach (WallSegment s in segs)
            {
                if (s.IsWall)
                {
                    if (float.IsNaN(start)) start = s.from;
                    end = s.to;
                }
                else if (!float.IsNaN(start))
                {
                    yield return (start, end);
                    start = float.NaN;
                }
            }

            if (!float.IsNaN(start)) yield return (start, end);
        }

        /// The side met at the start corner: walking +dir along a horizontal side starts at the West end.
        private static Side Perpendicular(Side side, bool startAtFrom)
        {
            if (HouseShell.IsHorizontal(side)) return startAtFrom ? Side.West : Side.East;
            return startAtFrom ? Side.South : Side.North;
        }

        /// Coordinate (along the perpendicular side) of the corner shared with 'side'.
        private static float CornerCoordOnPerp(RoomArea area, Side side) => CornerLineOnPerp(area, side);

        private static float CornerLineOnPerp(RoomArea area, Side side) => PlacementStrategies.LineOf(area, side);

        /// +1/-1: moving away from 'side' into the room along the perpendicular axis.
        private static float InwardAlong(Side side)
        {
            Vector2 n = HouseShell.Inward(side);
            return HouseShell.IsHorizontal(side) ? n.y : n.x;
        }
    }
}
