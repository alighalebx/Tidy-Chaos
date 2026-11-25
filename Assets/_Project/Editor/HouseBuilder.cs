using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WheresMyLeftSock.Core;
using WheresMyLeftSock.Items;
using WheresMyLeftSock.Player;
using WheresMyLeftSock.World;

namespace WheresMyLeftSock.EditorTools
{
    /// <summary>
    /// One-click greybox+dressing of the 4-room house from the spec floorplan.
    /// Menu: Tidy Chaos/Build House. Re-running deletes and regenerates "GeneratedHouse".
    /// Grid: 2.1m tiles, x = west->east (6 tiles), z = south(front door)->north (8 tiles).
    /// </summary>
    public static class HouseBuilder
    {
        private const string KenneyDir = "Assets/kenney-furniture-kit-interior-asset-collection/Models/FBX format/";
        private const string MatDir = "Assets/_Project/Art/Materials/";
        private const string ClutterDir = "Assets/_Project/Prefabs/Furniture/Clutter/";
        private const string RootName = "GeneratedHouse";
        private const string SceneName = "Assets/_Project/Scenes/HouseLevel.unity";

        public const float S = 0.21f;          // Kenney FBX units -> meters (wall tile = 2.1m)
        public const float T = 2.1f;           // tile size
        public const float WallH = 12.9f * S;  // 2.709m
        public const float FloorTop = 0.5f * S;

        // '#' wall, 'w' window, 'd' wide open doorway, 'D' front door, '.'/missing = nothing
        // HLines[z line] -> chars per x tile. VLines[x line] -> chars per z tile.
        private static readonly string[] HLines =
        {
            "wwD#w#",   // z=0   front (living/kitchen)
            "", "", "",
            "#d##d#",   // z=8.4 living | hallway
            "##d#d#",   // z=10.5 hallway | bedroom, bathroom
            "", "",
            "#ww#w#"    // z=16.8 back
        };

        private static readonly string[] VLines =
        {
            "#ww###w#", // x=0 west
            "", "", "",
            ".....#d#", // x=8.4 bedroom | bathroom
            "",
            "##w###w#"  // x=12.6 east
        };

        private static Transform _structure, _furniture, _clutter, _spots, _lighting, _outside, _hazards;
        private static System.Random _rng;
        private static int _spotCounter;
        private static readonly Dictionary<string, GameObject> ClutterPrefabs = new Dictionary<string, GameObject>();

        // ------------------------------------------------------------------ menu

        [MenuItem("Tidy Chaos/Build House")]
        public static void BuildAll()
        {
            BuildClutterPrefabs();
            BuildHouse();
        }

        [MenuItem("Tidy Chaos/Rebuild Clutter Prefabs Only")]
        public static void BuildClutterPrefabs()
        {
            EnsureFolder(ClutterDir.TrimEnd('/'));
            EnsureFolder(MatDir.TrimEnd('/'));
            ClutterPrefabs.Clear();

            //            name                 model                 display              mass  throw  holdOff
            MakeClutter("Pillow",            "pillow",             "Pillow",            0.3f, 1.3f, 0f, lieFlat: true);
            MakeClutter("PillowBlue",        "pillowBlue",         "Pillow",            0.3f, 1.3f, 0f, lieFlat: true);
            MakeClutter("PillowLong",        "pillowLong",         "Long Pillow",       0.5f, 1.2f, 0.1f, lieFlat: true);
            MakeClutter("PillowBlueLong",    "pillowBlueLong",     "Long Pillow",       0.5f, 1.2f, 0.1f, lieFlat: true);
            MakeClutter("BoxClosed",         "cardboardBoxClosed", "Cardboard Box",     1.0f, 1.0f, 0.1f);
            MakeClutter("BoxOpen",           "cardboardBoxOpen",   "Cardboard Box",     0.8f, 1.0f, 0.1f);
            MakeClutter("Books",             "books",              "Books",             0.7f, 1.0f, 0f);
            MakeClutter("Bear",              "bear",               "Teddy Bear",        0.4f, 1.3f, 0f, scaleMul: 0.45f);
            MakeClutter("Trashcan",          "trashcan",           "Trash Can",         1.2f, 1.0f, 0f);
            MakeClutter("Chair",             "chair",              "Chair",             3.0f, 0.8f, 0.3f);
            MakeClutter("ChairCushion",      "chairCushion",       "Chair",             3.0f, 0.8f, 0.3f);
            MakeClutter("ChairDesk",         "chairDesk",          "Office Chair",      4.0f, 0.7f, 0.35f);
            MakeClutter("StoolBar",          "stoolBar",           "Bar Stool",         2.5f, 0.8f, 0.2f);
            MakeClutter("LampTable",         "lampRoundTable",     "Lamp",              1.0f, 1.0f, 0f);
            MakeClutter("Laptop",            "laptop",             "Laptop",            1.2f, 1.0f, 0f);
            MakeClutter("Radio",             "radio",              "Radio",             1.0f, 1.0f, 0f);
            MakeClutter("Toaster",           "toaster",            "Toaster",           1.0f, 1.0f, 0f);
            MakeClutter("Blender",           "kitchenBlender",     "Blender",           1.0f, 1.0f, 0f);
            MakeClutter("SpeakerSmall",      "speakerSmall",       "Speaker",           1.0f, 1.0f, 0f);
            MakeClutter("TVVintage",         "televisionVintage",  "Old TV",            6.0f, 0.6f, 0.25f);
            MakeClutter("PlantSmall1",       "plantSmall1",        "Plant",             0.5f, 1.2f, 0f);
            MakeClutter("PlantSmall2",       "plantSmall2",        "Plant",             0.5f, 1.2f, 0f);
            MakeClutter("PlantSmall3",       "plantSmall3",        "Plant",             0.5f, 1.2f, 0f);
            MakeClutter("Keyboard",          "computerKeyboard",   "Keyboard",          0.6f, 1.2f, 0f);
            MakeLaundryPile("LaundryPile_A", new Color(0.85f, 0.35f, 0.3f), new Color(0.3f, 0.45f, 0.8f), new Color(0.95f, 0.9f, 0.8f));
            MakeLaundryPile("LaundryPile_B", new Color(0.35f, 0.7f, 0.45f), new Color(0.2f, 0.2f, 0.25f), new Color(0.9f, 0.75f, 0.3f));
            MakeLaundryPile("LaundryPile_C", new Color(0.5f, 0.25f, 0.75f), new Color(0.95f, 0.95f, 0.95f), new Color(0.4f, 0.3f, 0.25f));
            MakeTowel("Towel_Blue", new Color(0.12f, 0.45f, 0.82f));
            MakeTowel("Towel_Pink", new Color(0.92f, 0.3f, 0.5f));

            // Synty props already in the project (real-world scale)
            //                name              synty prefab                                         display            mass  throw hold  scale
            MakeSyntyClutter("LaundryBasket", "PolygonAdventure/Prefabs/Props/SM_Prop_Basket_02", "Laundry Basket", 1.5f, 1.0f, 0.15f, 1f);
            MakeSyntyClutter("LaundrySack",   "PolygonAdventure/Prefabs/Props/SM_Prop_Sack_01",   "Laundry Bag",    2.0f, 0.9f, 0.15f, 1f);
            MakeSyntyClutter("BookBig",       "PolygonAdventure/Prefabs/Props/SM_Prop_Book_01",   "Book",           0.8f, 1.1f, 0f, 0.7f);
            MakeSyntyClutter("BookSmall",     "PolygonAdventure/Prefabs/Props/SM_Prop_Book_02",   "Book",           0.5f, 1.2f, 0f, 1f);
            MakeSyntyClutter("CheeseWheel",   "PolygonAdventure/Prefabs/Props/SM_Prop_Cheese_01", "Suspicious Cheese", 1.5f, 1.0f, 0f, 0.45f);
            MakeSyntyClutter("FryingPan",     "PolygonAdventure/Prefabs/Props/SM_Prop_Pot_01",    "Frying Pan",     1.5f, 1.1f, 0.1f, 1f);
            MakeSyntyClutter("Pumpkin",       "PolygonAdventure/Prefabs/Props/SM_Prop_Pumpkin_01", "Pumpkin (why?)", 3.0f, 0.8f, 0.1f, 0.8f);
            MakeSyntyClutter("Burger",        "PolygonCity/Prefabs/Props/SM_Prop_Burger_01",      "Yesterday's Burger", 0.3f, 1.3f, 0f, 0.8f);
            MakeSyntyClutter("MovingBox",     "PolygonCity/Prefabs/Props/SM_Prop_CardboardBox_01", "Moving Box",    2.0f, 0.9f, 0.25f, 0.8f);
            MakeSyntyClutter("Newspaper",     "PolygonCity/Prefabs/Props/SM_Prop_Newspaper_01",   "Newspaper",      0.2f, 1.3f, 0f, 1f);
            MakeSyntyClutter("Vase",          "PolygonPrototype/Prefabs/Props/SM_Prop_Vase_01",   "Vase",           1.0f, 1.1f, 0f, 1f);
            MakeSyntyClutter("BaseballBat",   "PolygonPrototype/Prefabs/Props/SM_Prop_Bat_01",    "Baseball Bat",   1.0f, 1.1f, 0.2f, 0.62f, lieFlat: true);

            AssetDatabase.SaveAssets();
            Debug.Log($"[HouseBuilder] Clutter prefabs ready in {ClutterDir} ({ClutterPrefabs.Count}).");
        }

