using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace WheresMyLeftSock.EditorTools
{
    /// <summary>
    /// Swaps Kenney's pastel embedded materials (carpet = pale red, carpetBlue = baby blue, wood = peach...)
    /// for punchier palette materials. Context-aware: walls and floor tiles get their own wood/paint.
    /// Runs automatically at the end of Build House; also available from the menu.
    /// </summary>
    public static class PaletteBuilder
    {
        private const string Dir = "Assets/_Project/Art/Materials/Palette";
        private const string KenneyRoot = "Assets/kenney-furniture-kit-interior-asset-collection";

        private static readonly HashSet<string> WallModels = new HashSet<string>
        {
            "wall", "wallWindow", "wallDoorway", "wallDoorwayWide", "wallHalf", "wallCorner", "wallWindowSlide"
        };

        // furniture / props
        private static readonly Dictionary<string, Color> Props = new Dictionary<string, Color>
        {
            { "carpet",       new Color(0.82f, 0.20f, 0.22f) }, // proper red
            { "carpetDarker", new Color(0.52f, 0.11f, 0.16f) }, // crimson
            { "carpetBlue",   new Color(0.16f, 0.35f, 0.78f) }, // real blue
            { "carpetWhite",  new Color(0.95f, 0.95f, 0.93f) },
            { "wood",         new Color(0.80f, 0.55f, 0.32f) }, // honey oak
            { "woodDark",     new Color(0.50f, 0.32f, 0.19f) }, // walnut
            { "metal",        new Color(0.80f, 0.83f, 0.85f) },
            { "metalMedium",  new Color(0.42f, 0.47f, 0.50f) },
            { "metalDark",    new Color(0.20f, 0.23f, 0.26f) },
            { "metalLight",   new Color(0.93f, 0.95f, 0.95f) },
            { "plant",        new Color(0.22f, 0.62f, 0.30f) }, // leafy green, not mint
            { "fur",          new Color(0.60f, 0.38f, 0.20f) },
        };

        // per-model overrides ("model/material") so not every fabric is the same red
        private static readonly Dictionary<string, Color> PerModel = new Dictionary<string, Color>
        {
            // cartoon toy-box accents: bold primaries that contrast each room's wall colour
            // living room (blue walls): red sofa + yellow rug
            { "loungeSofa/carpet",          new Color(0.92f, 0.18f, 0.18f) },
            { "loungeChair/carpet",         new Color(0.92f, 0.18f, 0.18f) },
            { "loungeSofaCorner/carpet",    new Color(0.92f, 0.18f, 0.18f) },
            { "rugRound/carpet",            new Color(1.00f, 0.78f, 0.12f) },
            { "rugRound/carpetDarker",      new Color(0.95f, 0.55f, 0.05f) },
            // hallway (green walls) + bedroom (purple walls) runners: red
            { "rugRectangle/carpet",        new Color(0.92f, 0.20f, 0.22f) },
            { "rugRectangle/carpetDarker",  new Color(0.65f, 0.10f, 0.14f) },
            // bedroom (purple walls): orange-yellow duvet + chair
            { "bedDouble/carpet",           new Color(1.00f, 0.62f, 0.08f) },
            { "chairDesk/carpet",           new Color(1.00f, 0.62f, 0.08f) },
            // bathroom (aqua walls): orange mat
            { "rugRounded/carpet",          new Color(1.00f, 0.45f, 0.15f) },
            { "rugRounded/carpetDarker",    new Color(0.85f, 0.28f, 0.08f) },
            // kitchen (yellow walls, red checker): blue seats
            { "chairCushion/carpet",        new Color(0.15f, 0.42f, 0.92f) },
            { "stoolBar/carpet",            new Color(0.15f, 0.42f, 0.92f) },
            { "benchCushionLow/carpet",     new Color(0.15f, 0.42f, 0.92f) },
        };

        private static readonly Dictionary<string, Color> Walls = new Dictionary<string, Color>
        {
            { "wood",        new Color(0.97f, 0.97f, 0.95f) }, // white cartoon trim / frames
            { "_defaultMat", new Color(0.94f, 0.92f, 0.87f) }, // warm white face
            { "metalDark",   new Color(0.42f, 0.58f, 0.68f) }, // painted face: dusty blue
        };

        private static readonly Dictionary<string, Color> Floor = new Dictionary<string, Color>
        {
            { "wood", new Color(0.86f, 0.64f, 0.40f) }, // bright clean oak
        };

        private static readonly Dictionary<string, Material> Cache = new Dictionary<string, Material>();

        // ------------------------------------------------------------ colour schemes (swappable)

        private static Color C(float r, float g, float b) => new Color(r, g, b);

        private class Scheme
        {
            public Color Living, Kitchen, Hall, Bedroom, Bath, Outside;
            public Color Sofa, RugRound, RugRoundDark, Runner, RunnerDark, Bed, DeskChair, BathMat, BathMatDark, Seats;
            public Color KitchenCheckA, KitchenCheckB, BathCheckA, BathCheckB;
            public Color Trim, FloorWood;
        }

        private static readonly Dictionary<string, Scheme> Schemes = new Dictionary<string, Scheme>
        {
            { "ToyBox", new Scheme {
                Living = C(0.30f, 0.62f, 0.95f), Kitchen = C(1.00f, 0.80f, 0.20f), Hall = C(0.33f, 0.70f, 0.30f),
                Bedroom = C(0.58f, 0.42f, 0.90f), Bath = C(0.20f, 0.78f, 0.80f), Outside = C(0.96f, 0.95f, 0.92f),
                Sofa = C(0.92f, 0.18f, 0.18f), RugRound = C(1.00f, 0.78f, 0.12f), RugRoundDark = C(0.95f, 0.55f, 0.05f),
                Runner = C(0.92f, 0.20f, 0.22f), RunnerDark = C(0.65f, 0.10f, 0.14f), Bed = C(1.00f, 0.62f, 0.08f),
                DeskChair = C(1.00f, 0.62f, 0.08f), BathMat = C(1.00f, 0.45f, 0.15f), BathMatDark = C(0.85f, 0.28f, 0.08f),
                Seats = C(0.15f, 0.42f, 0.92f),
                KitchenCheckA = C(0.93f, 0.25f, 0.27f), KitchenCheckB = C(0.98f, 0.95f, 0.88f),
                BathCheckA = C(0.15f, 0.42f, 0.92f), BathCheckB = C(0.97f, 0.98f, 0.96f),
                Trim = C(0.97f, 0.97f, 0.95f), FloorWood = C(0.86f, 0.64f, 0.40f) } },

            // Fall Guys-ish: hot candy walls, turquoise/purple accents, light maple
            { "CandyPop", new Scheme {
                Living = C(1.00f, 0.48f, 0.72f), Kitchen = C(1.00f, 0.88f, 0.32f), Hall = C(1.00f, 0.62f, 0.28f),
                Bedroom = C(0.52f, 0.56f, 1.00f), Bath = C(0.42f, 0.90f, 0.72f), Outside = C(1.00f, 0.97f, 0.94f),
                Sofa = C(0.12f, 0.70f, 0.75f), RugRound = C(1.00f, 0.85f, 0.20f), RugRoundDark = C(0.95f, 0.60f, 0.10f),
                Runner = C(0.55f, 0.30f, 0.90f), RunnerDark = C(0.38f, 0.18f, 0.70f), Bed = C(1.00f, 0.40f, 0.65f),
                DeskChair = C(1.00f, 0.40f, 0.65f), BathMat = C(0.55f, 0.30f, 0.90f), BathMatDark = C(0.38f, 0.18f, 0.70f),
                Seats = C(0.12f, 0.70f, 0.75f),
                KitchenCheckA = C(1.00f, 0.42f, 0.62f), KitchenCheckB = C(1.00f, 0.98f, 0.95f),
                BathCheckA = C(0.52f, 0.56f, 1.00f), BathCheckB = C(1.00f, 0.98f, 0.95f),
                Trim = C(0.98f, 0.98f, 0.97f), FloorWood = C(0.90f, 0.70f, 0.48f) } },

            // groovy 70s: avocado, harvest gold, burnt orange, teal, salmon; cream trim, darker wood
            { "Retro70s", new Scheme {
                Living = C(0.58f, 0.66f, 0.28f), Kitchen = C(0.92f, 0.70f, 0.20f), Hall = C(0.86f, 0.48f, 0.20f),
                Bedroom = C(0.20f, 0.55f, 0.58f), Bath = C(0.95f, 0.62f, 0.62f), Outside = C(0.95f, 0.90f, 0.78f),
                Sofa = C(0.82f, 0.38f, 0.12f), RugRound = C(0.92f, 0.70f, 0.20f), RugRoundDark = C(0.60f, 0.35f, 0.15f),
                Runner = C(0.60f, 0.30f, 0.15f), RunnerDark = C(0.40f, 0.20f, 0.10f), Bed = C(0.92f, 0.60f, 0.15f),
                DeskChair = C(0.82f, 0.38f, 0.12f), BathMat = C(0.58f, 0.66f, 0.28f), BathMatDark = C(0.40f, 0.46f, 0.18f),
                Seats = C(0.82f, 0.38f, 0.12f),
                KitchenCheckA = C(0.82f, 0.38f, 0.12f), KitchenCheckB = C(0.96f, 0.90f, 0.76f),
                BathCheckA = C(0.20f, 0.55f, 0.58f), BathCheckB = C(0.96f, 0.90f, 0.76f),
                Trim = C(0.95f, 0.90f, 0.78f), FloorWood = C(0.62f, 0.40f, 0.22f) } },

            // THE PICK: Retro 70s house (living, bedroom, kitchen, hall) + Seaside bathroom
            { "Picked", new Scheme {
                Living = C(0.58f, 0.66f, 0.28f), Kitchen = C(0.92f, 0.70f, 0.20f), Hall = C(0.86f, 0.48f, 0.20f),
                Bedroom = C(0.20f, 0.55f, 0.58f), Bath = C(0.62f, 0.86f, 1.00f), Outside = C(0.95f, 0.90f, 0.78f),
                Sofa = C(0.82f, 0.38f, 0.12f), RugRound = C(0.92f, 0.70f, 0.20f), RugRoundDark = C(0.60f, 0.35f, 0.15f),
                Runner = C(0.60f, 0.30f, 0.15f), RunnerDark = C(0.40f, 0.20f, 0.10f), Bed = C(0.92f, 0.60f, 0.15f),
                DeskChair = C(0.82f, 0.38f, 0.12f), BathMat = C(1.00f, 0.55f, 0.45f), BathMatDark = C(0.85f, 0.35f, 0.30f),
                Seats = C(0.82f, 0.38f, 0.12f),
                KitchenCheckA = C(0.82f, 0.38f, 0.12f), KitchenCheckB = C(0.96f, 0.90f, 0.76f),
                BathCheckA = C(0.15f, 0.28f, 0.60f), BathCheckB = C(0.99f, 0.99f, 0.97f),
                Trim = C(0.95f, 0.90f, 0.78f), FloorWood = C(0.62f, 0.40f, 0.22f) } },

            // beach house: turquoise, coral, sunshine, ocean blue, sky; navy accents, driftwood floor
            { "Seaside", new Scheme {
                Living = C(0.22f, 0.74f, 0.80f), Kitchen = C(1.00f, 0.55f, 0.45f), Hall = C(1.00f, 0.84f, 0.35f),
                Bedroom = C(0.28f, 0.45f, 0.85f), Bath = C(0.62f, 0.86f, 1.00f), Outside = C(0.98f, 0.98f, 0.96f),
                Sofa = C(1.00f, 0.50f, 0.40f), RugRound = C(0.15f, 0.28f, 0.60f), RugRoundDark = C(0.08f, 0.16f, 0.40f),
                Runner = C(0.22f, 0.74f, 0.80f), RunnerDark = C(0.10f, 0.50f, 0.56f), Bed = C(1.00f, 0.84f, 0.35f),
                DeskChair = C(1.00f, 0.50f, 0.40f), BathMat = C(1.00f, 0.55f, 0.45f), BathMatDark = C(0.85f, 0.35f, 0.30f),
                Seats = C(0.15f, 0.28f, 0.60f),
                KitchenCheckA = C(0.22f, 0.74f, 0.80f), KitchenCheckB = C(0.99f, 0.99f, 0.97f),
                BathCheckA = C(0.15f, 0.28f, 0.60f), BathCheckB = C(0.99f, 0.99f, 0.97f),
                Trim = C(0.98f, 0.98f, 0.97f), FloorWood = C(0.88f, 0.72f, 0.52f) } },

            // sherbet: warm coral/apricot/watermelon/orchid with a cyan pop, teal accents
            { "Sherbet", new Scheme {
                Living = C(1.00f, 0.55f, 0.40f), Kitchen = C(1.00f, 0.78f, 0.35f), Hall = C(0.98f, 0.45f, 0.55f),
                Bedroom = C(0.72f, 0.45f, 0.85f), Bath = C(0.40f, 0.78f, 0.88f), Outside = C(0.99f, 0.97f, 0.94f),
                Sofa = C(0.10f, 0.55f, 0.60f), RugRound = C(0.98f, 0.90f, 0.60f), RugRoundDark = C(0.95f, 0.70f, 0.35f),
                Runner = C(0.10f, 0.55f, 0.60f), RunnerDark = C(0.05f, 0.36f, 0.40f), Bed = C(0.10f, 0.60f, 0.65f),
                DeskChair = C(1.00f, 0.78f, 0.35f), BathMat = C(0.72f, 0.45f, 0.85f), BathMatDark = C(0.50f, 0.28f, 0.65f),
                Seats = C(0.10f, 0.55f, 0.60f),
                KitchenCheckA = C(0.98f, 0.45f, 0.55f), KitchenCheckB = C(1.00f, 0.98f, 0.95f),
                BathCheckA = C(0.72f, 0.45f, 0.85f), BathCheckB = C(1.00f, 0.98f, 0.95f),
                Trim = C(0.98f, 0.98f, 0.97f), FloorWood = C(0.88f, 0.68f, 0.46f) } },
        };

        private const string DefaultScheme = "Picked";
        private const string SchemePref = "TC_PaletteScheme";
        private static Color _kitchenA, _kitchenB, _bathA, _bathB;

        public static string ActiveScheme => EditorPrefs.GetString(SchemePref, DefaultScheme);

        private static void LoadScheme(string name)
        {
            if (!Schemes.TryGetValue(name, out Scheme s)) s = Schemes[DefaultScheme];
            RoomPaint["LivingRoom"] = s.Living; RoomPaint["Kitchen"] = s.Kitchen; RoomPaint["Hallway"] = s.Hall;
            RoomPaint["Bedroom"] = s.Bedroom; RoomPaint["Bathroom"] = s.Bath; RoomPaint["Outside"] = s.Outside;
            PerModel["loungeSofa/carpet"] = s.Sofa; PerModel["loungeChair/carpet"] = s.Sofa; PerModel["loungeSofaCorner/carpet"] = s.Sofa;
            PerModel["rugRound/carpet"] = s.RugRound; PerModel["rugRound/carpetDarker"] = s.RugRoundDark;
            PerModel["rugRectangle/carpet"] = s.Runner; PerModel["rugRectangle/carpetDarker"] = s.RunnerDark;
            PerModel["bedDouble/carpet"] = s.Bed; PerModel["chairDesk/carpet"] = s.DeskChair;
            PerModel["rugRounded/carpet"] = s.BathMat; PerModel["rugRounded/carpetDarker"] = s.BathMatDark;
            PerModel["chairCushion/carpet"] = s.Seats; PerModel["stoolBar/carpet"] = s.Seats; PerModel["benchCushionLow/carpet"] = s.Seats;
            Walls["wood"] = s.Trim;
            Floor["wood"] = s.FloorWood;
            _kitchenA = s.KitchenCheckA; _kitchenB = s.KitchenCheckB; _bathA = s.BathCheckA; _bathB = s.BathCheckB;
        }

        /// <summary>Switch scheme and recolour the existing palette materials in place (no rebuild needed).</summary>
        public static void UseScheme(string name)
        {
            EditorPrefs.SetString(SchemePref, name);
            LoadScheme(name);
            void Set(string file, Color c)
            {
                var m = AssetDatabase.LoadAssetAtPath<Material>(Dir + "/" + file + ".mat");
                if (m == null) return;
                m.SetColor("_BaseColor", c);
                if (m.HasProperty("_Color")) m.SetColor("_Color", c);
                EditorUtility.SetDirty(m);
            }

            foreach (var kv in RoomPaint) Set("P_Paint_" + kv.Key, kv.Value);
            foreach (var kv in PerModel) Set("P_" + kv.Key.Replace('/', '_'), kv.Value);
            Set("P_Wall_wood", Walls["wood"]);
            Set("P_Floor_wood", Floor["wood"]);
            Checker("Kitchen", _kitchenA, _kitchenB);
            Checker("Bathroom", _bathA, _bathB);
            AssetDatabase.SaveAssets();
        }

        [MenuItem("Tidy Chaos/Palette/Picked (Retro + Seaside bath)")] private static void S0() => UseScheme("Picked");
        [MenuItem("Tidy Chaos/Palette/Toy Box")] private static void S1() => UseScheme("ToyBox");
        [MenuItem("Tidy Chaos/Palette/Candy Pop")] private static void S2() => UseScheme("CandyPop");
        [MenuItem("Tidy Chaos/Palette/Retro 70s")] private static void S3() => UseScheme("Retro70s");
        [MenuItem("Tidy Chaos/Palette/Seaside")] private static void S4() => UseScheme("Seaside");
        [MenuItem("Tidy Chaos/Palette/Sherbet")] private static void S5() => UseScheme("Sherbet");

        [MenuItem("Tidy Chaos/Apply Punchy Palette")]
        public static void ApplyMenu()
        {
            int n = ApplyAll();
            EditorSceneManager.SaveOpenScenes();
            Debug.Log($"[Palette] swapped {n} material slots.");
        }

        /// <summary>Swaps materials in furniture prefabs and the open scene. Returns slots changed.</summary>
        public static int ApplyAll()
        {
            Cache.Clear();
            LoadScheme(ActiveScheme);
            int changed = 0;

            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Project/Prefabs/Furniture" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                GameObject root = PrefabUtility.LoadPrefabContents(path);
                int c = ApplyTo(root);
                if (c > 0)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }

                PrefabUtility.UnloadPrefabContents(root);
                changed += c;
            }

            foreach (GameObject go in EditorSceneManager.GetActiveScene().GetRootGameObjects())
            {
                changed += ApplyTo(go);
            }

            changed += PaintRooms();

            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            return changed;
        }

        // ------------------------------------------------------------ room paint (the actual "vibe")

        private const float T = 2.1f;

        private static readonly Dictionary<string, Color> RoomPaint = new Dictionary<string, Color>
        {
            // cartoon toy-box: one clear bold hue per room, white trim, bold contrasting props
            { "LivingRoom", new Color(0.30f, 0.62f, 0.95f) }, // sky blue    <-> red sofa + yellow rug
            { "Kitchen",    new Color(1.00f, 0.80f, 0.20f) }, // sunflower   <-> red checker + blue seats
            { "Hallway",    new Color(0.33f, 0.70f, 0.30f) }, // grass green <-> red runner
            { "Bedroom",    new Color(0.58f, 0.42f, 0.90f) }, // purple      <-> orange duvet
            { "Bathroom",   new Color(0.20f, 0.78f, 0.80f) }, // aqua        <-> blue/white tiles + orange mat
            { "Outside",    new Color(0.96f, 0.95f, 0.92f) }, // white siding
        };

        private static string RoomAt(Vector3 p)
        {
            if (p.x < 0f || p.x > 6 * T || p.z < 0f || p.z > 8 * T) return "Outside";
            if (p.z < 4 * T) return p.x >= 4 * T ? "Kitchen" : "LivingRoom";
            if (p.z < 5 * T) return "Hallway";
            return p.x < 4 * T ? "Bedroom" : "Bathroom";
        }

        /// <summary>Each wall face gets the paint of the room it faces; kitchen + bathroom get checker floors.</summary>
        public static int PaintRooms()
        {
            LoadScheme(ActiveScheme);
            GameObject house = GameObject.Find("GeneratedHouse");
            if (house == null) return 0;
            Transform structure = house.transform.Find("Structure");
            if (structure == null) return 0;
            int changed = 0;

            Transform walls = structure.Find("Walls");
            if (walls != null)
            {
                foreach (MeshFilter mf in walls.GetComponentsInChildren<MeshFilter>(true))
                {
                    Renderer r = mf.GetComponent<Renderer>();
                    Mesh mesh = mf.sharedMesh;
                    if (r == null || mesh == null) continue;
                    Material[] mats = r.sharedMaterials;
                    Vector3[] normals = null;
                    bool dirty = false;
                    for (int i = 0; i < mats.Length && i < mesh.subMeshCount; i++)
                    {
                        if (mats[i] == null) continue;
                        string n = mats[i].name;
                        bool face = n == "P_Wall__defaultMat" || n == "P_Wall_metalDark" || n.StartsWith("P_Paint_");
                        if (!face) continue;
                        if (normals == null) normals = mesh.normals;
                        Vector3 avg = Vector3.zero;
                        foreach (int k in mesh.GetIndices(i)) avg += normals[k];
                        Vector3 dir = mf.transform.TransformDirection(avg).normalized;
                        dir.y = 0f;
                        string room = RoomAt(r.bounds.center + dir.normalized * 0.5f);
                        Material paint = Paint(room, RoomPaint[room]);
                        if (mats[i] != paint) { mats[i] = paint; dirty = true; changed++; }
                    }

                    if (dirty)
                    {
                        Undo.RecordObject(r, "Paint");
                        r.sharedMaterials = mats;
                    }
                }
            }

            Transform floor = structure.Find("Floor");
            if (floor != null)
            {
                Material kitchen = Checker("Kitchen", _kitchenA, _kitchenB);
                Material bath = Checker("Bathroom", _bathA, _bathB);
                foreach (Renderer r in floor.GetComponentsInChildren<Renderer>(true))
                {
                    string room = RoomAt(r.bounds.center);
                    Material want = room == "Kitchen" ? kitchen : (room == "Bathroom" ? bath : null);
                    if (want == null) continue;
                    var mats = new Material[r.sharedMaterials.Length];
                    for (int i = 0; i < mats.Length; i++) mats[i] = want;
                    Undo.RecordObject(r, "Paint");
                    r.sharedMaterials = mats;
                    changed++;
                }
            }

            return changed;
        }

        private static Material Paint(string room, Color c)
        {
            string path = Dir + "/P_Paint_" + room + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                EnsureDir();
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }

            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", 0.2f);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Checker(string name, Color a, Color b)
        {
            EnsureDir();
            string texPath = Dir + "/T_Checker_" + name + ".png";
            const int S = 64;
            var tex = new Texture2D(S, S, TextureFormat.RGB24, false);
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                    tex.SetPixel(x, y, ((x < S / 2) ^ (y < S / 2)) ? a : b);
            File.WriteAllBytes(texPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(texPath);
            var imp = (TextureImporter)AssetImporter.GetAtPath(texPath);
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.filterMode = FilterMode.Bilinear;
            imp.anisoLevel = 8;
            imp.SaveAndReimport();

            string path = Dir + "/P_Floor_Checker_" + name + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }

            m.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            m.SetTextureScale("_BaseMap", new Vector2(CheckerScale, CheckerScale));
            m.SetColor("_BaseColor", Color.white);
            m.SetFloat("_Smoothness", 0.45f);
            EditorUtility.SetDirty(m);
            return m;
        }

        // Kenney floor UVs span ~39.37 per tile; 3 repeats per 2.1m tile = 35cm checks
        private const float CheckerScale = 3f / 39.37f;

        private static void EnsureDir()
        {
            if (!AssetDatabase.IsValidFolder(Dir))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "Palette");
            }
        }

        public static int ApplyTo(GameObject root)
        {
            int changed = 0;
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = r.sharedMaterials;
                bool dirty = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    Material swap = PaletteFor(mats[i]);
                    if (swap != null && swap != mats[i])
                    {
                        mats[i] = swap;
                        dirty = true;
                        changed++;
                    }
                }

                if (dirty)
                {
                    Undo.RecordObject(r, "Palette");
                    r.sharedMaterials = mats;
                }
            }

            return changed;
        }

        private static Material PaletteFor(Material src)
        {
            if (src == null)
            {
                return null;
            }

            string srcPath = AssetDatabase.GetAssetPath(src);
            if (!srcPath.StartsWith(KenneyRoot))
            {
                return null; // already ours / Synty / custom
            }

            string model = Path.GetFileNameWithoutExtension(srcPath);
            string ctx = WallModels.Contains(model) ? "Wall" : (model == "floorFull" ? "Floor" : "Prop");
            Dictionary<string, Color> table = ctx == "Wall" ? Walls : (ctx == "Floor" ? Floor : Props);
            string key = ctx + "_" + src.name;
            if (PerModel.TryGetValue(model + "/" + src.name, out Color col))
            {
                key = model + "_" + src.name;
            }
            else if (!table.TryGetValue(src.name, out col))
            {
                return null;
            }
            if (Cache.TryGetValue(key, out Material cached))
            {
                return cached;
            }

            if (!AssetDatabase.IsValidFolder(Dir))
            {
                AssetDatabase.CreateFolder("Assets/_Project/Art/Materials", "Palette");
            }

            string path = Dir + "/P_" + key + ".mat";
            Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(src);
                AssetDatabase.CreateAsset(m, path);
            }

            m.SetColor("_BaseColor", col);
            if (m.HasProperty("_Color"))
            {
                m.SetColor("_Color", col);
            }

            EditorUtility.SetDirty(m);
            Cache[key] = m;
            return m;
        }
    }
}
