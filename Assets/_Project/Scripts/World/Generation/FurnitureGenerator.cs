using System;
using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;
using WheresMyLeftSock.Player;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Furnishes the house on scene load (every run - Restart reloads the scene, so every run is a new layout).
    /// Runs before everything else so SpawnManager / PetAI find the generated hiding spots.
    ///
    /// Pipeline: collect obstacles -> plan each room (RoomPlanner) -> fallback for rooms that fail ->
    ///           instantiate prefabs -> validate + cap hiding spots -> re-seat loose objects.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public class FurnitureGenerator : MonoBehaviour
    {
        [Serializable]
        public class FallbackPiece
        {
            public Room room;
            public FurnitureDef def;
            public Vector2 center;
            public float yaw;
            public float baseY = -1f;
        }

        [Serializable]
        public class SpotCap
        {
            public ConcealmentType type;
            [Min(0)] public int maxPerRoom = 2;
        }

        [Header("What to place")]
        [SerializeField] private List<RoomRecipe> recipes = new List<RoomRecipe>();
        [Tooltip("Highest generation level that is generated. Lower ones are authored (pinned) for now.")]
        [SerializeField] private GenLayer maxLayer = GenLayer.Base;

        [Header("Seed")]
        [Tooltip("-1 = new layout every run. Set a number to replay a layout (it is logged every run).")]
        [SerializeField] private int seed = -1;
        [SerializeField] private bool generateOnPlay = true;
        [SerializeField, Min(1)] private int maxAttemptsPerRoom = 40;
        [SerializeField] private float playerRadius = 0.3f;

        [Header("Scene")]
        [SerializeField] private Transform output;
        [Tooltip("Authored furniture that is not generated yet - treated as fixed obstacles.")]
        [SerializeField] private List<Transform> pinnedRoots = new List<Transform>();
        [Tooltip("Clutter and free hiding spots - re-seated if furniture lands on them.")]
        [SerializeField] private List<Transform> looseRoots = new List<Transform>();
        [Tooltip("Hazards - their colliders are kept free of furniture.")]
        [SerializeField] private List<Transform> reservedRoots = new List<Transform>();
        [SerializeField] private float spawnReserveRadius = 0.7f;

        [Header("Hiding spots on generated furniture (per room)")]
        [SerializeField] private List<SpotCap> spotCaps = new List<SpotCap>
        {
            new SpotCap { type = ConcealmentType.InsideDrawer, maxPerRoom = 4 },
            new SpotCap { type = ConcealmentType.HighShelf, maxPerRoom = 2 },
            new SpotCap { type = ConcealmentType.UnderClutter, maxPerRoom = 3 },
            new SpotCap { type = ConcealmentType.FloorHidden, maxPerRoom = 2 },
            new SpotCap { type = ConcealmentType.ObviousDecoySpot, maxPerRoom = 2 }
        };

        [Header("Mess")]
        [Tooltip("Chance each drawer / cabinet door starts yanked open (runtime only).")]
        [SerializeField, Range(0f, 1f)] private float openFurnitureChance = 0.2f;

        [Header("Authored layout (editor view + fallback if a room can't be planned)")]
        [SerializeField] private List<FallbackPiece> fallback = new List<FallbackPiece>();
        [SerializeField, HideInInspector] private bool outputIsAuthored;

        public static int LastSeed { get; private set; } = -1;
        public static event Action<FurnitureGenerator> Generated;

        public HousePlan LastPlan { get; private set; }
        public Transform Output => output;
        public List<RoomRecipe> Recipes => recipes;
        public GenLayer MaxLayer { get => maxLayer; set => maxLayer = value; }

        /// Editor hook so previews keep prefab links (set by the editor tools).
        public static Func<GameObject, Transform, GameObject> InstantiateOverride;

        private void Awake()
        {
            if (Application.isPlaying && generateOnPlay) Generate(seed >= 0 ? seed : NewSeed(), true);
        }

        public static int NewSeed() => (int)((DateTime.Now.Ticks ^ Environment.TickCount) & 0x7FFFFFFF) % 1000000;

        public void Configure(List<RoomRecipe> r, Transform outRoot, List<Transform> pinned, List<Transform> loose, List<Transform> reserved,
            List<FallbackPiece> fb)
        {
            recipes = r;
            output = outRoot;
            pinnedRoots = pinned;
            looseRoots = loose;
            reservedRoots = reserved;
            fallback = fb;
        }

        // ------------------------------------------------------------------ planning (no GameObjects)

        public Dictionary<Room, List<Obstacle>> CollectObstacles()
        {
            var pc = FindFirstObjectByType<PlayerController>();
            Vector3? spawn = pc != null ? pc.transform.position : (Vector3?)null;
            return SceneObstacles.Collect(pinnedRoots, reservedRoots, spawn, spawnReserveRadius);
        }

        public HousePlan Plan(int planSeed, Dictionary<Room, List<Obstacle>> obstacles, out Dictionary<Room, RoomGrid> grids)
        {
            var house = new HousePlan { seed = planSeed };
            grids = new Dictionary<Room, RoomGrid>();
            var counts = new Dictionary<string, int>();
            var planner = new RoomPlanner();

            foreach (RoomRecipe recipe in recipes)
            {
                if (recipe == null) continue;
                RoomArea area = HouseShell.Get(recipe.room);
                obstacles.TryGetValue(recipe.room, out List<Obstacle> obs);
                RoomPlan rp = planner.Plan(area, recipe, obs, counts, planSeed, maxAttemptsPerRoom, house, maxLayer, playerRadius, out RoomGrid grid);

                if (rp.placements.Count == 0)
                {
                    string failure = rp.failure;
                    int attempts = rp.attempts;
                    rp = FallbackPlan(area, obs, house, out grid);
                    rp.failure = failure;
                    rp.attempts = attempts;
                }

                house.rooms.Add(rp);
                grids[recipe.room] = grid;
            }

            return house;
        }

        /// The authored (hand-made) layout of one room - used as the fallback and as the editor layout.
        private RoomPlan FallbackPlan(RoomArea area, IList<Obstacle> obs, HousePlan house, out RoomGrid grid)
        {
            var rp = new RoomPlan { room = area.room, usedFallback = true };
            var ctx = new PlanContext(area, obs, new Dictionary<string, int>(), new System.Random(0), house, playerRadius);
            foreach (FallbackPiece f in fallback)
            {
                if (f.room != area.room || f.def == null) continue;
                Placement p = Placement.Create(f.def, f.center, f.yaw, f.baseY, f.baseY > 0.3f && !f.def.isRug && f.def.baseHeight < 0.01f);
                p.key = f.def.id;
                if (p.low) ctx.grid.Mark(p.body, Cell.Low);
                if (p.high) ctx.grid.Mark(p.body, Cell.High);
                if (p.hasClearance) ctx.grid.Mark(p.clearance, Cell.Clear);
                rp.placements.Add(p);
            }

            grid = ctx.grid;
            return rp;
        }

        // ------------------------------------------------------------------ building

        public bool OutputIsAuthored => outputIsAuthored;

        /// The hand-made layout the props were dressed around. The scene is saved with this one, so at runtime
        /// every prop can tell which piece it belongs to (see PropAnchoring).
        public HousePlan BuildAuthored()
        {
            ClearOutput();
            var house = new HousePlan { seed = -1 };
            var grids = new Dictionary<Room, RoomGrid>();
            Dictionary<Room, List<Obstacle>> obstacles = CollectObstacles();
            foreach (RoomRecipe recipe in recipes)
            {
                if (recipe == null) continue;
                obstacles.TryGetValue(recipe.room, out List<Obstacle> obs);
                house.rooms.Add(FallbackPlan(HouseShell.Get(recipe.room), obs, house, out RoomGrid g));
                grids[recipe.room] = g;
            }

            BuildPieces(house, grids, 0, out _, out _);
            outputIsAuthored = true;
            LastPlan = house;
            return house;
        }

        public HousePlan Generate(int genSeed, bool resolveLoose)
        {
            LastSeed = genSeed;
            var timer = System.Diagnostics.Stopwatch.StartNew();
            // props remember their furniture before it goes away (only meaningful on the authored layout)
            PropAnchoring anchors = resolveLoose && outputIsAuthored ? PropAnchoring.Capture(output, looseRoots, pinnedRoots) : null;

            ClearOutput();
            HousePlan house = Plan(genSeed, CollectObstacles(), out Dictionary<Room, RoomGrid> grids);
            BuildPieces(house, grids, genSeed, out int built, out int activeSpots);
            outputIsAuthored = false;

            int followed = anchors != null ? anchors.Apply(output, grids) : 0;
            int moved = resolveLoose ? LooseObjectResolver.Resolve(looseRoots, grids, house, anchors?.Linked) : 0;

            LastPlan = house;
            int fallbacks = house.rooms.FindAll(r => r.usedFallback).Count;
            Debug.Log($"[FurnitureGenerator] seed {genSeed} ({timer.ElapsedMilliseconds} ms): {built} pieces, {activeSpots} generated hiding spots, " +
                      $"{followed} props followed their furniture, {moved} loose objects re-seated" +
                      $"{(fallbacks > 0 ? $", {fallbacks} room(s) used the fallback layout" : "")}.");
            Generated?.Invoke(this);
            return house;
        }

        private void BuildPieces(HousePlan house, Dictionary<Room, RoomGrid> grids, int genSeed, out int pieces, out int activeSpots)
        {
            var built = new List<PlacedFurniture>();
            int spotCounter = 0;
            var propRng = new System.Random(RoomPlanner.SeedMix(genSeed, 0x9A0, 0x51));
            foreach (RoomPlan rp in house.rooms)
            {
                foreach (Placement p in rp.placements)
                {
                    if (p.def == null || p.def.prefab == null) continue;
                    GameObject go = InstantiateOverride != null ? InstantiateOverride(p.def.prefab, output) : Instantiate(p.def.prefab, output);
                    Vector3 pos = p.WorldPosition;
                    Quaternion rot = p.WorldRotation;
                    if (p.loose)
                    {
                        // loose clutter: any spin, dropped from just above the floor - some of it knocked over
                        rot = Quaternion.Euler(0f, (float)propRng.NextDouble() * 360f, 0f);
                        pos += Vector3.up * 0.02f;
                        if (propRng.NextDouble() < p.def.tipChance)
                        {
                            rot *= Tipped(propRng);
                            pos += Vector3.up * (Mathf.Max(p.def.size.x, p.def.size.y, p.def.size.z) * 0.5f + 0.05f); // settles under physics
                        }
                    }
                    else if (p.def.yawJitter > 0f && !p.requiresWall && !p.onTop)
                    {
                        // free-standing pieces and rugs a little crooked - nobody lines up a rug with a ruler
                        rot *= Quaternion.Euler(0f, ((float)propRng.NextDouble() * 2f - 1f) * p.def.yawJitter, 0f);
                    }

                    go.transform.SetPositionAndRotation(pos, rot);
                    go.name = $"{p.def.displayName} [{rp.room}]";
                    PlacedFurniture pf = go.GetComponent<PlacedFurniture>();
                    if (pf == null) pf = go.AddComponent<PlacedFurniture>();
                    if (p.loose && p.def.coverSpotChance > 0f && propRng.NextDouble() < p.def.coverSpotChance)
                    {
                        pf.spots = new[] { CoverSpot(p) };
                    }

                    pf.Bind(p.def, rp.room, p.key, !p.noSpots, ref spotCounter);
                    built.Add(pf);
                }
            }

            Physics.SyncTransforms();
            int disabled = ValidateSpots(built, grids, new System.Random(genSeed ^ 0x2545F491));

            var bundleRng = new System.Random(RoomPlanner.SeedMix(genSeed, 0xB0, 0x1D));
            foreach (PlacedFurniture pf in built) SpawnBundle(pf, grids, bundleRng);
            Physics.SyncTransforms();

            // morning chaos: a few drawers and cabinet doors already yanked open (runtime only - needs Awake)
            if (Application.isPlaying && openFurnitureChance > 0f)
            {
                var openRng = new System.Random(RoomPlanner.SeedMix(genSeed, 0x0DE, 0x77));
                foreach (OpenableFurniture of in output.GetComponentsInChildren<OpenableFurniture>())
                {
                    if (openRng.NextDouble() < openFurnitureChance) of.SetOpenInstant(true);
                }
            }

            pieces = built.Count;
            activeSpots = spotCounter - disabled;
        }

        /// Highest non-trigger surface below 'from' (loose props ignored, they can still be moving). Floor if nothing.
        private static float SurfaceBelow(Vector3 from)
        {
            float best = float.MaxValue, y = HouseShell.FloorTop;
            foreach (RaycastHit h in Physics.RaycastAll(from, Vector3.down, from.y + 1f, ~0, QueryTriggerInteraction.Ignore))
            {
                if (h.collider.GetComponentInParent<PhysicsClutter>() != null) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    y = h.point.y;
                }
            }

            return y;
        }

        /// Knocked over: on its side (either way), with a little lean.
        private static Quaternion Tipped(System.Random rng)
        {
            float side = rng.NextDouble() < 0.5 ? 90f : -90f;
            return Quaternion.Euler(side + ((float)rng.NextDouble() * 2f - 1f) * 12f, 0f, ((float)rng.NextDouble() * 2f - 1f) * 15f);
        }

        /// An "under clutter" spot on the floor beneath a loose prop. It is not parented to the prop: the prop gets
        /// shoved around, the spot (and the item hidden at it) stays put.
        private HidingSpot CoverSpot(Placement p)
        {
            var go = new GameObject("HS_UnderClutter");
            go.transform.SetParent(output, false);
            go.transform.position = new Vector3(p.center.x, HouseShell.FloorTop + 0.01f, p.center.y);
            var hs = go.AddComponent<HidingSpot>();
            hs.concealmentType = ConcealmentType.UnderClutter;
            return hs;
        }

        /// The piece's own props, placed with it as one unit. Floor props (chairs) are skipped when their
        /// spot is outside the room, in a doorway zone or inside another piece - a table against a wall
        /// simply gets no chair on that side.
        private void SpawnBundle(PlacedFurniture pf, IReadOnlyDictionary<Room, RoomGrid> grids, System.Random rng)
        {
            if (pf.def == null || pf.def.bundle == null) return;
            Transform t = pf.transform;
            grids.TryGetValue(pf.room, out RoomGrid g);
            RoomArea area = HouseShell.Get(pf.room);

            foreach (BundleItem item in pf.def.bundle)
            {
                if (item == null || item.options == null || item.options.Length == 0) continue;
                if (rng.NextDouble() >= item.chance) continue;
                GameObject prefab = item.options[rng.Next(item.options.Length)];
                if (prefab == null) continue;

                Vector3 local = item.localPosition;
                if (item.scatter > 0f)
                {
                    var off = new Vector3(((float)rng.NextDouble() * 2f - 1f) * item.scatter, 0f, ((float)rng.NextDouble() * 2f - 1f) * item.scatter);
                    var outward = new Vector3(local.x, 0f, local.z);
                    // floor props (chairs) only ever get pushed back, away from their piece - never into it
                    if (item.onFloor && outward.sqrMagnitude > 0.0001f && Vector3.Dot(off, outward) < 0f) off -= 2f * Vector3.Project(off, outward);
                    local += off;
                }

                Vector3 pos = t.TransformPoint(local) + Vector3.up * 0.01f;
                if (!item.onFloor && item.scatter > 0f)
                {
                    // slid around on top: sit on whatever is under the new spot (backrest, pillow, the floor past the edge)
                    pos.y = SurfaceBelow(new Vector3(pos.x, HouseShell.FloorTop + 2.0f, pos.z)) + 0.02f; // start below the ceiling
                }
                float yaw = item.localYaw + (float)(rng.NextDouble() * 2.0 - 1.0) * item.yawJitter;
                Quaternion rot = t.rotation * Quaternion.Euler(0f, yaw, 0f);
                if (rng.NextDouble() < item.tipChance)
                {
                    rot *= Tipped(rng);
                    pos += Vector3.up * 0.35f;
                }

                if (item.onFloor)
                {
                    var xz = new Vector2(pos.x, pos.z);
                    Rect around = new Rect(xz.x - 0.2f, xz.y - 0.2f, 0.4f, 0.4f);
                    bool inside = Connectivity.Grow(area.rect, -0.1f).Contains(around.min) && Connectivity.Grow(area.rect, -0.1f).Contains(around.max);
                    if (!inside) continue;
                    if (g != null && g.Any(around, Cell.Door | Cell.Reserved)) continue;
                    if (g != null && g.Any(around, Cell.Low) && !OnlyOwnBody(pf, around)) continue;
                }

                GameObject go = InstantiateOverride != null ? InstantiateOverride(prefab, output) : Instantiate(prefab, output);
                go.transform.SetPositionAndRotation(pos, rot);
                go.name = $"{prefab.name} ({pf.def.displayName})";
            }
        }

        /// The grid can't tell bodies apart: a chair tucked under its own table overlaps the table's cells.
        /// Accept it when nothing but the owner piece is there.
        private bool OnlyOwnBody(PlacedFurniture pf, Rect around)
        {
            Vector3 c = new Vector3(around.center.x, HouseShell.FloorTop + 0.4f, around.center.y);
            foreach (Collider col in Physics.OverlapBox(c, new Vector3(around.width * 0.5f, 0.3f, around.height * 0.5f), Quaternion.identity, ~0,
                         QueryTriggerInteraction.Ignore))
            {
                PlacedFurniture owner = col.GetComponentInParent<PlacedFurniture>();
                if (owner != null && owner != pf) return false; // another generated piece
                foreach (Transform root in pinnedRoots)
                {
                    if (root != null && col.transform.IsChildOf(root)) return false; // authored furniture
                }
            }

            return true;
        }

        public void ClearOutput()
        {
            if (output == null)
            {
                output = new GameObject("GeneratedFurniture").transform;
                output.SetParent(transform, false);
            }

            for (int i = output.childCount - 1; i >= 0; i--)
            {
                GameObject child = output.GetChild(i).gameObject;
                if (Application.isPlaying)
                {
                    child.SetActive(false); // invisible to FindObjectsByType right away
                    Destroy(child);
                }
                else
                {
                    DestroyImmediate(child);
                }
            }
        }

        /// Turn off spots that ended up outside the room / squeezed against a wall / under another piece,
        /// then cap each concealment type per room so generated furniture doesn't flood the spawn pool.
        private int ValidateSpots(List<PlacedFurniture> built, Dictionary<Room, RoomGrid> grids, System.Random rng)
        {
            int off = 0;
            var buckets = new Dictionary<(Room, ConcealmentType), List<HidingSpot>>();
            foreach (PlacedFurniture pf in built)
            {
                RoomArea area = HouseShell.Get(pf.room);
                Bounds own = OwnBounds(pf);
                foreach (HidingSpot s in pf.spots)
                {
                    if (s == null || !s.gameObject.activeSelf) continue;
                    Vector3 w = s.transform.position;
                    var xz = new Vector2(w.x, w.z);
                    Rect safe = Connectivity.Grow(area.rect, -0.2f);
                    bool bad = !safe.Contains(xz);
                    bool insideOwn = w.x > own.min.x && w.x < own.max.x && w.z > own.min.z && w.z < own.max.z;
                    if (!bad && !insideOwn && w.y < HouseShell.FloorTop + 0.15f && grids.TryGetValue(pf.room, out RoomGrid g) &&
                        g.WorldToCell(xz, out int i, out int j) && (g[i, j] & Cell.Low) != 0)
                    {
                        bad = true; // a floor spot covered by some body (e.g. "behind the sofa" with the sofa on a wall)
                    }

                    if (bad)
                    {
                        s.gameObject.SetActive(false);
                        off++;
                        continue;
                    }

                    var key = (pf.room, s.concealmentType);
                    if (!buckets.TryGetValue(key, out List<HidingSpot> list)) buckets[key] = list = new List<HidingSpot>();
                    list.Add(s);
                }
            }

            foreach (var kv in buckets)
            {
                int cap = CapFor(kv.Key.Item2);
                List<HidingSpot> list = kv.Value;
                for (int i = list.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (list[i], list[j]) = (list[j], list[i]);
                }

                for (int i = cap; i < list.Count; i++)
                {
                    list[i].gameObject.SetActive(false);
                    off++;
                }
            }

            return off;
        }

        private static Bounds OwnBounds(PlacedFurniture pf)
        {
            Bounds b = new Bounds(pf.transform.position, Vector3.zero);
            bool has = false;
            foreach (Renderer r in pf.GetComponentsInChildren<Renderer>())
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }

            return b;
        }

        private int CapFor(ConcealmentType t)
        {
            foreach (SpotCap c in spotCaps)
            {
                if (c.type == t) return c.maxPerRoom;
            }

            return 99;
        }
    }
}