        public static void BuildHouse()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (ClutterPrefabs.Count == 0)
            {
                LoadClutterPrefabs();
            }

            GameObject old = GameObject.Find(RootName);
            if (old != null)
            {
                Object.DestroyImmediate(old);
            }

            _rng = new System.Random(1234);
            _spotCounter = 0;

            var root = new GameObject(RootName);
            _structure = Group(root, "Structure");
            _furniture = Group(root, "Furniture");
            _clutter = Group(root, "Clutter");
            _spots = Group(root, "HidingSpots");
            _lighting = Group(root, "Lighting");
            _outside = Group(root, "Outside");
            _hazards = Group(root, "Hazards");

            BuildStructure();
            BuildBedroom();
            BuildBathroom();
            BuildHallway();
            BuildLivingRoom();
            BuildKitchen();
            BuildSyntyDressing();
            BuildOutside();
            BuildLighting();
            BuildHazards();

            root.AddComponent<HoverHighlighter>();

            DisableOldTestObjects();
            PlacePlayer();
            EnsureBuildSettings();
            PaletteBuilder.ApplyAll(); // punchy colours instead of Kenney pastels
            GameSystemsBuilder.Setup(); // items, spawning, scoring, escalation, audio, UI, intro

            Undo.RegisterCreatedObjectUndo(root, "Build House");
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log($"[HouseBuilder] Built house: {_spotCounter} hiding spots, {_clutter.childCount} clutter props.");
        }

        // ------------------------------------------------------------------ structure

        private static void BuildStructure()
        {
            Material post = Mat("M_WallTrim", new Color(0.93f, 0.91f, 0.87f), 0.1f);
            Material bathFloor = Mat("M_Floor_BathTile", new Color(0.45f, 0.7f, 0.8f), 0.6f);
            Material kitchenFloor = Mat("M_Floor_Kitchen", new Color(0.86f, 0.82f, 0.74f), 0.4f);
            Material ceiling = Mat("M_Ceiling", new Color(0.96f, 0.95f, 0.92f), 0.05f);

            // floor tiles (visual only)
            Transform floors = Group(_structure.gameObject, "Floor");
            for (int i = 0; i < 6; i++)
            {
                for (int j = 0; j < 8; j++)
                {
                    float x = i * T + T * 0.5f, z = j * T + T * 0.5f;
                    var tile = Place("floorFull", floors, new Vector3(x, 0f, z), 0f, snap: false, onFloor: false, colliders: false, openables: false);
                    Room r = RoomAt(x, z);
                    if (r == Room.Bathroom) SetAllMaterials(tile, bathFloor);
                    else if (r == Room.Kitchen) SetAllMaterials(tile, kitchenFloor);
                }
            }

            // thick floor collider so socks never fall through
            Box("FloorCollider", _structure, new Vector3(3 * T, FloorTop - 0.3f, 4 * T), new Vector3(6 * T + 1f, 0.6f, 8 * T + 1f), null, true);

            // ceiling
            Box("Ceiling", _structure, new Vector3(3 * T, WallH + 0.05f, 4 * T), new Vector3(6 * T + 0.2f, 0.1f, 8 * T + 0.2f), ceiling, true);

            Transform walls = Group(_structure.gameObject, "Walls");
            var used = new HashSet<Vector2Int>();
            for (int j = 0; j < HLines.Length; j++)
            {
                string line = HLines[j];
                for (int i = 0; i < line.Length; i++)
                {
                    if (WallPiece(line[i], walls, new Vector3(i * T + T * 0.5f, 0f, j * T), 0f))
                    {
                        used.Add(new Vector2Int(i, j));
                        used.Add(new Vector2Int(i + 1, j));
                    }
                }
            }

            for (int i = 0; i < VLines.Length; i++)
            {
                string line = VLines[i];
                for (int j = 0; j < line.Length; j++)
                {
                    if (WallPiece(line[j], walls, new Vector3(i * T, 0f, j * T + T * 0.5f), 90f))
                    {
                        used.Add(new Vector2Int(i, j));
                        used.Add(new Vector2Int(i, j + 1));
                    }
                }
            }

            // corner posts hide seams where wall tiles meet
            foreach (Vector2Int v in used)
            {
                Box("Post", walls, new Vector3(v.x * T, WallH * 0.5f, v.y * T), new Vector3(0.13f, WallH, 0.13f), post, true);
            }
        }

