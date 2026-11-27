using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Turns what is already in the scene into planner obstacles:
    ///  - pinned roots: authored furniture of levels that aren't generated yet (bodies + door/drawer fronts)
    ///  - reserved roots: hazards (every collider, triggers included)
    ///  - the player spawn (reserved, and must stay reachable)
    /// </summary>
    public static class SceneObstacles
    {
        public const float PinnedFrontClearance = 0.6f;

        public static Dictionary<Room, List<Obstacle>> Collect(IEnumerable<Transform> pinnedRoots, IEnumerable<Transform> reservedRoots,
            Vector3? spawn, float spawnRadius)
        {
            var map = new Dictionary<Room, List<Obstacle>>();
            foreach (RoomArea a in HouseShell.Rooms) map[a.room] = new List<Obstacle>();

            if (pinnedRoots != null)
            {
                foreach (Transform root in pinnedRoots)
                {
                    if (root == null) continue;
                    foreach (Transform piece in root)
                    {
                        if (!piece.gameObject.activeInHierarchy) continue;
                        if (TryPinned(piece, out Obstacle o)) Add(map, o);
                    }
                }
            }

            if (reservedRoots != null)
            {
                foreach (Transform root in reservedRoots)
                {
                    if (root == null) continue;
                    foreach (Collider c in root.GetComponentsInChildren<Collider>())
                    {
                        Bounds b = c.bounds;
                        Add(map, new Obstacle
                        {
                            name = c.name,
                            rect = Rect.MinMaxRect(b.min.x - 0.1f, b.min.z - 0.1f, b.max.x + 0.1f, b.max.z + 0.1f),
                            reserved = true
                        });
                    }
                }
            }

            if (spawn.HasValue)
            {
                Vector3 s = spawn.Value;
                Add(map, new Obstacle
                {
                    name = "player spawn",
                    rect = new Rect(s.x - spawnRadius, s.z - spawnRadius, spawnRadius * 2f, spawnRadius * 2f),
                    reserved = true,
                    accessTarget = true
                });
            }

            return map;
        }

        private static void Add(Dictionary<Room, List<Obstacle>> map, Obstacle o)
        {
            Room room = HouseShell.RoomAt(new Vector3(o.rect.center.x, 0f, o.rect.center.y));
            if (map.TryGetValue(room, out List<Obstacle> list)) list.Add(o);
        }

        private static bool TryPinned(Transform piece, out Obstacle o)
        {
            o = default;
            bool has = false;
            Bounds b = default;
            foreach (Renderer r in piece.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }

            if (!has || b.size.y < 0.06f) return false; // nothing, or a rug

            float split = HouseShell.FloorTop + HouseShell.LowBandTop;
            bool thinWallDecor = Mathf.Min(b.size.x, b.size.z) < 0.15f && b.min.y > HouseShell.FloorTop + 0.3f;
            o.name = piece.name;
            o.rect = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            o.low = b.min.y < split && !thinWallDecor;
            o.high = b.max.y > split;

            bool openable = piece.GetComponentInChildren<OpenableFurniture>(true) != null;
            bool spots = piece.GetComponentInChildren<HidingSpot>(true) != null;
            o.accessTarget = openable || spots;

            if (openable)
            {
                Vector3 f = piece.forward;
                const float d = PinnedFrontClearance, m = 0.06f;
                o.hasClearance = true;
                if (Mathf.Abs(f.x) > Mathf.Abs(f.z))
                {
                    o.clearance = f.x > 0f
                        ? Rect.MinMaxRect(b.max.x, b.min.z + m, b.max.x + d, b.max.z - m)
                        : Rect.MinMaxRect(b.min.x - d, b.min.z + m, b.min.x, b.max.z - m);
                }
                else
                {
                    o.clearance = f.z > 0f
                        ? Rect.MinMaxRect(b.min.x + m, b.max.z, b.max.x - m, b.max.z + d)
                        : Rect.MinMaxRect(b.min.x + m, b.min.z - d, b.max.x - m, b.min.z);
                }
            }

            return true;
        }
    }
}
