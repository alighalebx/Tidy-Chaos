using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WheresMyLeftSock.Items;
using WheresMyLeftSock.World.Generation;

namespace WheresMyLeftSock.EditorTools
{
    /// <summary>
    /// Tidy Chaos/Generation menu: preview layouts in the editor, stress-test the rules over many seeds,
    /// render a contact sheet of layouts side by side.
    /// </summary>
    [InitializeOnLoad]
    public static class GenerationTools
    {
        private static int _previewSeed = 1;

        static GenerationTools()
        {
            // The scene must always be saved with the authored layout: props are dressed around it and use it
            // at runtime to find the piece they belong to.
            EditorSceneManager.sceneSaving += (scene, path) =>
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    var g = root.GetComponentInChildren<FurnitureGenerator>();
                    if (g != null && !g.OutputIsAuthored)
                    {
                        ShowAuthored(g);
                        Debug.Log("[Generation] Restored the authored furniture layout before saving (props are dressed around it).");
                    }
                }
            };
        }

        private static FurnitureGenerator Gen()
        {
            var g = Object.FindFirstObjectByType<FurnitureGenerator>();
            if (g == null) Debug.LogWarning("[Generation] No FurnitureGenerator in the scene - run Tidy Chaos/Build House.");
            return g;
        }

        /// Preview a generated layout in the editor (furniture only - authored props are never moved in edit mode).
        public static HousePlan Preview(FurnitureGenerator gen, int seed)
        {
            FurnitureGenerator.InstantiateOverride = (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            try
            {
                HousePlan plan = gen.Generate(seed, false);
                EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
                return plan;
            }
            finally
            {
                FurnitureGenerator.InstantiateOverride = null;
            }
        }

        public static void ShowAuthored(FurnitureGenerator gen)
        {
            FurnitureGenerator.InstantiateOverride = (prefab, parent) => (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            try
            {
                gen.BuildAuthored();
                if (!Application.isPlaying) EditorSceneManager.MarkSceneDirty(gen.gameObject.scene);
            }
            finally
            {
                FurnitureGenerator.InstantiateOverride = null;
            }
        }

        [MenuItem("Tidy Chaos/Generation/Show Authored Layout")]
        private static void ShowAuthoredMenu()
        {
            var g = Gen();
            if (g != null) ShowAuthored(g);
        }

        [MenuItem("Tidy Chaos/Generation/Preview Next Seed")]
        private static void PreviewNext()
        {
            var g = Gen();
            if (g != null) Preview(g, ++_previewSeed);
        }

        [MenuItem("Tidy Chaos/Generation/Preview Random Seed")]
        private static void PreviewRandom()
        {
            var g = Gen();
            if (g != null) Preview(g, _previewSeed = FurnitureGenerator.NewSeed());
        }

        [MenuItem("Tidy Chaos/Generation/Preview Last Played Seed")]
        private static void PreviewLast()
        {
            var g = Gen();
            if (g != null && FurnitureGenerator.LastSeed >= 0) Preview(g, _previewSeed = FurnitureGenerator.LastSeed);
        }

        // ------------------------------------------------------------------ stress test

        [MenuItem("Tidy Chaos/Generation/Stress Test (500 seeds)")]
        private static void StressMenu() => Debug.Log(StressTest(500));

        /// Plans N seeds (no GameObjects) and re-checks every placement with independent invariants.
        public static string StressTest(int seeds, int firstSeed = 1)
        {
            var g = Gen();
            if (g == null) return "no generator";
            Dictionary<Room, List<Obstacle>> obstacles = g.CollectObstacles();

            var fallbacks = new Dictionary<Room, int>();
            var attempts = new Dictionary<Room, int>();
            var defCounts = new Dictionary<string, int>();
            var rejections = new Dictionary<string, int>();
            var violations = new List<string>();
            var signatures = new HashSet<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            for (int s = firstSeed; s < firstSeed + seeds; s++)
            {
                HousePlan plan = g.Plan(s, obstacles, out Dictionary<Room, RoomGrid> _);
                foreach (var kv in plan.rejections)
                {
                    rejections.TryGetValue(kv.Key, out int n);
                    rejections[kv.Key] = n + kv.Value;
                }

                var sig = new StringBuilder();
                foreach (RoomPlan rp in plan.rooms)
                {
                    attempts.TryGetValue(rp.room, out int a);
                    attempts[rp.room] = a + rp.attempts;
                    if (rp.usedFallback)
                    {
                        fallbacks.TryGetValue(rp.room, out int f);
                        fallbacks[rp.room] = f + 1;
                        if (violations.Count < 20) violations.Add($"seed {s} {rp.room}: FALLBACK ({rp.failure})");
                        continue;
                    }

                    foreach (Placement p in rp.placements)
                    {
                        defCounts.TryGetValue(p.def.id, out int c);
                        defCounts[p.def.id] = c + 1;
                        sig.Append(p.def.id).Append(Mathf.RoundToInt(p.center.x * 2)).Append(',').Append(Mathf.RoundToInt(p.center.y * 2)).Append(';');
                    }

                    CheckInvariants(s, rp, obstacles.TryGetValue(rp.room, out var o) ? o : null, violations);
                }

                signatures.Add(sig.ToString());
            }

            sw.Stop();
            var sb = new StringBuilder();
            sb.AppendLine($"[Generation] Stress test: {seeds} seeds in {sw.ElapsedMilliseconds} ms ({sw.ElapsedMilliseconds / (float)seeds:F1} ms/house), " +
                          $"{signatures.Count} distinct layouts.");
            foreach (RoomArea area in HouseShell.Rooms)
            {
                fallbacks.TryGetValue(area.room, out int f);
                attempts.TryGetValue(area.room, out int a);
                sb.AppendLine($"  {area.room,-10} fallback {f}/{seeds}   avg attempts {a / (float)seeds:F2}");
            }

            sb.AppendLine("  pieces per house: " + string.Join(", ", Sorted(defCounts, seeds)));
            sb.AppendLine("  rule rejections (candidates): " + string.Join(", ", SortedRaw(rejections)));
            sb.AppendLine(violations.Count == 0 ? "  invariants: OK (no violations)" : $"  VIOLATIONS ({violations.Count}):\n    " + string.Join("\n    ", violations));
            return sb.ToString();
        }

        /// Independent re-check of the hard rules on a finished plan (not using the planner's grid).
        private static void CheckInvariants(int seed, RoomPlan rp, List<Obstacle> obstacles, List<string> violations)
        {
            RoomArea area = HouseShell.Get(rp.room);
            void V(string msg)
            {
                if (violations.Count < 40) violations.Add($"seed {seed} {rp.room}: {msg}");
            }

            var doorZones = new List<Rect>();
            foreach (WallSegment s in area.segments)
            {
                if (!s.IsOpening) continue;
                float depth = s.kind == EdgeKind.FrontDoor ? PlanContext.FrontDoorZoneDepth : PlanContext.DoorZoneDepth;
                doorZones.Add(Strip(s, s.clearFrom - PlanContext.DoorZoneMargin, s.clearTo + PlanContext.DoorZoneMargin, depth));
            }

            for (int i = 0; i < rp.placements.Count; i++)
            {
                Placement p = rp.placements[i];
                if (!RoomMaskUtil.Allows(p.def.rooms, rp.room)) V($"{p.def.id} not allowed in {rp.room}");
                if (!Inside(area.rect, p.body)) V($"{p} outside the room");

                // architecture: hidden fixtures out of every doorway's line of sight, door-wall distances
                foreach (WallSegment s in area.segments)
                {
                    if (!s.IsOpening) continue;
                    if (p.def.hideFromDoors && Overlaps(Strip(s, s.clearFrom + 0.1f, s.clearTo - 0.1f, 4f), p.body)) V($"{p} visible through a doorway");
                    if (p.def.doorWallMinDistance > 0f && p.low && BackSideOf(area, p) == s.side)
                    {
                        bool h = s.side == Side.South || s.side == Side.North;
                        float a = h ? p.body.xMin : p.body.yMin, b = h ? p.body.xMax : p.body.yMax;
                        if (Mathf.Max(s.clearFrom - b, a - s.clearTo) < p.def.doorWallMinDistance - 0.01f) V($"{p} too close to a doorway on its wall");
                    }
                }
                if ((!p.rug || p.def.isHazard) && !p.onTop) // hazards are floor-layer but never in a doorway
                {
                    foreach (Rect dz in doorZones)
                    {
                        if (Overlaps(p.body, dz)) V($"{p} in a doorway zone");
                    }
                }

                for (int j = i + 1; j < rp.placements.Count; j++)
                {
                    Placement q = rp.placements[j];
                    bool family = p.anchor == q || q.anchor == p; // TV on its cabinet, mirror on its sink
                    bool sameBand = (p.low && q.low) || (p.high && q.high && !family) || (p.rug && q.rug);
                    if (sameBand && Overlaps(p.body, q.body)) V($"{p} overlaps {q}");
                    if ((p.loose && (q.low || q.loose) || q.loose && p.low) && Overlaps(p.body, q.body)) V($"prop {p} overlaps {q}");
                    bool hz = p.def.isHazard || q.def.isHazard;
                    if (hz && (p.low || p.loose || p.rug) && (q.low || q.loose || q.rug) && Overlaps(p.body, q.body)) V($"hazard overlap: {p} / {q}");
                    if ((p.low || p.loose) && q.hasClearance && Overlaps(p.body, q.clearance)) V($"{p} blocks the front of {q}");
                    if ((q.low || q.loose) && p.hasClearance && Overlaps(q.body, p.clearance)) V($"{q} blocks the front of {p}");
                }

                if (obstacles == null) continue;
                foreach (Obstacle o in obstacles)
                {
                    if (p.low && o.low && Overlaps(p.body, o.rect)) V($"{p} overlaps authored {o.name}");
                    if (o.reserved && !p.onTop && p.baseY < 0.5f && Overlaps(p.body, o.rect)) V($"{p} on reserved {o.name}");
                }
            }
        }

        private static Rect Strip(WallSegment s, float from, float to, float depth)
        {
            switch (s.side)
            {
                case Side.South: return Rect.MinMaxRect(from, s.line, to, s.line + depth);
                case Side.North: return Rect.MinMaxRect(from, s.line - depth, to, s.line);
                case Side.West: return Rect.MinMaxRect(s.line, from, s.line + depth, to);
                default: return Rect.MinMaxRect(s.line - depth, from, s.line, to);
            }
        }

        /// The wall a piece's back is against (or null-ish -1 cast when free-standing).
        private static Side BackSideOf(RoomArea area, Placement p)
        {
            Vector2 f = p.Forward;
            Side s = f.y > 0.5f ? Side.South : f.y < -0.5f ? Side.North : f.x > 0.5f ? Side.West : Side.East;
            Rect r = area.rect;
            float gap = s == Side.South ? p.body.yMin - r.yMin : s == Side.North ? r.yMax - p.body.yMax : s == Side.West ? p.body.xMin - r.xMin : r.xMax - p.body.xMax;
            return gap < 0.2f ? s : (Side)(-1);
        }

        private static bool Inside(Rect outer, Rect r) =>
            r.xMin >= outer.xMin - 0.001f && r.xMax <= outer.xMax + 0.001f && r.yMin >= outer.yMin - 0.001f && r.yMax <= outer.yMax + 0.001f;

        /// Real overlap: more than 6cm deep on both axes (the grid guarantees half a 10cm cell; exactly 5cm is rounding).
        private static bool Overlaps(Rect a, Rect b)
        {
            float w = Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin);
            float h = Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin);
            return w > 0.06f && h > 0.06f;
        }

        // ------------------------------------------------------------------ facing audit

        [MenuItem("Tidy Chaos/Generation/Facing Audit (catalog)")]
        private static void FacingMenu() => Debug.Log(FacingAudit(Path.Combine(Application.dataPath, "../Temp/FacingAudit.png")));

        /// Renders every catalog piece from where its FRONT should be (+Z, slightly above), one tile each.
        /// Every tile must show the usable side (drawers, doors, seat, bowl) facing the camera - if a piece
        /// shows its side or back, fix its modelYaw in FurnitureCatalog.
        public static string FacingAudit(string path, int tile = 220)
        {
            var defs = new List<FurnitureDef>();
            foreach (string guid in AssetDatabase.FindAssets("t:FurnitureDef", new[] { FurnitureCatalog.DefDir }))
            {
                var d = AssetDatabase.LoadAssetAtPath<FurnitureDef>(AssetDatabase.GUIDToAssetPath(guid));
                if (d != null && d.prefab != null && !d.isRug) defs.Add(d);
            }

            defs.Sort((a, b) => string.CompareOrdinal(a.id, b.id));
            int cols = 6, rows = Mathf.CeilToInt(defs.Count / (float)cols);
            var sheet = new Texture2D(cols * tile, rows * tile, TextureFormat.RGB24, false);
            var camGo = new GameObject("AuditCam");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.17f, 0.2f);
            cam.fieldOfView = 40f;
            var light = new GameObject("AuditLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(40f, 200f, 0f);
            var rt = new RenderTexture(tile, tile, 24) { antiAliasing = 4 };
            var shot = new Texture2D(tile, tile, TextureFormat.RGB24, false);
            Vector3 origin = new Vector3(3000f, 0f, 3000f);
            var order = new StringBuilder("[Generation] Facing audit (left->right, top->bottom): ");
            cam.targetTexture = rt;
            cam.Render();
            try
            {
                for (int k = 0; k < defs.Count; k++)
                {
                    FurnitureDef d = defs[k];
                    order.Append(d.id).Append(k < defs.Count - 1 ? ", " : "");
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(d.prefab);
                    go.transform.SetPositionAndRotation(origin, Quaternion.identity);
                    float size = Mathf.Max(d.size.x, d.size.y, d.size.z);
                    Vector3 c = origin + Vector3.up * d.size.y * 0.5f;
                    cam.transform.position = c + new Vector3(0.25f * size, 0.6f * size, 2.4f * size); // front = +Z
                    cam.transform.LookAt(c);
                    cam.Render();
                    RenderTexture.active = rt;
                    shot.ReadPixels(new Rect(0, 0, tile, tile), 0, 0);
                    shot.Apply();
                    sheet.SetPixels((k % cols) * tile, (rows - 1 - k / cols) * tile, tile, tile, shot.GetPixels());
                    Object.DestroyImmediate(go);
                }
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                Object.DestroyImmediate(light.gameObject);
            }

            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(shot);
            return order + "\n  -> " + Path.GetFullPath(path);
        }

        private static IEnumerable<string> Sorted(Dictionary<string, int> d, int seeds)
        {
            var keys = new List<string>(d.Keys);
            keys.Sort();
            foreach (string k in keys) yield return $"{k} {d[k] / (float)seeds:F2}";
        }

        private static IEnumerable<string> SortedRaw(Dictionary<string, int> d)
        {
            var keys = new List<string>(d.Keys);
            keys.Sort((a, b) => d[b].CompareTo(d[a]));
            foreach (string k in keys) yield return $"{k} {d[k]}";
        }

        // ------------------------------------------------------------------ contact sheet

        [MenuItem("Tidy Chaos/Generation/Contact Sheet (6 seeds)")]
        private static void SheetMenu()
        {
            string path = ContactSheet(new[] { 1, 2, 3, 4, 5, 6 }, Path.Combine(Application.dataPath, "../Temp/GenerationSheet.png"));
            Debug.Log("[Generation] Contact sheet: " + path);
        }

        /// Renders a top-down view of each seed (ceiling and loose clutter hidden) into one PNG.
        public static string ContactSheet(int[] seeds, string path, int tile = 700, bool hideClutter = true)
        {
            var g = Gen();
            if (g == null) return null;

            var hidden = new List<GameObject>();
            void Hide(string n)
            {
                foreach (Transform t in g.GetComponentsInChildren<Transform>(true))
                {
                    if (t.name == n && t.gameObject.activeSelf)
                    {
                        t.gameObject.SetActive(false);
                        hidden.Add(t.gameObject);
                    }
                }
            }

            Hide("Ceiling");
            Hide("Lighting");
            if (hideClutter) Hide("Clutter");

            // flat, shadowless light reads better from above
            var shadowed = new List<(Light l, LightShadows s)>();
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                shadowed.Add((l, l.shadows));
                l.shadows = LightShadows.None;
            }

            var camGo = new GameObject("SheetCam");
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 8.9f;
            cam.aspect = 12.6f / 17.8f;
            cam.transform.SetPositionAndRotation(new Vector3(6.3f, 20f, 8.4f), Quaternion.Euler(90f, 0f, 0f));
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.12f, 0.14f);
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 40f;

            int w = Mathf.RoundToInt(tile * cam.aspect), h = tile;
            int cols = Mathf.Min(3, seeds.Length), rows = Mathf.CeilToInt(seeds.Length / (float)cols);
            var sheet = new Texture2D(cols * w + (cols + 1) * 8, rows * h + (rows + 1) * 8, TextureFormat.RGB24, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(30, 30, 34, 255);
            sheet.SetPixels32(fill);

            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            var shot = new Texture2D(w, h, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt;
                cam.Render(); // warm-up: the first off-screen render of a fresh camera comes out empty
                for (int k = 0; k < seeds.Length; k++)
                {
                    Preview(g, seeds[k]);
                    cam.targetTexture = rt;
                    cam.Render();
                    RenderTexture.active = rt;
                    shot.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                    shot.Apply();
                    int cx = k % cols, cy = rows - 1 - k / cols;
                    sheet.SetPixels(8 + cx * (w + 8), 8 + cy * (h + 8), w, h, shot.GetPixels());
                }
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(camGo);
                foreach (GameObject go in hidden) go.SetActive(true);
                foreach (var (l, s) in shadowed) l.shadows = s;
                if (!Application.isPlaying) ShowAuthored(g);
            }

            sheet.Apply();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllBytes(path, sheet.EncodeToPNG());
            Object.DestroyImmediate(sheet);
            Object.DestroyImmediate(shot);
            return Path.GetFullPath(path);
        }
    }
}