        private static bool WallPiece(char c, Transform parent, Vector3 p, float yaw)
        {
            switch (c)
            {
                case '#':
                    Place("wall", parent, p, yaw, snap: false, onFloor: false, openables: false);
                    return true;
                case 'w':
                    Place("wallWindow", parent, p, yaw, snap: false, onFloor: false, openables: false);
                    return true;
                case 'd':
                    Place("wallDoorwayWide", parent, p, yaw, snap: false, onFloor: false, openables: false);
                    return true;
                case 'D':
                    Place("wallDoorway", parent, p, yaw, snap: false, onFloor: false, openables: false);
                    var door = Place("doorwayFront", parent, p, yaw + 180f, snap: false, onFloor: false, openables: false, label: "FrontDoor_Exit");
                    door.AddComponent<ExitDoor>();
                    return true;
                default:
                    return false;
            }
        }

        // ------------------------------------------------------------------ rooms
        // Room bounds: Living x0-12.6 z0-8.4 (kitchen x>8.4), Hall z8.4-10.5,
        // Bedroom x0-8.4 z10.5-16.8, Bathroom x8.4-12.6 z10.5-16.8.
        // yaw: direction the furniture FRONT faces (0 = +z/north, 90 = east, 180 = south, 270 = west).

        private static void BuildBedroom()
        {
            const Room R = Room.Bedroom;
            var bed = Place("bedDouble", _furniture, V(4.2f, 16.8f), 180f, label: "Bed");
            var nsL = Place("cabinetBedDrawer", _furniture, V(2.75f, 16.8f), 180f, label: "Nightstand");
            var nsR = Place("cabinetBedDrawer", _furniture, V(5.65f, 16.8f), 180f, label: "Nightstand");
            var wardrobe = Place("bookcaseClosedDoors", _furniture, V(0f, 11.6f), 90f, label: "Wardrobe");
            var desk = Place("desk", _furniture, V(0f, 13.6f), 90f, label: "Desk");
            var shelf = Place("bookcaseClosedWide", _furniture, V(1.8f, 10.5f), 0f, label: "Bookcase");
            var closet = Place("bookcaseClosedDoors", _furniture, V(8.4f, 11.4f), 270f, label: "Closet");
            Place("bookcaseOpen", _furniture, V(8.4f, 15.8f), 270f, label: "Shelf");
            Place("rugRectangle", _furniture, V(4.2f, 12.7f), 0f, snap: false, colliders: false);
            Place("lampSquareFloor", _furniture, V(7.9f, 16.3f), 0f, snap: false);

            SpotInside(nsL, "drawer", R);
            SpotInside(nsR, "drawer", R);
            SpotInside(wardrobe, "door", R);
            SpotInside(closet, "door", R);
            SpotInside(desk, "drawer", R);
            SpotOnTop(shelf, R, ConcealmentType.HighShelf);
            SpotOnTop(closet, R, ConcealmentType.HighShelf);

            Spot(R, ConcealmentType.UnderClutter, Floor(6.5f, 15.0f)); Clutter("LaundryPile_A", 6.5f, 15.0f);
            Spot(R, ConcealmentType.UnderClutter, Surface(3.8f, 15.4f)); Clutter("Pillow", 3.8f, 15.4f); Clutter("PillowBlue", 4.6f, 15.5f);
            Spot(R, ConcealmentType.UnderClutter, Floor(7.8f, 13.2f)); Clutter("BoxOpen", 7.8f, 13.2f);
            Spot(R, ConcealmentType.FloorHidden, Floor(0.35f, 15.9f));
            Spot(R, ConcealmentType.ObviousDecoySpot, Surface(0.4f, 13.2f)); // desk top
            Spot(R, ConcealmentType.ObviousDecoySpot, Floor(4.2f, 12.4f));   // middle of rug

            Clutter("ChairDesk", 1.25f, 13.6f, yaw: 270f);
            Clutter("LampTable", 2.75f, 16.45f);
            Clutter("Laptop", 0.4f, 14.0f, yaw: 90f);
            Clutter("PlantSmall2", 0.35f, 14.25f);
            Clutter("Bear", 4.2f, 14.7f);
            Clutter("LaundryPile_B", 2.2f, 12.0f);
            Clutter("LaundryPile_C", 5.6f, 11.4f);
            Clutter("Books", 1.4f, 10.75f);
            Clutter("BoxClosed", 6.9f, 16.3f);
            Clutter("Keyboard", 0.35f, 13.4f, yaw: 90f);
        }

        private static void BuildBathroom()
        {
            const Room R = Room.Bathroom;
            var tub = Place("bathtub", _furniture, V(11.2f, 16.8f), 180f, label: "Bathtub");
            var toilet = Place("toilet", _furniture, V(12.6f, 13.6f), 270f, label: "Toilet");
            var sink = Place("bathroomSinkSquare", _furniture, V(11.0f, 10.5f), 0f, label: "Sink");
            Place("bathroomMirror", _furniture, V(11.0f, 10.5f), 0f, yOffset: 1.25f, label: "Mirror");
            var vanity = Place("bathroomCabinetDrawer", _furniture, V(8.4f, 15.6f), 90f, label: "Vanity");
            var washer = Place("washerDryerStacked", _furniture, V(12.6f, 11.6f), 270f, label: "Washer");
            Place("rugRounded", _furniture, V(10.4f, 14.3f), 90f, snap: false, colliders: false);

            SpotInside(vanity, "drawer", R);
            SpotInside(vanity, "drawerBottom", R);
            SpotInside(washer, "washerDoor", R);
            SpotOnTop(washer, R, ConcealmentType.HighShelf);
            Spot(R, ConcealmentType.FloorHidden, Surface(11.2f, 16.2f)); // inside the tub
            Spot(R, ConcealmentType.UnderClutter, Floor(9.5f, 15.0f)); Clutter("Towel_Blue", 9.5f, 15.0f);
            Spot(R, ConcealmentType.UnderClutter, Floor(11.9f, 12.65f)); Clutter("Trashcan", 11.9f, 12.65f);
            Spot(R, ConcealmentType.ObviousDecoySpot, Surface(12.3f, 13.6f)); // toilet lid
            Spot(R, ConcealmentType.ObviousDecoySpot, Floor(10.0f, 13.0f));

            Clutter("Towel_Pink", 11.6f, 16.2f);
            Clutter("LaundryPile_B", 9.2f, 12.0f);
            Clutter("PlantSmall3", 8.75f, 15.45f);
        }

