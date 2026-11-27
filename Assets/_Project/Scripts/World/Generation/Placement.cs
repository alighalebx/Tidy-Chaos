using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// One planned piece: where it stands (XZ center + yaw in 90 degree steps), its footprint and front clearance.
    /// Pure data - no GameObjects - so plans can be generated, tested and stress-tested without a scene.
    /// </summary>
    public sealed class Placement
    {
        public string key;
        public RecipeEntry entry;
        public FurnitureDef def;
        public Vector2 center;          // world XZ
        public float yaw;               // 0 = front faces +z, 90 = +x ...
        public float baseY;             // bottom of the piece above the floor top
        public Rect body;               // world XZ footprint
        public Rect clearance;          // world XZ free area in front (if hasClearance)
        public bool hasClearance;
        public bool low, high, rug, onTop, requiresWall;
        public bool loose;              // pushable prop: marks Cell.Prop, never blocks walking
        public Placement anchor;        // the placement this one was positioned from (null = standalone)
        public bool noSpots;            // hiding spots disabled (e.g. a blind kitchen corner)
        public bool interactable;       // needs a reachable front (drawers, doors, seats, hiding spots)
        public bool optional;           // composite parts that may be dropped if they break a rule (uppers, hood)
        public Rect[] sideStrips;       // left/right access strips (beds), null when the def needs none
        public float bias;              // strategy-provided score (e.g. how centered a relation is)

        public Vector2 Forward => Fwd(yaw);
        public Vector2 Right => new Vector2(Forward.y, -Forward.x);
        public float Width => def.size.x;
        public float Depth => def.size.z;

        public static Vector2 Fwd(float yaw)
        {
            float r = yaw * Mathf.Deg2Rad;
            return new Vector2(Mathf.Round(Mathf.Sin(r) * 1000f) / 1000f, Mathf.Round(Mathf.Cos(r) * 1000f) / 1000f);
        }

        public static float NormalizeYaw(float yaw)
        {
            yaw = Mathf.Round(yaw / 90f) * 90f % 360f;
            return yaw < 0f ? yaw + 360f : yaw;
        }

        public static Placement Create(FurnitureDef def, Vector2 center, float yaw, float baseY = -1f, bool onTop = false)
        {
            yaw = NormalizeYaw(yaw);
            var p = new Placement
            {
                def = def,
                center = center,
                yaw = yaw,
                baseY = baseY >= 0f ? baseY : def.baseHeight,
                rug = def.isRug,
                onTop = onTop
            };

            bool rotated = Mathf.Approximately(yaw, 90f) || Mathf.Approximately(yaw, 270f);
            float sx = rotated ? def.size.z : def.size.x;
            float sz = rotated ? def.size.x : def.size.z;
            if (def.isLoose)
            {
                // loose props get a random spin when spawned: reserve the square that covers any rotation
                float s = Mathf.Max(def.size.x, def.size.z);
                sx = sz = s;
            }

            p.body = RectFromCenter(center, sx, sz);

            float top = p.baseY + def.size.y + def.stackHeight;
            p.loose = def.isLoose;
            p.low = !p.rug && !p.loose && !onTop && p.baseY < HouseShell.LowBandTop;
            p.high = !p.rug && !p.loose && top > HouseShell.LowBandTop;

            if (def.frontClearance > 0.01f && !p.rug && !p.loose)
            {
                p.hasClearance = true;
                Vector2 f = p.Forward;
                Vector2 c = center + f * (def.size.z * 0.5f + def.frontClearance * 0.5f);
                // a little narrower than the piece so neighbours in a run (or an L corner) don't trip it
                float cw = Mathf.Max(0.2f, def.size.x - 0.12f);
                float cx = rotated ? def.frontClearance : cw;
                float cz = rotated ? cw : def.frontClearance;
                p.clearance = RectFromCenter(c, cx, cz);
            }

            if (def.sideAccess > 0.01f && !p.rug && !onTop)
            {
                // strips along both long sides, leaving the head end (where nightstands go) out
                const float headSkip = 0.6f;
                float along = Mathf.Max(0.3f, def.size.z - headSkip);
                Vector2 f = p.Forward;
                Vector2 r = p.Right;
                p.sideStrips = new Rect[2];
                for (int s = 0; s < 2; s++)
                {
                    float sign = s == 0 ? -1f : 1f;
                    Vector2 c = center + r * sign * (def.size.x * 0.5f + def.sideAccess * 0.5f) + f * (headSkip * 0.5f);
                    p.sideStrips[s] = rotated ? RectFromCenter(c, along, def.sideAccess) : RectFromCenter(c, def.sideAccess, along);
                }
            }

            return p;
        }

        public static Rect RectFromCenter(Vector2 c, float sx, float sz) => new Rect(c.x - sx * 0.5f, c.y - sz * 0.5f, sx, sz);

        /// World position of the piece's bottom-center.
        public Vector3 WorldPosition => new Vector3(center.x, HouseShell.FloorTop + baseY, center.y);

        public Quaternion WorldRotation => Quaternion.Euler(0f, yaw, 0f);

        public override string ToString() => $"{key}:{(def != null ? def.id : "?")} @({center.x:F2},{center.y:F2}) yaw {yaw:F0}";
    }

    /// Something already standing in the room that the planner must respect (authored furniture, hazards, spawn).
    public struct Obstacle
    {
        public string name;
        public Rect rect;
        public bool low, high, reserved;
        public bool hasClearance;
        public Rect clearance;
        public bool accessTarget;  // must stay reachable (authored cabinets, the player spawn)
    }

    public sealed class RoomPlan
    {
        public Room room;
        public readonly List<Placement> placements = new List<Placement>();
        public int attempts;
        public bool usedFallback;
        public string failure; // last reason an attempt failed (diagnostics)
    }

    public sealed class HousePlan
    {
        public int seed;
        public readonly List<RoomPlan> rooms = new List<RoomPlan>();
        public readonly Dictionary<string, int> rejections = new Dictionary<string, int>();

        public void Reject(string rule)
        {
            rejections.TryGetValue(rule, out int n);
            rejections[rule] = n + 1;
        }
    }
}
