using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Props follow their furniture. The scene is saved with the authored furniture layout (the one the props
    /// were dressed around). Before a new layout is generated, every loose object remembers the piece it stands
    /// on (laptop on the desk, pillows on the bed) or right next to (desk chair, dining chairs) in that piece's
    /// local space; afterwards it is put back relative to the matching new piece (same def, same room, same order).
    /// A floor prop whose new spot is blocked is left to LooseObjectResolver.
    /// </summary>
    public sealed class PropAnchoring
    {
        public const float NearDistance = 0.6f;

        private struct Link
        {
            public Transform obj;
            public string defId;
            public Room room;
            public int ordinal;
            public Vector3 localPos;
            public Quaternion localRot;
            public bool onTop;
        }

        private readonly List<Link> _links = new List<Link>();
        public readonly HashSet<Transform> Linked = new HashSet<Transform>();
        public int Captured => _links.Count;

        public static PropAnchoring Capture(Transform output, IEnumerable<Transform> looseRoots, IEnumerable<Transform> pinnedRoots)
        {
            var result = new PropAnchoring();
            if (output == null) return result;
            Physics.SyncTransforms();

            List<(PlacedFurniture pf, Rect rect, int ordinal)> pieces = Index(output);
            var pinned = new List<Rect>();
            foreach (Transform root in pinnedRoots)
            {
                if (root == null) continue;
                foreach (Transform t in root)
                {
                    if (t.gameObject.activeInHierarchy && TryRect(t, out Rect r, out float h) && h > 0.06f) pinned.Add(r);
                }
            }

            foreach (Transform root in looseRoots)
            {
                if (root == null) continue;
                foreach (Transform obj in root)
                {
                    if (!obj.gameObject.activeInHierarchy) continue;
                    Vector3 p = obj.position;
                    var xz = new Vector2(p.x, p.z);
                    Room room = HouseShell.RoomAt(p);
                    int hostIdx = -1;
                    bool onTop = false;

                    if (p.y > HouseShell.FloorTop + 0.12f && SupportUnder(obj, out PlacedFurniture support))
                    {
                        hostIdx = pieces.FindIndex(x => x.pf == support);
                        onTop = hostIdx >= 0;
                    }

                    if (hostIdx < 0)
                    {
                        float best = NearDistance;
                        for (int i = 0; i < pieces.Count; i++)
                        {
                            if (pieces[i].pf.room != room || pieces[i].pf.def == null || pieces[i].pf.def.isRug) continue;
                            float d = Distance(pieces[i].rect, xz);
                            if (d < best)
                            {
                                best = d;
                                hostIdx = i;
                            }
                        }

                        // closer to an authored (not generated) piece? then it belongs to that one - leave it alone
                        if (hostIdx >= 0)
                        {
                            foreach (Rect r in pinned)
                            {
                                if (Distance(r, xz) < best) { hostIdx = -1; break; }
                            }
                        }
                    }

                    if (hostIdx < 0) continue;
                    PlacedFurniture host = pieces[hostIdx].pf;
                    result._links.Add(new Link
                    {
                        obj = obj,
                        defId = host.def.id,
                        room = host.room,
                        ordinal = pieces[hostIdx].ordinal,
                        localPos = host.transform.InverseTransformPoint(p),
                        localRot = Quaternion.Inverse(host.transform.rotation) * obj.rotation,
                        onTop = onTop
                    });
                }
            }

            return result;
        }

        /// Re-attach captured props to the new furniture. Returns how many were moved with their piece.
        public int Apply(Transform output, Dictionary<Room, RoomGrid> grids)
        {
            Physics.SyncTransforms();
            List<(PlacedFurniture pf, Rect rect, int ordinal)> pieces = Index(output);
            int moved = 0;
            foreach (Link l in _links)
            {
                if (l.obj == null) continue;
                int idx = pieces.FindIndex(x => x.pf.room == l.room && x.pf.def != null && x.pf.def.id == l.defId && x.ordinal == l.ordinal);
                if (idx < 0) idx = pieces.FindIndex(x => x.pf.room == l.room && x.pf.def != null && x.pf.def.id == l.defId);
                if (idx < 0) continue; // the piece isn't in this layout - LooseObjectResolver takes over

                Transform host = pieces[idx].pf.transform;
                Vector3 target = host.TransformPoint(l.localPos);
                Quaternion rot = host.rotation * l.localRot;

                if (!l.onTop)
                {
                    RoomArea area = HouseShell.Get(l.room);
                    var xz = new Vector2(target.x, target.z);
                    if (!Connectivity.Grow(area.rect, -0.15f).Contains(xz)) continue;
                    if (grids.TryGetValue(l.room, out RoomGrid g) && g.WorldToCell(xz, out int i, out int j) &&
                        (g[i, j] & (Cell.Low | Cell.Door | Cell.Reserved)) != 0) continue;
                }

                l.obj.SetPositionAndRotation(target, rot);
                var rb = l.obj.GetComponent<Rigidbody>();
                if (rb != null)
                {
                    rb.position = target;
                    rb.rotation = rot;
                    if (!rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                    }
                }

                Linked.Add(l.obj);
                moved++;
            }

            Physics.SyncTransforms();
            return moved;
        }

        private static List<(PlacedFurniture, Rect, int)> Index(Transform output)
        {
            var list = new List<(PlacedFurniture, Rect, int)>();
            var counts = new Dictionary<(Room, string), int>();
            foreach (Transform t in output)
            {
                if (!t.gameObject.activeSelf) continue;
                var pf = t.GetComponent<PlacedFurniture>();
                if (pf == null || pf.def == null) continue;
                counts.TryGetValue((pf.room, pf.def.id), out int n);
                counts[(pf.room, pf.def.id)] = n + 1;
                TryRect(t, out Rect r, out _);
                list.Add((pf, r, n));
            }

            return list;
        }

        /// First non-clutter collider straight below the object.
        private static bool SupportUnder(Transform obj, out PlacedFurniture support)
        {
            support = null;
            RaycastHit[] hits = Physics.RaycastAll(obj.position + Vector3.up * 0.05f, Vector3.down, 2.5f, ~0, QueryTriggerInteraction.Ignore);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(obj)) continue;
                if (h.collider.GetComponentInParent<PhysicsClutter>() != null) continue; // look through stacks
                support = h.collider.GetComponentInParent<PlacedFurniture>();
                return support != null;
            }

            return false;
        }

        private static bool TryRect(Transform t, out Rect rect, out float height)
        {
            rect = default;
            height = 0f;
            bool has = false;
            Bounds b = default;
            foreach (Renderer r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }

            if (!has) return false;
            rect = Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
            height = b.size.y;
            return true;
        }

        private static float Distance(Rect r, Vector2 p)
        {
            float dx = Mathf.Max(r.xMin - p.x, 0f, p.x - r.xMax);
            float dz = Mathf.Max(r.yMin - p.y, 0f, p.y - r.yMax);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }
    }
}