        private static void BuildHallway()
        {
            const Room R = Room.Hallway;
            var bench = Place("benchCushionLow", _furniture, V(1.4f, 10.5f), 180f, label: "Shoe Bench");
            Place("coatRack", _furniture, V(0f, 9.45f), 90f, yOffset: 1.45f, label: "Coat Hooks");
            var coatCloset = Place("bookcaseClosedDoors", _furniture, V(12.6f, 9.45f), 270f, label: "Coat Closet");
            var side = Place("sideTableDrawers", _furniture, V(6.3f, 8.4f), 0f, label: "Side Table");
            Place("rugRectangle", _furniture, V(6.3f, 9.45f), 0f, snap: false, colliders: false);
            Place("pottedPlant", _furniture, V(11.4f, 8.8f), 0f, snap: false);
            Place("lampWall", _furniture, V(6.3f, 10.5f), 180f, yOffset: 1.7f, colliders: false);

            SpotInside(coatCloset, "door", R);
            SpotOnTop(coatCloset, R, ConcealmentType.HighShelf);
            SpotInside(side, "drawer", R);
            Spot(R, ConcealmentType.FloorHidden, Floor(0.35f, 9.0f));
            Spot(R, ConcealmentType.ObviousDecoySpot, Surface(1.4f, 10.2f)); // bench top
            Spot(R, ConcealmentType.UnderClutter, Floor(10.9f, 10.1f)); Clutter("BoxClosed", 10.9f, 10.1f);

            Clutter("BoxOpen", 7.6f, 8.8f);
            Clutter("BoxClosed", 7.6f, 8.8f); // stacks
            Clutter("LaundryPile_C", 4.6f, 9.9f);
            Clutter("PlantSmall1", 6.0f, 8.6f);
        }

        private static void BuildLivingRoom()
        {
            const Room R = Room.LivingRoom;
            var tv = Place("cabinetTelevisionDoors", _furniture, V(6.3f, 8.4f), 180f, label: "TV Cabinet");
            Place("televisionModern", _furniture, new Vector3(6.3f, TopY(tv), 8.4f), 180f, onFloor: false, label: "TV");
            var sofa = Place("loungeSofa", _furniture, V(6.3f, 4.2f), 0f, snap: false, label: "Sofa");
            var coffee = Place("tableCoffee", _furniture, V(6.3f, 6.0f), 0f, snap: false, label: "Coffee Table");
            Place("loungeChair", _furniture, V(3.4f, 5.3f), 60f, snap: false, label: "Armchair");
            Place("rugRound", _furniture, V(6.3f, 5.6f), 0f, snap: false, colliders: false);
            var books = Place("bookcaseOpen", _furniture, V(0f, 7.4f), 90f, label: "Bookshelf");
            Place("pottedPlant", _furniture, V(0.5f, 0.5f), 0f, snap: false);
            Place("lampRoundFloor", _furniture, V(8.0f, 4.0f), 0f, snap: false);
            Place("coatRackStanding", _furniture, V(3.6f, 0.6f), 0f, snap: false, label: "Coat Rack");
            Place("rugDoormat", _furniture, V(5.25f, 0.5f), 0f, snap: false, colliders: false);
            Place("speaker", _furniture, V(4.9f, 8.4f), 180f, label: "Speaker");
            Place("speaker", _furniture, V(7.7f, 8.4f), 180f, label: "Speaker");

            SpotInside(tv, "door", R);
            SpotOnTop(books, R, ConcealmentType.HighShelf);
            Spot(R, ConcealmentType.UnderClutter, Surface(6.3f, 4.0f)); Clutter("PillowLong", 6.3f, 4.05f); Clutter("Pillow", 5.85f, 4.1f);
            Spot(R, ConcealmentType.FloorHidden, Floor(6.3f, 3.45f)); // behind sofa
            Spot(R, ConcealmentType.UnderClutter, Floor(0.8f, 1.6f)); Clutter("BoxClosed", 0.8f, 1.6f); Clutter("BoxOpen", 0.8f, 1.6f);
            Spot(R, ConcealmentType.UnderClutter, Floor(2.3f, 7.6f)); Clutter("LaundryPile_A", 2.3f, 7.6f);
            Spot(R, ConcealmentType.ObviousDecoySpot, Surface(coffee.transform.position.x, coffee.transform.position.z));
            Spot(R, ConcealmentType.ObviousDecoySpot, Floor(5.25f, 0.6f)); // doormat

            Clutter("PillowBlue", 6.75f, 4.1f);
            Clutter("Books", 6.0f, 6.1f);
            Clutter("Radio", 6.7f, 6.0f);
            Clutter("SpeakerSmall", 0.35f, 7.0f);
            Clutter("BoxClosed", 1.6f, 1.1f);
            Clutter("TVVintage", 2.0f, 3.0f);
            Clutter("Bear", 3.4f, 5.3f);
        }

        private static void BuildKitchen()
        {
            const Room R = Room.Kitchen;
            // counters along the east wall, facing west
            var c1 = Place("kitchenCabinetDrawer", _furniture, V(12.6f, 2.2f), 270f, label: "Kitchen Cabinet");
            Place("kitchenStove", _furniture, V(12.6f, 3.1f), 270f, label: "Oven");
            var c3 = Place("kitchenCabinet", _furniture, V(12.6f, 4.0f), 270f, label: "Kitchen Cabinet");
            Place("kitchenSink", _furniture, V(12.6f, 4.9f), 270f, label: "Sink Cabinet");
            var c5 = Place("kitchenCabinetDrawer", _furniture, V(12.6f, 5.8f), 270f, label: "Kitchen Cabinet");
            Place("kitchenCabinet", _furniture, V(12.6f, 6.7f), 270f, label: "Kitchen Cabinet");
            var fridge = Place("kitchenFridgeLarge", _furniture, V(12.6f, 7.75f), 270f, label: "Fridge");
            Place("hoodModern", _furniture, V(12.6f, 3.1f), 270f, yOffset: 1.55f, colliders: false);
            var up1 = Place("kitchenCabinetUpper", _furniture, V(12.6f, 2.2f), 270f, yOffset: 1.45f, label: "Upper Cabinet");
            Place("kitchenCabinetUpperDouble", _furniture, V(12.6f, 6.25f), 270f, yOffset: 1.45f, label: "Upper Cabinet");
            var table = Place("table", _furniture, V(10.0f, 3.8f), 90f, snap: false, label: "Kitchen Table");
            Place("kitchenMicrowave", _furniture, new Vector3(12.25f, TopY(c5), 5.8f), 270f, onFloor: false, label: "Microwave");

            SpotInside(fridge, "doorLeft", R);
            SpotInside(c1, "drawer", R);
            SpotInside(c3, "door", R);
            SpotInside(up1, "door", R, ConcealmentType.HighShelf);
            SpotOnTop(fridge, R, ConcealmentType.HighShelf);
            Spot(R, ConcealmentType.UnderClutter, Surface(10.0f, 3.4f)); Clutter("BoxOpen", 10.0f, 3.4f);
            Spot(R, ConcealmentType.UnderClutter, Floor(12.0f, 0.75f)); Clutter("Trashcan", 12.0f, 0.75f);
            Spot(R, ConcealmentType.ObviousDecoySpot, Surface(12.2f, 4.0f)); // counter

            Clutter("Chair", 9.25f, 3.3f, yaw: 90f);
            Clutter("ChairCushion", 9.25f, 4.3f, yaw: 90f);
            Clutter("Chair", 10.75f, 3.3f, yaw: 270f);
            Clutter("StoolBar", 10.75f, 4.3f, yaw: 270f);
            Clutter("Toaster", 12.2f, 2.0f, yaw: 270f);
            Clutter("Blender", 12.25f, 6.95f, yaw: 270f);
            Clutter("PlantSmall1", 12.2f, 5.3f);
            Clutter("LaundryPile_C", 9.0f, 1.2f);
        }

