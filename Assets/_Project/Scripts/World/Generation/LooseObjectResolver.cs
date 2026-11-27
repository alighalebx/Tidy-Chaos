using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Keeps hand-placed loose objects (clutter, floor hiding spots) valid after furniture moved
    /// (objects that followed their furniture via PropAnchoring are skipped):
    ///  - inside a new piece: set on top only if it is a real surface (table, desk, counter), else nearest free floor
    ///  - floating (its support moved away): dropped onto whatever is below
    ///  - on the floor in a doorway, a reserved zone or in front of furniture: nearest free floor
    /// Objects are processed bottom-up so stacks settle onto what's under them.
    /// </summary>
    public static class LooseObjectResolver
    {
        public const float MaxSurface = 1.1f; // above the floor top
        private const Cell Blocking = Cell.Low | Cell.Door | Cell.Reserved | Cell.Clear;

        public static int Resolve(IEnumerable<Transform> roots, Dictionary<Room, RoomGrid> grids, HousePlan plan, ICollection<Transform> skip = null)
        {
            var items = new List<Transform>();
            foreach (Transform root in roots)
            {
                if (root == null) continue;
                foreach (Transform t in root)
                {
                    if (t.gameObject.activeInHierarchy && (skip == null || !skip.Contains(t))) items.Add(t);
                }
            }

            items.Sort((a, b) => a.position.y.CompareTo(b.position.y));
            int moved = 0;
            foreach (Transform t in items)
            {
                if (ResolveOne(t, grids, plan)) moved++;
            }

            Physics.SyncTransforms();
            return moved;
        }

        private static bool ResolveOne(Transform t, Dictionary<Room, RoomGrid> grids, HousePlan plan)
        {
            Vector3 p = t.position;
            Room room = HouseShell.RoomAt(p);
            float off = t.GetComponent<HidingSpot>() != null ? 0.01f : 0.02f;
            Physics.SyncTransforms();

            grids.TryGetValue(room, out RoomGrid g);
            Placement host = HostAt(plan, room, p);
            if (host != null)
            {
                float top = HouseShell.FloorTop + host.baseY + host.def.size.y;
                if (host.def.isSurface && top - HouseShell.FloorTop <= MaxSurface && Surface(t, new Vector3(p.x, top + 0.3f, p.z), true, out float y))
                {
                    return MoveTo(t, new Vector3(p.x, y + off, p.z));
                }

                return ToFreeFloor(t, g, p, off);
            }

            if (Surface(t, p + Vector3.up * 0.05f, false, out float below) && below < p.y - 0.06f)
            {
                MoveTo(t, new Vector3(p.x, below + off, p.z));
                p = t.position;
            }

            // on the floor, but in the way: doorway, hazard, spawn, or in front of a drawer/door/seat
            bool onFloor = p.y < HouseShell.FloorTop + 0.2f;
            if (onFloor && g != null && g.WorldToCell(new Vector2(p.x, p.z), out int i, out int j) && (g[i, j] & Blocking) != 0)
            {
                return ToFreeFloor(t, g, p, off);
            }

            return false;
        }

        private static bool ToFreeFloor(Transform t, RoomGrid g, Vector3 p, float off)
        {
            return g != null && NearestFree(g, new Vector2(p.x, p.z), out Vector2 c) && MoveTo(t, new Vector3(c.x, HouseShell.FloorTop + off, c.y));
        }

        /// A generated low piece whose footprint contains p and whose top is above p.
        private static Placement HostAt(HousePlan plan, Room room, Vector3 p)
        {
            foreach (RoomPlan rp in plan.rooms)
            {
                if (rp.room != room) continue;
                foreach (Placement pl in rp.placements)
                {
                    if (!pl.low || pl.rug) continue;
                    float top = HouseShell.FloorTop + pl.baseY + pl.def.size.y;
                    if (top > p.y + 0.02f && pl.body.Contains(new Vector2(p.x, p.z))) return pl;
                }
            }

            return null;
        }

        private static bool Surface(Transform self, Vector3 from, bool ignoreClutter, out float y)
        {
            y = HouseShell.FloorTop;
            float best = float.MaxValue;
            bool any = false;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, from.y + 1f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.transform.IsChildOf(self)) continue;
                if (ignoreClutter && h.collider.GetComponentInParent<PhysicsClutter>() != null) continue;
                if (h.collider.GetComponentInParent<WheresMyLeftSock.Player.PlayerController>() != null) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    y = h.point.y;
                    any = true;
                }
            }

            return any;
        }

        /// Nearest cell that is free, not a doorway/reserved zone/furniture front, and has 0.3m of room around it.
        private static bool NearestFree(RoomGrid g, Vector2 from, out Vector2 result)
        {
            result = from;
            if (!g.WorldToCell(from, out int si, out int sj))
            {
                si = Mathf.Clamp(si, 0, g.nx - 1);
                sj = Mathf.Clamp(sj, 0, g.nz - 1);
            }

            var seen = new bool[g.nx * g.nz];
            var q = new Queue<int>();
            q.Enqueue(sj * g.nx + si);
            seen[sj * g.nx + si] = true;
            while (q.Count > 0)
            {
                int idx = q.Dequeue();
                int i = idx % g.nx, j = idx / g.nx;
                Vector2 c = g.CellCenter(i, j);
                Rect around = new Rect(c.x - 0.3f, c.y - 0.3f, 0.6f, 0.6f);
                bool inside = around.xMin > g.area.xMin + 0.05f && around.xMax < g.area.xMax - 0.05f &&
                              around.yMin > g.area.yMin + 0.05f && around.yMax < g.area.yMax - 0.05f;
                if (inside && !g.Any(around, Blocking))
                {
                    result = c;
                    return true;
                }

                for (int d = 0; d < 4; d++)
                {
                    int a = i + (d == 0 ? 1 : d == 1 ? -1 : 0);
                    int b = j + (d == 2 ? 1 : d == 3 ? -1 : 0);
                    if (!g.InBounds(a, b)) continue;
                    int n = b * g.nx + a;
                    if (seen[n]) continue;
                    seen[n] = true;
                    q.Enqueue(n);
                }
            }

            return false;
        }

        private static bool MoveTo(Transform t, Vector3 pos)
        {
            if ((t.position - pos).sqrMagnitude < 0.0004f) return false;
            t.position = pos;
            var rb = t.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.position = pos;
                if (!rb.isKinematic)
                {
                    rb.linearVelocity = Vector3.zero;
                    rb.angularVelocity = Vector3.zero;
                }
            }

            Physics.SyncTransforms();
            return true;
        }
    }
}
