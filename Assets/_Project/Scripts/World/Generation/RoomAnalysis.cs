using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// How a room is used, read from its architecture (computed once per room plan):
    ///  - main entrance: the opening that leads toward the front door; privacy grows away from it
    ///  - sightlines: what you see straight through each doorway when you walk in
    ///  - lanes: the walking routes between the room's openings (seating groups stay off them)
    /// </summary>
    public sealed class RoomAnalysis
    {
        public const float SightDepth = 4.0f;
        public const float LaneHalfWidth = 0.45f;

        public readonly List<WallSegment> openings = new List<WallSegment>();
        public readonly List<Rect> sightlines = new List<Rect>();
        public readonly List<(Vector2 a, Vector2 b)> lanes = new List<(Vector2, Vector2)>();
        public readonly Vector2 mainEntrance;
        public readonly float diagonal;

        public RoomAnalysis(PlanContext ctx)
        {
            RoomArea area = ctx.area;
            diagonal = new Vector2(area.rect.width, area.rect.height).magnitude;

            var points = new List<Vector2>();
            int bestDepth = int.MaxValue;
            foreach (WallSegment s in area.segments)
            {
                if (s.IsOpening)
                {
                    openings.Add(s);
                    // a little narrower than the clear span: what you actually see walking through
                    float depth = Mathf.Min(SightDepth, HouseShell.IsHorizontal(s.side) ? area.rect.height : area.rect.width);
                    sightlines.Add(ctx.Strip(s.side, s.line, s.clearFrom + 0.1f, s.clearTo - 0.1f, depth));
                    points.Add(HouseShell.OpeningCenter(s));

                    int d = HouseShell.OpeningDepth(s);
                    if (d < bestDepth)
                    {
                        bestDepth = d;
                        mainEntrance = HouseShell.OpeningCenter(s);
                    }
                }
            }

            // open sides (living <-> kitchen) count as one wide opening at their middle
            foreach (Side side in new[] { Side.South, Side.North, Side.West, Side.East })
            {
                if (!area.SideIsOpen(side)) continue;
                PlacementStrategies.SideRange(area, side, out float from, out float to);
                float line = PlacementStrategies.LineOf(area, side);
                float mid = (from + to) * 0.5f;
                Vector2 c = HouseShell.IsHorizontal(side) ? new Vector2(mid, line) : new Vector2(line, mid);
                points.Add(c);

                RoomArea beyond = null;
                foreach (WallSegment s in area.On(side))
                {
                    beyond = HouseShell.Beyond(s);
                    break;
                }

                int d = beyond != null ? HouseShell.Depth(beyond.room) : 99;
                if (d < bestDepth)
                {
                    bestDepth = d;
                    mainEntrance = c;
                }
            }

            for (int i = 0; i < points.Count; i++)
            {
                for (int j = i + 1; j < points.Count; j++) lanes.Add((points[i], points[j]));
            }

            if (bestDepth == int.MaxValue) mainEntrance = area.rect.center;
        }

        public bool InSightline(Rect body)
        {
            foreach (Rect s in sightlines)
            {
                if (PlacementRules.Overlaps(s, body, 0.02f)) return true;
            }

            return false;
        }

        public int LanesCrossed(Rect zone)
        {
            int n = 0;
            foreach (var (a, b) in lanes)
            {
                if (SegmentRectDistance(a, b, zone) < LaneHalfWidth) n++;
            }

            return n;
        }

        /// 0 at the main entrance .. 1 at the far side of the room.
        public float Remoteness(Vector2 p) => Mathf.Clamp01(Vector2.Distance(p, mainEntrance) / diagonal);

        private static float SegmentRectDistance(Vector2 a, Vector2 b, Rect r)
        {
            // sample the segment; rooms are small, 20 samples is plenty
            float best = float.MaxValue;
            for (int i = 0; i <= 20; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / 20f);
                float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
                float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
                best = Mathf.Min(best, Mathf.Sqrt(dx * dx + dz * dz));
            }

            return best;
        }
    }
}