        // ------------------------------------------------------------------ synty dressing (variety)

        private const string Synty = "Assets/SyntyStudios/";

        private static void BuildSyntyDressing()
        {
            // --- indoor throwables ---
            Clutter("LaundryBasket", 7.5f, 14.95f);
            Clutter("BaseballBat", 1.3f, 15.9f, yaw: 80f);
            Clutter("BookBig", 0.4f, 12.95f, yaw: 90f);
            Clutter("Newspaper", 3.0f, 11.3f);
            Clutter("LaundrySack", 12.1f, 14.75f);
            Clutter("MovingBox", 1.9f, 8.85f);
            Clutter("BookSmall", 6.6f, 9.0f);
            Clutter("Pumpkin", 1.4f, 6.2f);
            Clutter("Vase", 7.7f, 5.3f);
            Clutter("Newspaper", 4.5f, 3.0f);
            Clutter("MovingBox", 2.9f, 1.0f);
            Clutter("MovingBox", 2.9f, 1.0f);
            Clutter("Burger", 10.2f, 4.2f);
            Clutter("CheeseWheel", 12.25f, 2.6f);
            Clutter("FryingPan", 12.2f, 3.1f, yaw: 270f);

            // --- wall decor ---
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_Poster_01", _furniture, new Vector3(8.4f, 1.15f, 14.0f), 270f, 1f, snapFromWall: true);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_Poster_02", _furniture, new Vector3(3.0f, 1.15f, 10.5f), 0f, 1f, snapFromWall: true);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_Poster_Frame_01", _furniture, new Vector3(0f, 0.9f, 4.2f), 90f, 1f, snapFromWall: true);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_Poster_03", _furniture, new Vector3(7.3f, 1.15f, 8.4f), 0f, 0.8f, snapFromWall: true);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_Poster_01", _furniture, new Vector3(0f, 1.2f, 15.8f), 90f, 0.8f, snapFromWall: true);

            // --- big plants ---
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_PotPlant_02", _furniture, new Vector3(7.9f, FloorTop, 0.55f), 0f, 0.9f);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_PotPlant_01", _furniture, new Vector3(12.05f, FloorTop, 10.15f), 0f, 0.8f);
        }

        private static void BuildYard()
        {
            Transform yard = Group(_outside.gameObject, "Yard");
            string[] trees = { "PolygonStarter/Prefabs/SM_Generic_Tree_01", "PolygonStarter/Prefabs/SM_Generic_Tree_02", "PolygonStarter/Prefabs/SM_Generic_Tree_03", "PolygonStarter/Prefabs/SM_Generic_Tree_04" };
            Vector2[] treePos =
            {
                new Vector2(-3.5f, 2f), new Vector2(-4.2f, 8f), new Vector2(-3.2f, 14f), new Vector2(-4.5f, 19f),
                new Vector2(16.2f, 1.5f), new Vector2(17.0f, 7.5f), new Vector2(16.0f, 13f), new Vector2(17.2f, 19.5f),
                new Vector2(2f, 22f), new Vector2(9f, 23f), new Vector2(13.5f, 22f), new Vector2(-1.5f, -2.2f), new Vector2(14.5f, -2.0f)
            };
            for (int i = 0; i < treePos.Length; i++)
            {
                PlaceSynty(trees[i % trees.Length], yard, new Vector3(treePos[i].x, 0f, treePos[i].y), Rand(0f, 360f), Rand(1.1f, 1.5f));
            }

            for (int i = 0; i < 8; i++)
            {
                PlaceSynty("PolygonStarter/Prefabs/SM_Generic_Small_Rocks_0" + (1 + i % 5), yard,
                    new Vector3(Rand(-6f, 19f), 0f, Rand(18f, 26f)), Rand(0f, 360f), Rand(0.8f, 1.3f), keepColliders: false);
            }

            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_Mailbox_01", yard, new Vector3(3.9f, 0f, -2.4f), 180f, 1f);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_Hydrant_01", yard, new Vector3(11.5f, 0.06f, -3.6f), 0f, 1f);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_BusStop_01", yard, new Vector3(2.5f, 0f, -11.6f), 0f, 1f);
            PlaceSynty("PolygonCity/Prefabs/Props/SM_Prop_ParkBench_01", yard, new Vector3(4.0f, 0f, 20.5f), 180f, 1f);
            PlaceSynty("PolygonAdventure/Prefabs/Props/SM_Prop_Washingline_01", yard, new Vector3(9.5f, 0f, 20.5f), 0f, 1f);
            Box("Sidewalk_Far", _outside, new Vector3(3 * T, 0.03f, -11.4f), new Vector3(90f, 0.06f, 1.8f), Mat("M_Sidewalk", new Color(0.72f, 0.71f, 0.68f), 0.1f), false);
        }

        private static void MakeSyntyClutter(string name, string syntyPath, string display, float mass, float throwMult, float holdOffset,
            float scale, bool lieFlat = false)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Synty + syntyPath + ".prefab");
            if (src == null)
            {
                Debug.LogWarning("[HouseBuilder] Synty prefab missing: " + syntyPath);
                return;
            }

            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            foreach (Collider c in inst.GetComponentsInChildren<Collider>())
            {
                Object.DestroyImmediate(c);
            }

            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale *= scale;
            inst.transform.localRotation = Quaternion.Euler(lieFlat ? 90f : 0f, 0f, 0f);
            Bounds b = RendererBounds(root);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            FinishClutter(root, b.size, display, mass, throwMult, holdOffset);
        }

        /// Static Synty decor. snapFromWall: p is on the wall line, prop is pushed out along its yaw so its back touches the wall.
        private static GameObject PlaceSynty(string syntyPath, Transform parent, Vector3 p, float yaw, float scale,
            bool keepColliders = true, bool snapFromWall = false)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>(Synty + syntyPath + ".prefab");
            if (src == null)
            {
                Debug.LogWarning("[HouseBuilder] Synty prefab missing: " + syntyPath);
                return null;
            }

            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            inst.transform.localScale *= scale;
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 pos = p;
            if (snapFromWall)
            {
                Bounds b = RendererBounds(inst); // at origin, unrotated
                pos += rot * Vector3.forward * (-b.min.z + 0.07f);
            }

            inst.transform.SetPositionAndRotation(pos, rot);
            inst.transform.SetParent(parent, true);
            if (!keepColliders)
            {
                foreach (Collider c in inst.GetComponentsInChildren<Collider>())
                {
                    c.enabled = false;
                }
            }

            Physics.SyncTransforms();
            return inst;
        }

        // ------------------------------------------------------------------ outside / lighting / hazards

        private static void BuildOutside()
        {
            Box("Ground", _outside, new Vector3(3 * T, -0.1f, 4 * T), new Vector3(90f, 0.2f, 90f), Mat("M_Grass", new Color(0.35f, 0.62f, 0.2f), 0.05f), true);
            Box("Road", _outside, new Vector3(3 * T, 0.01f, -7.5f), new Vector3(90f, 0.02f, 6f), Mat("M_Road", new Color(0.22f, 0.22f, 0.24f), 0.2f), false);
            Box("Sidewalk", _outside, new Vector3(3 * T, 0.03f, -3.6f), new Vector3(90f, 0.06f, 1.8f), Mat("M_Sidewalk", new Color(0.72f, 0.71f, 0.68f), 0.1f), false);
            Box("Path", _outside, new Vector3(2.5f * T, 0.02f, -1.35f), new Vector3(1.5f, 0.04f, 2.7f), Mat("M_Sidewalk", new Color(0.72f, 0.71f, 0.68f), 0.1f), false);
            Box("RoadLine", _outside, new Vector3(3 * T, 0.025f, -7.5f), new Vector3(90f, 0.005f, 0.15f), Mat("M_RoadLine", new Color(0.95f, 0.85f, 0.3f), 0.2f), false);

            // the ride, idling in front of the house (visible through the front windows)
            var car = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SyntyStudios/PolygonCity/Prefabs/Vehicles/SM_Veh_Car_Sedan_01.prefab");
            if (car != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(car);
                inst.name = "RideCar";
                inst.transform.SetParent(_outside, false);
                inst.transform.SetPositionAndRotation(new Vector3(8.5f, 0.02f, -6.3f), Quaternion.Euler(0f, 270f, 0f));
                string texPath = AssetDatabase.GUIDToAssetPath("fbde5f73f39181f4d9c7e24c34f0bdf8");
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texPath);
                Material carMat = Mat("M_SyntyCity", Color.white, 0.3f, tex);
                SetAllMaterials(inst, carMat);
            }
            else
            {
                Debug.LogWarning("[HouseBuilder] Synty sedan prefab not found, skipping car.");
            }

            BuildYard();
        }

        private static void BuildLighting()
        {
            Light sun = null;
            foreach (Light l in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (l.type == LightType.Directional && !l.transform.IsChildOf(_lighting))
                {
                    sun = l;
                    break;
                }
            }

            if (sun == null)
            {
                sun = new GameObject("Sun").AddComponent<Light>();
                sun.type = LightType.Directional;
                sun.transform.SetParent(_lighting, false);
            }

            Undo.RecordObject(sun, "Sun");
            Undo.RecordObject(sun.transform, "Sun");
            sun.transform.rotation = Quaternion.Euler(35f, -35f, 0f); // mid-morning sun from the south-east, through the front windows
            sun.color = new Color(1f, 0.95f, 0.85f);
            sun.intensity = 2.7f;
            sun.shadows = LightShadows.Soft;

            void RoomLight(string name, float x, float z, float intensity, float range)
            {
                Place("lampSquareCeiling", _lighting, new Vector3(x, WallH - 0.49f, z), 0f, snap: false, onFloor: false, colliders: false);
                var go = new GameObject(name);
                go.transform.SetParent(_lighting, false);
                go.transform.position = new Vector3(x, WallH - 0.6f, z);
                var l = go.AddComponent<Light>();
                l.type = LightType.Point;
                l.color = new Color(1f, 0.92f, 0.81f);
                l.intensity = 0.9f;
                l.range = range;
                l.shadows = LightShadows.None;
            }

            RoomLight("Light_Bedroom", 4.2f, 13.65f, 2.2f, 7f);
            RoomLight("Light_Bathroom", 10.5f, 13.65f, 1.8f, 6f);
            RoomLight("Light_Hall_W", 3.15f, 9.45f, 1.4f, 5f);
            RoomLight("Light_Hall_E", 9.45f, 9.45f, 1.4f, 5f);
            RoomLight("Light_Living_W", 3.2f, 4.2f, 2.0f, 7f);
            RoomLight("Light_Living_E", 7.0f, 4.2f, 2.0f, 7f);
            RoomLight("Light_Kitchen", 10.6f, 4.2f, 2.0f, 6.5f);
        }

        private static void BuildHazards()
        {
            // bathroom puddle
            Material water = Mat("M_Puddle", new Color(0.55f, 0.75f, 0.9f), 0.97f);
            var puddleVis = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            puddleVis.name = "PuddleVisual";
            Object.DestroyImmediate(puddleVis.GetComponent<Collider>());
            puddleVis.transform.SetParent(_hazards, false);
            puddleVis.transform.position = new Vector3(10.4f, FloorTop + 0.004f, 13.4f);
            puddleVis.transform.localScale = new Vector3(1.3f, 0.003f, 0.9f);
            puddleVis.GetComponent<Renderer>().sharedMaterial = water;
            HazardTrigger("Hazard_Puddle", new Vector3(10.4f, FloorTop + 0.3f, 13.4f), new Vector3(1.2f, 0.6f, 0.8f));

            // hallway legos
            Transform lego = Group(_hazards.gameObject, "Legos");
            Color[] cols = { new Color(0.9f, 0.15f, 0.15f), new Color(0.15f, 0.4f, 0.9f), new Color(0.95f, 0.8f, 0.1f), new Color(0.2f, 0.75f, 0.3f) };
            for (int i = 0; i < 16; i++)
            {
                var b = GameObject.CreatePrimitive(PrimitiveType.Cube);
                b.name = "Lego";
                Object.DestroyImmediate(b.GetComponent<Collider>());
                b.transform.SetParent(lego, false);
                b.transform.position = new Vector3(3.2f + Rand(-0.55f, 0.55f), FloorTop + 0.02f, 9.5f + Rand(-0.45f, 0.45f));
                b.transform.rotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
                b.transform.localScale = new Vector3(0.064f, 0.04f, 0.032f);
                b.GetComponent<Renderer>().sharedMaterial = Mat("M_Lego_" + (i % 4), cols[i % 4], 0.7f);
            }

            HazardTrigger("Hazard_Legos", new Vector3(3.2f, FloorTop + 0.3f, 9.5f), new Vector3(1.2f, 0.6f, 1.0f));
        }

        private static void HazardTrigger(string name, Vector3 pos, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_hazards, false);
            go.transform.position = pos;
            var bc = go.AddComponent<BoxCollider>();
            bc.size = size;
            bc.isTrigger = true;
            go.AddComponent<SlipperyHazard>();
        }

        // ------------------------------------------------------------------ scene housekeeping

        private static void DisableOldTestObjects()
        {
            string[] names = { "Plane", "bedDouble", "bookcaseClosedWide", "cabinetBedDrawerTable", "plantSmall1", "ExitDoor" };
            var scene = EditorSceneManager.GetActiveScene();
            foreach (GameObject go in scene.GetRootGameObjects())
            {
                if (System.Array.IndexOf(names, go.name) >= 0 && go.activeSelf)
                {
                    Undo.RecordObject(go, "Disable old");
                    go.SetActive(false);
                    Debug.Log($"[HouseBuilder] Disabled old test object '{go.name}'.");
                }
            }
        }

        private static void PlacePlayer()
        {
            var pc = Object.FindFirstObjectByType<PlayerController>();
            if (pc == null)
            {
                Debug.LogWarning("[HouseBuilder] No PlayerController in scene.");
                return;
            }

            Undo.RecordObject(pc.transform, "Move player");
            pc.transform.SetPositionAndRotation(new Vector3(6.4f, FloorTop + 0.05f, 13.7f), Quaternion.Euler(0f, -150f, 0f));
        }

        private static void EnsureBuildSettings()
        {
            var list = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            list.RemoveAll(s => s.path == SceneName);
            list.Insert(0, new EditorBuildSettingsScene(SceneName, true));
            EditorBuildSettings.scenes = list.ToArray();
        }

        // ------------------------------------------------------------------ hiding spots

        private static HidingSpot Spot(Room room, ConcealmentType type, Vector3 pos, Transform parent = null)
        {
            _spotCounter++;
            var go = new GameObject($"HS_{room}_{type}_{_spotCounter:00}");
            go.transform.SetParent(parent != null ? parent : _spots, true);
            go.transform.position = pos;
            var hs = go.AddComponent<HidingSpot>();
            hs.spotID = $"{room}_{_spotCounter:00}";
            hs.room = room;
            hs.concealmentType = type;
            return hs;
        }

        /// Inside a drawer (spot follows the drawer) or behind a door (spot inside the carcass).
        private static void SpotInside(GameObject furniture, string partPrefix, Room room, ConcealmentType type = ConcealmentType.InsideDrawer)
        {
            Transform part = FindPart(furniture.transform, partPrefix);
            if (part == null)
            {
                Debug.LogWarning($"[HouseBuilder] '{furniture.name}' has no part '{partPrefix}'.");
                return;
            }

            Bounds pb = part.GetComponent<Renderer>().bounds;
            if (part.name.ToLowerInvariant().Contains("drawer"))
            {
                Spot(room, type, new Vector3(pb.center.x, pb.min.y + 0.04f, pb.center.z), part);
            }
            else
            {
                Bounds body = RendererBounds(furniture);
                Vector3 c = body.center;
                Spot(room, type, new Vector3(c.x, pb.min.y + 0.04f, c.z), furniture.transform);
            }
        }

        private static void SpotOnTop(GameObject furniture, Room room, ConcealmentType type)
        {
            Vector3 p = furniture.transform.position;
            Spot(room, type, new Vector3(p.x, TopY(furniture) + 0.01f, p.z));
        }

        private static Vector3 Floor(float x, float z) => new Vector3(x, FloorTop + 0.01f, z);

        private static Vector3 Surface(float x, float z) => new Vector3(x, SurfaceY(x, z) + 0.01f, z);

        // ------------------------------------------------------------------ clutter

        private static void Clutter(string prefabName, float x, float z, float? yaw = null)
        {
            if (!ClutterPrefabs.TryGetValue(prefabName, out GameObject prefab) || prefab == null)
            {
                Debug.LogWarning($"[HouseBuilder] Missing clutter prefab '{prefabName}'.");
                return;
            }

            float y = SurfaceY(x, z) + 0.02f;
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            inst.transform.SetParent(_clutter, false);
            inst.transform.SetPositionAndRotation(new Vector3(x, y, z), Quaternion.Euler(0f, yaw ?? Rand(0f, 360f), 0f));
            Physics.SyncTransforms();
        }

        private static void LoadClutterPrefabs()
        {
            ClutterPrefabs.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { ClutterDir.TrimEnd('/') }))
            {
                var p = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
                ClutterPrefabs[p.name] = p;
            }
        }

        private static void MakeClutter(string name, string model, string display, float mass, float throwMult, float holdOffset,
            bool lieFlat = false, float scaleMul = 1f)
        {
            var root = new GameObject(name);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel(model));
            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale *= S * scaleMul;
            inst.transform.localRotation = Quaternion.Euler(lieFlat ? 90f : 0f, 180f, 0f);
            Bounds b = RendererBounds(root);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            FinishClutter(root, b.size, display, mass, throwMult, holdOffset);
        }

        private static void MakeLaundryPile(string name, Color a, Color b, Color c)
        {
            var root = new GameObject(name);
            Color[] cols = { a, b, c, a };
            Vector3[] pos = { new Vector3(-0.1f, 0.07f, 0.02f), new Vector3(0.12f, 0.06f, -0.05f), new Vector3(0.02f, 0.13f, 0.06f), new Vector3(0.05f, 0.05f, 0.14f) };
            Vector3[] scl = { new Vector3(0.38f, 0.14f, 0.3f), new Vector3(0.32f, 0.12f, 0.34f), new Vector3(0.3f, 0.12f, 0.26f), new Vector3(0.26f, 0.1f, 0.2f) };
            for (int i = 0; i < 4; i++)
            {
                var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Object.DestroyImmediate(s.GetComponent<Collider>());
                s.name = "Cloth";
                s.transform.SetParent(root.transform, false);
                s.transform.localPosition = pos[i];
                s.transform.localScale = scl[i];
                s.transform.localRotation = Quaternion.Euler(0f, i * 40f, i * 7f);
                s.GetComponent<Renderer>().sharedMaterial = Mat($"M_Fabric_{name}_{i}", cols[i], 0.05f);
            }

            FinishClutter(root, new Vector3(0.5f, 0.22f, 0.45f), "Laundry Pile", 0.8f, 1.2f, 0f);
        }

        private static void MakeTowel(string name, Color col)
        {
            var root = new GameObject(name);
            var t = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.DestroyImmediate(t.GetComponent<Collider>());
            t.name = "Cloth";
            t.transform.SetParent(root.transform, false);
            t.transform.localPosition = new Vector3(0f, 0.025f, 0f);
            t.transform.localScale = new Vector3(0.6f, 0.05f, 0.42f);
            t.GetComponent<Renderer>().sharedMaterial = Mat("M_" + name, col, 0.05f);
            FinishClutter(root, new Vector3(0.6f, 0.05f, 0.42f), "Towel", 0.3f, 1.3f, 0f);
        }

        private static void FinishClutter(GameObject root, Vector3 size, string display, float mass, float throwMult, float holdOffset)
        {
            var bc = root.AddComponent<BoxCollider>();
            bc.size = new Vector3(Mathf.Max(size.x, 0.06f), Mathf.Max(size.y, 0.06f), Mathf.Max(size.z, 0.06f));
            bc.center = new Vector3(0f, bc.size.y * 0.5f, 0f);

            var rb = root.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            var pc = root.AddComponent<PhysicsClutter>();
            var so = new SerializedObject(pc);
            so.FindProperty("propDisplayName").stringValue = display;
            so.FindProperty("mass").floatValue = mass;
            so.FindProperty("throwForceMultiplier").floatValue = throwMult;
            so.FindProperty("holdDistanceOffset").floatValue = holdOffset;
            so.ApplyModifiedPropertiesWithoutUndo();

            string path = ClutterDir + root.name + ".prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            ClutterPrefabs[root.name] = prefab;
            Object.DestroyImmediate(root);
        }

        // ------------------------------------------------------------------ placement core

        /// Instantiates a Kenney model inside a wrapper whose origin is the model's bottom-center and whose
        /// +Z is the furniture front. p is the wall line (snap) or the center point (no snap).
        private static GameObject Place(string model, Transform parent, Vector3 p, float yaw, bool snap = true, bool onFloor = true,
            bool colliders = true, bool openables = true, float yOffset = 0f, string label = null)
        {
            var wrap = new GameObject(label ?? model);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(LoadModel(model));
            inst.transform.SetParent(wrap.transform, false);
            inst.transform.localScale *= S;
            inst.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            Bounds b = RendererBounds(wrap);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);

            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            Vector3 pos = p;
            if (snap)
            {
                pos += rot * Vector3.forward * (b.size.z * 0.5f + 0.07f);
            }

            pos.y += yOffset + (onFloor ? FloorTop : 0f);
            wrap.transform.SetPositionAndRotation(pos, rot);
            wrap.transform.SetParent(parent, true);

            if (colliders)
            {
                foreach (MeshFilter mf in wrap.GetComponentsInChildren<MeshFilter>())
                {
                    string partName = mf.name.ToLowerInvariant();
                    bool foliage = partName == "plant" || partName.Contains("leaf") || partName.Contains("leaves");
                    if (mf.sharedMesh != null && mf.GetComponent<Collider>() == null && !foliage)
                    {
                        mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
                    }
                }
            }

            if (openables)
            {
                SetupOpenables(wrap, label ?? model);
            }

            Physics.SyncTransforms();
            return wrap;
        }

        private static void SetupOpenables(GameObject wrap, string label)
        {
            Vector3 fwd = wrap.transform.forward;
            foreach (Transform t in wrap.GetComponentsInChildren<Transform>())
            {
                string n = t.name.ToLowerInvariant();
                bool isPartRoot = t == wrap.transform || t.parent == wrap.transform;
                bool isDrawer = !isPartRoot && n.Contains("drawer");
                bool isDoor = !isPartRoot && n.Contains("door") && !n.Contains("doorway");
                var mf = t.GetComponent<MeshFilter>();
                if ((!isDrawer && !isDoor) || mf == null || mf.sharedMesh == null)
                {
                    continue;
                }

                var of = t.gameObject.AddComponent<OpenableFurniture>();
                if (isDrawer)
                {
                    Bounds wb = t.GetComponent<Renderer>().bounds;
                    float depth = Mathf.Abs(fwd.x) * wb.size.x + Mathf.Abs(fwd.z) * wb.size.z;
                    Vector3 localOffset = t.parent.InverseTransformVector(fwd * depth * 0.7f);
                    of.Configure(label + " Drawer", OpenMode.Slide, localOffset, Vector3.up, 0f);
                }
                else
                {
                    Vector3 axis = t.InverseTransformDirection(Vector3.up);
                    Quaternion closed = t.localRotation;
                    Vector3 c0 = t.TransformPoint(mf.sharedMesh.bounds.center);
                    t.localRotation = closed * Quaternion.AngleAxis(100f, axis);
                    float plus = Vector3.Dot(t.TransformPoint(mf.sharedMesh.bounds.center) - c0, fwd);
                    t.localRotation = closed * Quaternion.AngleAxis(-100f, axis);
                    float minus = Vector3.Dot(t.TransformPoint(mf.sharedMesh.bounds.center) - c0, fwd);
                    t.localRotation = closed;
                    of.Configure(label + " Door", OpenMode.Hinge, Vector3.zero, axis, plus >= minus ? 100f : -100f);
                }

                var rb = t.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.useGravity = false;
            }
        }

        // ------------------------------------------------------------------ helpers

        private static Vector3 V(float x, float z) => new Vector3(x, 0f, z);

        private static Room RoomAt(float x, float z)
        {
            if (z < 4 * T) return x >= 4 * T ? Room.Kitchen : Room.LivingRoom;
            if (z < 5 * T) return Room.Hallway;
            return x < 4 * T ? Room.Bedroom : Room.Bathroom;
        }

        private static float SurfaceY(float x, float z)
        {
            Physics.SyncTransforms();
            var hits = Physics.RaycastAll(new Vector3(x, WallH - 0.1f, z), Vector3.down, WallH + 1f, ~0, QueryTriggerInteraction.Ignore);
            float best = FloorTop;
            float bestDist = float.MaxValue;
            foreach (RaycastHit h in hits)
            {
                if (h.collider.transform.IsChildOf(_lighting)) continue;
                if (h.distance < bestDist)
                {
                    bestDist = h.distance;
                    best = h.point.y;
                }
            }

            return best;
        }

        private static float TopY(GameObject go) => RendererBounds(go).max.y;

        private static Transform FindPart(Transform root, string prefix)
        {
            string p = prefix.ToLowerInvariant();
            foreach (Transform t in root.GetComponentsInChildren<Transform>())
            {
                if (t.name.ToLowerInvariant().StartsWith(p) && t.GetComponent<Renderer>() != null) return t;
            }

            return null;
        }

        private static Bounds RendererBounds(GameObject go)
        {
            bool has = false;
            Bounds b = new Bounds(go.transform.position, Vector3.zero);
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                if (!has) { b = r.bounds; has = true; }
                else b.Encapsulate(r.bounds);
            }

            return b;
        }

        private static GameObject LoadModel(string model)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(KenneyDir + model + ".fbx");
            if (go == null) throw new FileNotFoundException("Kenney model not found: " + model);
            return go;
        }

        private static Transform Group(GameObject parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent.transform, false);
            return g.transform;
        }

        private static GameObject Box(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            go.transform.localScale = size;
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
            else Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            return go;
        }

        private static void SetAllMaterials(GameObject go, Material m)
        {
            foreach (Renderer r in go.GetComponentsInChildren<Renderer>())
            {
                var mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = m;
                r.sharedMaterials = mats;
            }
        }

        private static Material Mat(string name, Color c, float smooth, Texture tex = null)
        {
            EnsureFolder(MatDir.TrimEnd('/'));
            string path = MatDir + name + ".mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(m, path);
            }

            m.SetColor("_BaseColor", c);
            m.SetFloat("_Smoothness", smooth);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static float Rand(float a, float b) => a + (float)_rng.NextDouble() * (b - a);
    }
}
