using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WheresMyLeftSock.Items;
using WheresMyLeftSock.World;
using WheresMyLeftSock.World.Generation;

namespace WheresMyLeftSock.EditorTools
{
    /// <summary>
    /// Source of truth for the furniture generator's data:
    ///  - CATALOG: every piece (Kenney model, rooms, clearance, hiding spots...) -> baked prefab + FurnitureDef
    ///  - RECIPES: what each room is furnished with and how the pieces relate
    /// Menu: Tidy Chaos/Generation/Build Furniture Catalog. Re-running rebuilds prefabs, defs and recipes.
    /// </summary>
    public static class FurnitureCatalog
    {
        public const string PrefabDir = "Assets/_Project/Prefabs/Furniture/Generated";
        public const string DefDir = "Assets/_Project/Data/Generation/Furniture";
        public const string RecipeDir = "Assets/_Project/Data/Generation/Recipes";

        private enum SpotKind { InsidePart, Surface, Floor }

        private sealed class SpotSpec
        {
            public ConcealmentType type;
            public SpotKind kind;
            public string part;
            public float x, z; // local, +z = front
        }

        private sealed class Entry
        {
            public string id, model, label;
            public GenLayer layer = GenLayer.Base;
            public RoomMask rooms;
            public float clearance, baseHeight, stackHeight;
            public float gap = 0.12f;   // breathing room to unrelated furniture
            public float sideAccess;    // beds: one long side kept free
            public bool surface;        // loose props may be set on top
            public float privacy;       // +1 far from the entrance .. -1 near it
            public bool hidden;         // never visible straight through a doorway
            public float doorWall;      // min distance to a doorway on the same wall (99 = never on a door wall)
            public bool centered;       // centred on its wall span / under a window
            public bool offLanes;       // stay out of walking lanes
            public bool fitted;         // may sit flush in a fitted run (sink beside the washer)
            public float modelYaw;      // extra rotation for Kenney models that are not modelled facing +Z (toilet)
            public string synty;        // Synty prefab path (under Assets/SyntyStudios/, no extension) instead of a Kenney model
            public float syntyScale = 1f;
            public string clutter;      // an existing physics clutter prefab (loose prop) instead of a model
            public float cover;         // loose props: chance of an 'under clutter' hiding spot beneath it
            public ConcealmentType? marker; // invisible floor marker carrying one hiding spot
            public float tip;           // loose props: chance knocked over
            public float jitter;        // free-standing pieces / rugs: random yaw (degrees)
            public string hazard;       // level 4: "puddle" or "legos"
            public bool rug, colliders = true;
            public int maxPerHouse = 99;
            public SpotSpec[] spots = new SpotSpec[0];
            public BundleSpec[] bundle = new BundleSpec[0];
        }

        /// A prop that comes with the piece (see BundleItem). x/z local, +z = the piece's front.
        private sealed class BundleSpec
        {
            public string[] prefabs;   // clutter prefab names (one is picked)
            public float x, z, yaw, jitter, chance = 1f;
            public bool floor;
            public float scatter, tip;   // mess: random offset (m), chance knocked over

            public BundleSpec Messy(float scatterM, float tipChance = 0f)
            {
                scatter = scatterM;
                tip = tipChance;
                return this;
            }
        }

        private static BundleSpec OnTop(float x, float z, float chance, float yaw = 0f, float jitter = 20f, params string[] prefabs) =>
            new BundleSpec { prefabs = prefabs, x = x, z = z, yaw = yaw, jitter = jitter, chance = chance };

        private static BundleSpec AtFloor(float x, float z, float yaw, float chance, float jitter = 12f, params string[] prefabs) =>
            new BundleSpec { prefabs = prefabs, x = x, z = z, yaw = yaw, jitter = jitter, chance = chance, floor = true };

        private static SpotSpec Inside(string part, ConcealmentType t = ConcealmentType.InsideDrawer) => new SpotSpec { type = t, kind = SpotKind.InsidePart, part = part };
        private static SpotSpec Top(ConcealmentType t, float x = 0f, float z = 0f) => new SpotSpec { type = t, kind = SpotKind.Surface, x = x, z = z };
        private static SpotSpec OnFloor(ConcealmentType t, float x, float z) => new SpotSpec { type = t, kind = SpotKind.Floor, x = x, z = z };

        // ------------------------------------------------------------------ CATALOG (level 1: base furniture)

        private static List<Entry> Catalog() => new List<Entry>
        {
            // bedroom: bed is the private anchor - not on the door wall, centred on its wall or under a window,
            // one long side free to get in
            new Entry { id = "bed_double", model = "bedDouble", label = "Bed", rooms = RoomMask.Bedroom, clearance = 0.6f, maxPerHouse = 1,
                gap = 0.25f, sideAccess = 0.55f, privacy = 0.6f, doorWall = 1.2f, centered = true,
                spots = new[] { Top(ConcealmentType.UnderClutter, -0.35f, 0.1f) },
                // throw pillows (the first one hides the under-pillow spot), sometimes the teddy
                bundle = new[] { OnTop(-0.35f, 0.1f, 0.85f, 0f, 40f, "Pillow", "PillowBlue").Messy(0.4f), OnTop(0.4f, -0.1f, 0.55f, 0f, 40f, "PillowBlue", "Pillow").Messy(0.6f),
                                 OnTop(0.5f, -0.75f, 0.3f, 0f, 25f, "Bear").Messy(0.3f) } },
            new Entry { id = "nightstand", model = "cabinetBedDrawer", label = "Nightstand", rooms = RoomMask.Bedroom, clearance = 0.5f,
                gap = 0.1f, surface = true, spots = new[] { Inside("drawer") },
                bundle = new[] { OnTop(0f, -0.05f, 0.7f, 0f, 30f, "LampTable").Messy(0f, 0.12f) } },
            new Entry { id = "desk", model = "desk", label = "Desk", rooms = RoomMask.Bedroom, clearance = 0.7f,
                gap = 0.15f, surface = true, doorWall = 0.6f,
                spots = new[] { Inside("drawer"), Top(ConcealmentType.ObviousDecoySpot, 0.45f, -0.15f) },
                // the work set: chair pulled up, laptop, a plant (the right side stays free for the decoy spot)
                bundle = new[] { AtFloor(0f, 0.62f, 180f, 0.95f, 25f, "ChairDesk").Messy(0.3f, 0.12f), OnTop(-0.2f, -0.02f, 0.85f, 0f, 12f, "Laptop").Messy(0.1f),
                                 OnTop(-0.6f, -0.25f, 0.6f, 0f, 180f, "PlantSmall2", "PlantSmall1", "PlantSmall3") } },
            new Entry { id = "rug_rect", model = "rugRectangle", label = "Rug", rooms = RoomMask.Bedroom | RoomMask.Hallway, rug = true, colliders = false, jitter = 7f },

            // bathroom: privacy gradient - sink by the entrance, tub and toilet at the far end,
            // toilet never on a door wall and never in view through a doorway
            new Entry { id = "bathtub", model = "bathtub", label = "Bathtub", rooms = RoomMask.Bathroom, clearance = 0.6f, maxPerHouse = 1,
                gap = 0.12f, privacy = 0.7f, doorWall = 0.6f, spots = new[] { Top(ConcealmentType.FloorHidden) } },
            new Entry { id = "toilet", model = "toilet", label = "Toilet", rooms = RoomMask.Bathroom, clearance = 0.6f, maxPerHouse = 1,
                gap = 0.25f, privacy = 1f, hidden = true, doorWall = 99f, modelYaw = 90f, // Kenney toilet is modelled sideways
                spots = new[] { Top(ConcealmentType.ObviousDecoySpot, 0f, 0.12f) } },
            new Entry { id = "bath_sink", model = "bathroomSinkSquare", label = "Sink", rooms = RoomMask.Bathroom, clearance = 0.6f, maxPerHouse = 1,
                gap = 0.2f, privacy = -0.8f, fitted = true },
            new Entry { id = "bath_mirror", model = "bathroomMirror", label = "Mirror", rooms = RoomMask.Bathroom, baseHeight = 1.25f },
            new Entry { id = "bath_mat", model = "rugDoormat", label = "Bath Mat", rooms = RoomMask.Bathroom, rug = true, colliders = false, jitter = 12f },

            // living room: TV centred on its wall, seating group off the walking lanes
            new Entry { id = "tv_cabinet", model = "cabinetTelevisionDoors", label = "TV Cabinet", rooms = RoomMask.LivingRoom, clearance = 0.6f,
                stackHeight = 0.95f, maxPerHouse = 1, gap = 0.15f, surface = true, doorWall = 0.8f, centered = true,
                spots = new[] { Inside("door") } },
            new Entry { id = "tv", model = "televisionModern", label = "TV", rooms = RoomMask.LivingRoom, maxPerHouse = 1 },
            new Entry { id = "speaker", model = "speaker", label = "Speaker", rooms = RoomMask.LivingRoom, gap = 0.05f },
            new Entry { id = "sofa", model = "loungeSofa", label = "Sofa", rooms = RoomMask.LivingRoom, maxPerHouse = 1, gap = 0.3f, offLanes = true, jitter = 4f,
                spots = new[] { Top(ConcealmentType.UnderClutter, 0f, 0.05f), OnFloor(ConcealmentType.FloorHidden, 0f, -0.75f) },
                bundle = new[] { OnTop(0f, 0.05f, 0.75f, 0f, 15f, "PillowLong", "PillowBlueLong").Messy(0.25f), OnTop(-0.72f, 0.05f, 0.6f, 0f, 35f, "Pillow").Messy(0.5f),
                                 OnTop(0.72f, 0.05f, 0.6f, 0f, 35f, "PillowBlue").Messy(0.5f) } },
            new Entry { id = "coffee_table", model = "tableCoffee", label = "Coffee Table", rooms = RoomMask.LivingRoom, jitter = 8f,
                gap = 0.25f, surface = true, offLanes = true, spots = new[] { Top(ConcealmentType.ObviousDecoySpot) },
                bundle = new[] { OnTop(-0.45f, 0.1f, 0.7f, 0f, 35f, "Books", "BookSmall").Messy(0.3f), OnTop(0.45f, -0.05f, 0.6f, 0f, 35f, "Radio", "Newspaper").Messy(0.25f) } },
            new Entry { id = "armchair", model = "loungeChair", label = "Armchair", rooms = RoomMask.LivingRoom, clearance = 0.3f, gap = 0.2f, offLanes = true, jitter = 14f,
                bundle = new[] { OnTop(0f, 0.05f, 0.3f, 0f, 20f, "Bear") } },
            new Entry { id = "rug_round", model = "rugRound", label = "Rug", rooms = RoomMask.LivingRoom, rug = true, colliders = false, jitter = 10f },

            // kitchen (counter modules share the 0.9m grid; run rules live in CounterRunStrategy)
            new Entry { id = "k_cabinet", model = "kitchenCabinet", label = "Kitchen Cabinet", rooms = RoomMask.Kitchen, clearance = 0.9f,
                gap = 0.05f, surface = true, spots = new[] { Inside("door"), Top(ConcealmentType.ObviousDecoySpot) },
                bundle = new[] { OnTop(0.22f, -0.22f, 0.3f, 0f, 20f, "Toaster", "Blender", "CheeseWheel", "FryingPan", "PlantSmall1").Messy(0.1f) } },
            new Entry { id = "k_cabinet_drawer", model = "kitchenCabinetDrawer", label = "Kitchen Cabinet", rooms = RoomMask.Kitchen, clearance = 0.9f,
                gap = 0.05f, surface = true, spots = new[] { Inside("drawer"), Top(ConcealmentType.ObviousDecoySpot) },
                bundle = new[] { OnTop(0.22f, -0.22f, 0.3f, 0f, 20f, "Toaster", "Blender", "CheeseWheel", "FryingPan", "PlantSmall1").Messy(0.1f) } },
            new Entry { id = "k_stove", model = "kitchenStove", label = "Oven", rooms = RoomMask.Kitchen, clearance = 0.9f, maxPerHouse = 1, gap = 0.05f },
            new Entry { id = "k_sink", model = "kitchenSink", label = "Sink Cabinet", rooms = RoomMask.Kitchen, clearance = 0.9f, maxPerHouse = 1,
                gap = 0.05f, surface = true },
            new Entry { id = "k_fridge", model = "kitchenFridgeLarge", label = "Fridge", rooms = RoomMask.Kitchen, clearance = 0.9f, maxPerHouse = 1,
                gap = 0.05f, spots = new[] { Inside("doorLeft"), Top(ConcealmentType.HighShelf) } },
            new Entry { id = "k_hood", model = "hoodModern", label = "Hood", rooms = RoomMask.Kitchen, baseHeight = 1.55f, colliders = false },
            new Entry { id = "k_upper", model = "kitchenCabinetUpper", label = "Upper Cabinet", rooms = RoomMask.Kitchen, baseHeight = 1.45f,
                spots = new[] { Inside("door", ConcealmentType.HighShelf) } },
            new Entry { id = "k_upper_double", model = "kitchenCabinetUpperDouble", label = "Upper Cabinet", rooms = RoomMask.Kitchen, baseHeight = 1.45f,
                spots = new[] { Inside("doorLeft", ConcealmentType.HighShelf) } },
            new Entry { id = "k_microwave", model = "kitchenMicrowave", label = "Microwave", rooms = RoomMask.Kitchen, maxPerHouse = 1 },
            new Entry { id = "k_table", model = "table", label = "Kitchen Table", rooms = RoomMask.Kitchen, jitter = 8f,
                gap = 0.6f, surface = true, offLanes = true, spots = new[] { Top(ConcealmentType.UnderClutter, 0f, -0.2f) },
                // dining set: chairs on both long sides (dropped where the table meets a wall), sometimes a stool at the end
                bundle = new[] { AtFloor(-0.45f, 0.72f, 180f, 0.85f, 12f, "Chair", "ChairCushion").Messy(0.22f, 0.15f), AtFloor(0.45f, 0.72f, 180f, 0.7f, 12f, "Chair", "ChairCushion").Messy(0.22f, 0.15f),
                                 AtFloor(-0.45f, -0.72f, 0f, 0.7f, 12f, "Chair", "ChairCushion").Messy(0.22f, 0.15f), AtFloor(0.45f, -0.72f, 0f, 0.85f, 12f, "Chair", "ChairCushion").Messy(0.22f, 0.15f),
                                 AtFloor(1.12f, 0f, 270f, 0.25f, 15f, "StoolBar").Messy(0.2f, 0.35f), OnTop(0.5f, 0.18f, 0.4f, 0f, 60f, "Burger") } },

            // hallway: shoe bench by the way out
            new Entry { id = "shoe_bench", model = "benchCushionLow", label = "Shoe Bench", rooms = RoomMask.Hallway, clearance = 0.4f,
                gap = 0.15f, surface = true, privacy = -1f, spots = new[] { Top(ConcealmentType.ObviousDecoySpot) } },

            // ---------------- level 2: hiding furniture (standalone storage) ----------------
            // tall storage likes corners and may stand flush beside other storage (fitted / built-in look);
            // being tall, the window rule keeps it off every window
            new Entry { id = "wardrobe", model = "bookcaseClosedDoors", label = "Wardrobe", layer = GenLayer.Hiding, rooms = RoomMask.Bedroom,
                clearance = 0.8f, gap = 0.1f, fitted = true, doorWall = 0.4f,
                spots = new[] { Inside("door"), Top(ConcealmentType.HighShelf) } },
            new Entry { id = "coat_closet", model = "bookcaseClosedDoors", label = "Coat Closet", layer = GenLayer.Hiding, rooms = RoomMask.Hallway,
                clearance = 0.8f, gap = 0.1f, fitted = true, privacy = -1f,
                spots = new[] { Inside("door"), Top(ConcealmentType.HighShelf) } },
            new Entry { id = "bookcase_open", model = "bookcaseOpen", label = "Bookshelf", layer = GenLayer.Hiding,
                rooms = RoomMask.Bedroom | RoomMask.LivingRoom, clearance = 0.5f, gap = 0.1f, fitted = true,
                spots = new[] { Top(ConcealmentType.HighShelf) } },
            new Entry { id = "bookcase_wide", model = "bookcaseClosedWide", label = "Bookcase", layer = GenLayer.Hiding,
                rooms = RoomMask.Bedroom | RoomMask.LivingRoom, clearance = 0.5f, gap = 0.1f, fitted = true, centered = true,
                spots = new[] { Top(ConcealmentType.HighShelf) } },
            new Entry { id = "vanity", model = "bathroomCabinetDrawer", label = "Vanity", layer = GenLayer.Hiding, rooms = RoomMask.Bathroom,
                clearance = 0.6f, gap = 0.12f, fitted = true, surface = true, privacy = -0.6f,
                spots = new[] { Inside("drawerTop"), Inside("drawerBottom") },
                bundle = new[] { OnTop(-0.25f, 0.05f, 0.6f, 0f, 180f, "PlantSmall3", "PlantSmall1") } },
            new Entry { id = "washer", model = "washerDryerStacked", label = "Washer", layer = GenLayer.Hiding, rooms = RoomMask.Bathroom,
                clearance = 0.7f, gap = 0.1f, fitted = true, privacy = -0.3f,
                spots = new[] { Inside("washerDoor"), Top(ConcealmentType.HighShelf) } },
            new Entry { id = "side_table", model = "sideTableDrawers", label = "Side Table", layer = GenLayer.Hiding,
                rooms = RoomMask.Hallway | RoomMask.LivingRoom | RoomMask.Bedroom, clearance = 0.5f, gap = 0.15f, surface = true, centered = true,
                spots = new[] { Inside("drawerLeft") },
                bundle = new[] { OnTop(-0.35f, 0f, 0.6f, 0f, 180f, "PlantSmall1", "PlantSmall2"), OnTop(0.3f, 0.02f, 0.4f, 0f, 30f, "BookSmall", "Books") } },

            // ---------------- level 3: props ----------------
            // standing decor (static): lamps, plants, coat stand
            new Entry { id = "floor_lamp_round", model = "lampRoundFloor", label = "Floor Lamp", layer = GenLayer.Props,
                rooms = RoomMask.LivingRoom | RoomMask.Bedroom, gap = 0.08f },
            new Entry { id = "floor_lamp_square", model = "lampSquareFloor", label = "Floor Lamp", layer = GenLayer.Props,
                rooms = RoomMask.Bedroom | RoomMask.LivingRoom, gap = 0.08f },
            new Entry { id = "potted_plant", model = "pottedPlant", label = "Plant", layer = GenLayer.Props,
                rooms = RoomMask.LivingRoom | RoomMask.Hallway | RoomMask.Bedroom | RoomMask.Kitchen, gap = 0.08f },
            new Entry { id = "big_plant_a", synty = "PolygonCity/Prefabs/Props/SM_Prop_PotPlant_01", syntyScale = 0.8f, label = "Plant",
                layer = GenLayer.Props, rooms = RoomMask.LivingRoom | RoomMask.Hallway | RoomMask.Bedroom, gap = 0.08f },
            new Entry { id = "big_plant_b", synty = "PolygonCity/Prefabs/Props/SM_Prop_PotPlant_02", syntyScale = 0.9f, label = "Plant",
                layer = GenLayer.Props, rooms = RoomMask.LivingRoom | RoomMask.Hallway, gap = 0.08f },
            new Entry { id = "coat_stand", model = "coatRackStanding", label = "Coat Rack", layer = GenLayer.Props,
                rooms = RoomMask.LivingRoom | RoomMask.Hallway, gap = 0.1f, privacy = -1f },
            new Entry { id = "doormat", model = "rugDoormat", label = "Doormat", layer = GenLayer.Props, rooms = RoomMask.LivingRoom, jitter = 6f,
                rug = true, colliders = false, spots = new[] { Top(ConcealmentType.ObviousDecoySpot) } },

            // wall decor (upper band: never over windows, doors or tall storage)
            new Entry { id = "coat_hooks", model = "coatRack", label = "Coat Hooks", layer = GenLayer.Props, baseHeight = 1.45f,
                rooms = RoomMask.Hallway | RoomMask.LivingRoom, privacy = -1f },
            new Entry { id = "wall_lamp", model = "lampWall", label = "Wall Lamp", layer = GenLayer.Props, baseHeight = 1.7f, colliders = false,
                rooms = RoomMask.Hallway, centered = true },
            new Entry { id = "poster_a", synty = "PolygonCity/Prefabs/Props/SM_Prop_Poster_01", label = "Poster", layer = GenLayer.Props,
                baseHeight = 1.15f, colliders = false, rooms = RoomMask.Bedroom | RoomMask.LivingRoom | RoomMask.Hallway },
            new Entry { id = "poster_b", synty = "PolygonCity/Prefabs/Props/SM_Prop_Poster_02", label = "Poster", layer = GenLayer.Props,
                baseHeight = 1.15f, colliders = false, rooms = RoomMask.Bedroom | RoomMask.LivingRoom | RoomMask.Hallway },
            new Entry { id = "poster_c", synty = "PolygonCity/Prefabs/Props/SM_Prop_Poster_03", syntyScale = 0.8f, label = "Poster", layer = GenLayer.Props,
                baseHeight = 1.2f, colliders = false, rooms = RoomMask.Bedroom | RoomMask.LivingRoom | RoomMask.Hallway },
            new Entry { id = "poster_frame", synty = "PolygonCity/Prefabs/Props/SM_Prop_Poster_Frame_01", label = "Picture", layer = GenLayer.Props,
                baseHeight = 1.1f, colliders = false, rooms = RoomMask.LivingRoom | RoomMask.Bedroom },

            // loose clutter (physics, pushable) - cover = chance of an 'under clutter' hiding spot beneath it
            new Entry { id = "laundry_a", clutter = "LaundryPile_A", label = "Laundry Pile", layer = GenLayer.Props, cover = 0.6f,
                rooms = RoomMask.Bedroom | RoomMask.Bathroom | RoomMask.Hallway | RoomMask.LivingRoom },
            new Entry { id = "laundry_b", clutter = "LaundryPile_B", label = "Laundry Pile", layer = GenLayer.Props, cover = 0.6f,
                rooms = RoomMask.Bedroom | RoomMask.Bathroom | RoomMask.Hallway | RoomMask.LivingRoom },
            new Entry { id = "laundry_c", clutter = "LaundryPile_C", label = "Laundry Pile", layer = GenLayer.Props, cover = 0.6f,
                rooms = RoomMask.Bedroom | RoomMask.Bathroom | RoomMask.Hallway | RoomMask.LivingRoom | RoomMask.Kitchen },
            new Entry { id = "towel_blue", clutter = "Towel_Blue", label = "Towel", layer = GenLayer.Props, cover = 0.6f, rooms = RoomMask.Bathroom },
            new Entry { id = "towel_pink", clutter = "Towel_Pink", label = "Towel", layer = GenLayer.Props, cover = 0.6f, rooms = RoomMask.Bathroom },
            new Entry { id = "trashcan", clutter = "Trashcan", label = "Trash Can", layer = GenLayer.Props, cover = 0.5f, tip = 0.3f,
                rooms = RoomMask.Bathroom | RoomMask.Kitchen },
            new Entry { id = "box_closed", clutter = "BoxClosed", label = "Box", layer = GenLayer.Props, cover = 0.4f, tip = 0.25f,
                rooms = RoomMask.LivingRoom | RoomMask.Hallway | RoomMask.Bedroom,
                bundle = new[] { OnTop(0f, 0f, 0.3f, 0f, 25f, "BoxOpen", "BoxClosed") } }, // sometimes a stack
            new Entry { id = "box_open", clutter = "BoxOpen", label = "Box", layer = GenLayer.Props, cover = 0.4f, tip = 0.3f,
                rooms = RoomMask.LivingRoom | RoomMask.Hallway | RoomMask.Bedroom },
            new Entry { id = "moving_box", clutter = "MovingBox", label = "Moving Box", layer = GenLayer.Props, tip = 0.2f,
                rooms = RoomMask.LivingRoom | RoomMask.Hallway },
            new Entry { id = "laundry_basket", clutter = "LaundryBasket", label = "Laundry Basket", layer = GenLayer.Props, tip = 0.35f,
                rooms = RoomMask.Bedroom | RoomMask.Bathroom },
            new Entry { id = "laundry_sack", clutter = "LaundrySack", label = "Laundry Bag", layer = GenLayer.Props,
                rooms = RoomMask.Bathroom | RoomMask.Bedroom | RoomMask.Hallway },
            new Entry { id = "bat", clutter = "BaseballBat", label = "Baseball Bat", layer = GenLayer.Props, rooms = RoomMask.Bedroom | RoomMask.Hallway },
            new Entry { id = "newspaper", clutter = "Newspaper", label = "Newspaper", layer = GenLayer.Props,
                rooms = RoomMask.LivingRoom | RoomMask.Bedroom | RoomMask.Hallway },
            new Entry { id = "book_floor", clutter = "BookSmall", label = "Book", layer = GenLayer.Props,
                rooms = RoomMask.Bedroom | RoomMask.LivingRoom | RoomMask.Hallway },
            new Entry { id = "pumpkin", clutter = "Pumpkin", label = "Pumpkin", layer = GenLayer.Props, rooms = RoomMask.LivingRoom | RoomMask.Kitchen },
            new Entry { id = "vase", clutter = "Vase", label = "Vase", layer = GenLayer.Props, tip = 0.5f, rooms = RoomMask.LivingRoom },
            new Entry { id = "old_tv", clutter = "TVVintage", label = "Old TV", layer = GenLayer.Props, tip = 0.25f, rooms = RoomMask.LivingRoom | RoomMask.Bedroom },
            new Entry { id = "speaker_small", clutter = "SpeakerSmall", label = "Speaker", layer = GenLayer.Props, tip = 0.3f,
                rooms = RoomMask.LivingRoom | RoomMask.Bedroom },

            // floor hiding spots without a prop: tucked into a nook / in plain view from a doorway
            new Entry { id = "spot_tucked", marker = ConcealmentType.FloorHidden, label = "Floor Spot", layer = GenLayer.Props, rooms = RoomMask.All },
            new Entry { id = "spot_decoy", marker = ConcealmentType.ObviousDecoySpot, label = "Decoy Spot", layer = GenLayer.Props, rooms = RoomMask.All },

            // spill clutter: what ends up strewn around a mess pile / fallen off the sofa / dropped round the table
            new Entry { id = "pillow_floor", clutter = "Pillow", label = "Pillow", layer = GenLayer.Props, cover = 0.4f,
                rooms = RoomMask.Bedroom | RoomMask.LivingRoom },
            new Entry { id = "pillow_blue_floor", clutter = "PillowBlue", label = "Pillow", layer = GenLayer.Props, cover = 0.4f,
                rooms = RoomMask.Bedroom | RoomMask.LivingRoom },
            new Entry { id = "bear_floor", clutter = "Bear", label = "Teddy Bear", layer = GenLayer.Props, tip = 0.5f, rooms = RoomMask.Bedroom | RoomMask.LivingRoom },
            new Entry { id = "books_floor", clutter = "Books", label = "Books", layer = GenLayer.Props, tip = 0.4f,
                rooms = RoomMask.Bedroom | RoomMask.LivingRoom | RoomMask.Hallway },
            new Entry { id = "burger", clutter = "Burger", label = "Burger", layer = GenLayer.Props, rooms = RoomMask.Kitchen | RoomMask.LivingRoom },
            new Entry { id = "cheese", clutter = "CheeseWheel", label = "Cheese", layer = GenLayer.Props, tip = 0.4f, rooms = RoomMask.Kitchen },
            new Entry { id = "pan", clutter = "FryingPan", label = "Frying Pan", layer = GenLayer.Props, rooms = RoomMask.Kitchen },

            // ---------------- level 4: hazards ----------------
            // floor layer like a rug, but never in a doorway zone, on a rug, under furniture or clutter, or on the spawn
            new Entry { id = "puddle", hazard = "puddle", label = "Puddle", layer = GenLayer.Obstacles, rooms = RoomMask.Bathroom | RoomMask.Kitchen },
            new Entry { id = "legos", hazard = "legos", label = "Legos", layer = GenLayer.Obstacles,
                rooms = RoomMask.Hallway | RoomMask.LivingRoom | RoomMask.Bedroom, jitter = 180f },
        };

        // ------------------------------------------------------------------ RECIPES

        private static RecipeEntry E(string key, PlacementKind kind, params string[] defs) =>
            new RecipeEntry { key = key, placement = kind, options = System.Array.ConvertAll(defs, LoadDef) };

        private static RecipeEntry Rel(string key, string anchor, RelationKind rel, float min, float max, params string[] defs)
        {
            RecipeEntry e = E(key, PlacementKind.Relative, defs);
            e.anchorKey = anchor;
            e.relation = rel;
            e.minDistance = min;
            e.maxDistance = max;
            return e;
        }

        private static RecipeEntry Count(this RecipeEntry e, int min, int max, float chance = 1f)
        {
            e.min = min;
            e.max = max;
            e.chance = chance;
            return e;
        }

        private static Dictionary<Room, List<RecipeEntry>> Recipes()
        {
            var r = new Dictionary<Room, List<RecipeEntry>>();

            // Bed first (biggest), headboard to a wall; nightstands flush beside it; desk likes a window.
            r[Room.Bedroom] = new List<RecipeEntry>
            {
                E("bed", PlacementKind.Wall, "bed_double").With(e => e.avoidFacingDoorway = true), // feet never point at a door
                Rel("nightstand", "bed", RelationKind.BesideFlush, 0.03f, 0.03f, "nightstand").Count(1, 2, 0.75f),
                E("desk", PlacementKind.Wall, "desk").Count(0, 1, 0.9f).With(e => e.preferWindow = true),
                Rel("rug", "bed", RelationKind.InFront, -0.7f, -0.3f, "rug_rect").Count(0, 1, 0.85f),
                // level 2: a wardrobe (sometimes two), then shelving in the walls that are left
                E("wardrobe", PlacementKind.Wall, "wardrobe").Count(1, 2, 0.5f).With(e => e.preferCorner = true),
                E("shelving", PlacementKind.Wall, "bookcase_open", "bookcase_wide").Count(1, 2, 0.6f).With(e => e.preferCorner = true),
            };

            // Tub likes a corner at the far end, toilet hidden from every doorway, sink by the entrance,
            // mirror always above the sink.
            r[Room.Bathroom] = new List<RecipeEntry>
            {
                E("toilet", PlacementKind.Wall, "toilet").With(e => e.avoidFacingDoorway = true),
                E("tub", PlacementKind.Wall, "bathtub").With(e => e.preferCorner = true),  // the toilet chooses first: most private and most constrained
                E("sink", PlacementKind.Wall, "bath_sink"),
                Rel("mirror", "sink", RelationKind.Above, 0f, 0f, "bath_mirror"),
                Rel("mat", "tub", RelationKind.InFront, 0.02f, 0.12f, "bath_mat").Count(0, 1, 0.9f),
                // level 2: the utility side - washer stack, vanity (both fitted, so they can line up with the sink)
                E("washer", PlacementKind.Wall, "washer").With(e => e.preferCorner = true),
                E("vanity", PlacementKind.Wall, "vanity").Count(0, 1, 0.85f),
            };

            // TV set on a wall, sofa facing it, coffee table between, rug under it, armchair at its end.
            r[Room.LivingRoom] = new List<RecipeEntry>
            {
                E("tv", PlacementKind.Wall, "tv_cabinet"),
                Rel("tv_set", "tv", RelationKind.OnTop, 0f, 0f, "tv"),
                Rel("speakers", "tv", RelationKind.BesideFlush, 0.08f, 0.08f, "speaker").Count(0, 2, 0.7f),
                Rel("sofa", "tv", RelationKind.FacingAcross, 2.0f, 3.4f, "sofa"),
                Rel("coffee", "sofa", RelationKind.InFront, 0.35f, 0.6f, "coffee_table").Count(0, 1, 0.95f),
                Rel("rug", "coffee", RelationKind.Under, 0f, 0f, "rug_round").Count(0, 1, 0.85f),
                Rel("armchair", "coffee", RelationKind.SideOf, 0.3f, 0.6f, "armchair").Count(0, 1, 0.8f),
                // level 2: shelving on a free wall, maybe a side table
                E("shelving", PlacementKind.Wall, "bookcase_open", "bookcase_wide").Count(0, 2, 0.75f).With(e => e.preferCorner = true),
                E("side_table", PlacementKind.Wall, "side_table").Count(0, 1, 0.35f),
            };

            // One continuous counter run (see CounterRunStrategy), then a table in what's left.
            var counter = E("counter", PlacementKind.CounterRun);
            counter.counter = new CounterRunSettings
            {
                fridge = LoadDef("k_fridge"),
                stove = LoadDef("k_stove"),
                sink = LoadDef("k_sink"),
                hood = LoadDef("k_hood"),
                microwave = LoadDef("k_microwave"),
                cabinets = new[] { LoadDef("k_cabinet"), LoadDef("k_cabinet_drawer") },
                uppers = new[] { LoadDef("k_upper"), LoadDef("k_upper_double") },
                minModules = 4,
                maxModules = 9,
                lShapeChance = 0.5f,
                legMax = 3,
                upperChance = 0.65f,
                microwaveChance = 0.75f
            };
            counter.options = new[] { LoadDef("k_cabinet") }; // layer lookup
            r[Room.Kitchen] = new List<RecipeEntry>
            {
                counter,
                E("table", PlacementKind.Free, "k_table").Count(0, 1, 0.9f),
            };

            // Bench by the way out (privacy -1 pulls it to the living-room door), runner rug down the middle.
            r[Room.Hallway] = new List<RecipeEntry>
            {
                E("bench", PlacementKind.Wall, "shoe_bench"),
                E("runner", PlacementKind.Free, "rug_rect").Count(0, 1, 0.85f).With(e => { e.alignLongAxis = true; e.preferCenterLine = true; }),
                // level 2: coat closet by the way out, a side table centred on a free stretch
                E("coat_closet", PlacementKind.Wall, "coat_closet"),
                E("side_table", PlacementKind.Wall, "side_table").Count(0, 1, 0.85f),
            };

            // ---------------- level 3: props  +  level 4: hazards ----------------
            // order: decor (static) -> hazards (they get first pick of the walking lanes) -> clutter along the walls
            // -> mess piles anywhere + junk spilling off them -> the bare floor hiding spots
            r[Room.Bedroom].AddRange(new[]
            {
                E("lamp", PlacementKind.Wall, "floor_lamp_square", "floor_lamp_round").Count(0, 1, 0.6f).With(e => e.preferCorner = true),
                E("plant", PlacementKind.Wall, "potted_plant", "big_plant_a").Count(0, 1, 0.5f).With(e => e.preferCorner = true),
                E("posters", PlacementKind.Wall, "poster_a", "poster_b", "poster_c", "poster_frame").Count(1, 2, 0.6f).With(e => e.preferAboveFurniture = true),
                Hazard("legos", 0, 1, 0.4f, "legos"),
                Scatter("clutter", 6, 10, "laundry_a", "laundry_b", "laundry_c", "box_open", "box_closed", "laundry_basket", "bat", "newspaper",
                    "book_floor", "books_floor", "old_tv", "speaker_small"),
                Mess("mess", 1, 2, "laundry_a", "laundry_b", "laundry_c"),
                Spill("spill", "mess", 0, 5, "pillow_floor", "pillow_blue_floor", "book_floor", "newspaper", "bear_floor", "laundry_c"),
                E("tucked", PlacementKind.Wall, "spot_tucked").Count(1, 2, 0.6f).With(e => e.preferNook = true),
                E("decoy", PlacementKind.Free, "spot_decoy").With(e => e.preferSightline = true),
            });

            r[Room.Bathroom].AddRange(new[]
            {
                Hazard("puddle", 0, 1, 1f, "puddle"), // somebody showered in a hurry
                Scatter("towels", 2, 3, "towel_blue", "towel_pink"),
                Scatter("trash", 1, 1, "trashcan"),
                Scatter("clutter", 1, 3, "laundry_b", "laundry_sack", "laundry_basket"),
                Spill("washer_spill", "washer", 0, 2, "laundry_a", "laundry_b", "towel_blue", "towel_pink"),
                E("tucked", PlacementKind.Wall, "spot_tucked").Count(0, 1, 0.6f).With(e => e.preferNook = true),
                E("decoy", PlacementKind.Free, "spot_decoy").With(e => e.preferSightline = true),
            });

            r[Room.LivingRoom].AddRange(new[]
            {
                E("doormat", PlacementKind.AtOpening, "doormat").With(e => e.openingKind = EdgeKind.FrontDoor),
                E("coat_stand", PlacementKind.Wall, "coat_stand").Count(0, 1, 0.7f),
                Rel("lamp", "sofa", RelationKind.BesideFlush, 0.15f, 0.15f, "floor_lamp_round").Count(0, 1, 0.7f),
                E("plants", PlacementKind.Wall, "potted_plant", "big_plant_a", "big_plant_b").Count(1, 2, 0.6f).With(e => e.preferCorner = true),
                E("posters", PlacementKind.Wall, "poster_a", "poster_b", "poster_frame").Count(1, 2, 0.6f).With(e => e.preferAboveFurniture = true),
                Hazard("legos", 0, 1, 0.4f, "legos"),
                Scatter("clutter", 6, 9, "box_closed", "box_open", "laundry_a", "moving_box", "old_tv", "speaker_small", "pumpkin", "vase", "newspaper",
                    "books_floor"),
                Mess("mess", 1, 2, "box_open", "laundry_a", "moving_box"),
                Spill("spill", "mess", 0, 4, "newspaper", "books_floor", "book_floor", "burger", "pillow_floor"),
                Spill("cushions", "sofa", 0, 2, "pillow_floor", "pillow_blue_floor"), // fell off the sofa
                E("tucked", PlacementKind.Wall, "spot_tucked").With(e => e.preferNook = true),
                E("decoy", PlacementKind.Free, "spot_decoy").With(e => e.preferSightline = true),
            });

            r[Room.Kitchen].AddRange(new[]
            {
                Hazard("puddle", 0, 1, 0.4f, "puddle"), // spilled drink
                Scatter("trash", 1, 1, "trashcan"),
                Scatter("clutter", 1, 3, "laundry_c", "pumpkin", "cheese"),
                Spill("food", "table", 0, 3, "burger", "cheese", "pan", "pumpkin"), // breakfast went badly
                E("plant", PlacementKind.Wall, "potted_plant").Count(0, 1, 0.4f).With(e => e.preferCorner = true),
                E("tucked", PlacementKind.Wall, "spot_tucked").Count(0, 1, 0.5f).With(e => e.preferNook = true),
                E("decoy", PlacementKind.Free, "spot_decoy").Count(0, 1, 0.5f).With(e => e.preferSightline = true),
            });

            r[Room.Hallway].AddRange(new[]
            {
                E("hooks", PlacementKind.Wall, "coat_hooks"),
                E("wall_lamp", PlacementKind.Wall, "wall_lamp").Count(0, 1, 0.7f),
                E("plant", PlacementKind.Wall, "potted_plant", "big_plant_b").Count(0, 1, 0.6f).With(e => e.preferCorner = true),
                E("poster", PlacementKind.Wall, "poster_c", "poster_a").Count(0, 1, 0.5f),
                Hazard("legos", 0, 1, 1f, "legos"), // the hallway always has legos
                Scatter("clutter", 3, 6, "box_closed", "box_open", "laundry_c", "moving_box", "book_floor", "books_floor", "newspaper"),
                Mess("mess", 0, 1, "laundry_b", "box_open"),
                Spill("spill", "mess", 0, 3, "newspaper", "book_floor", "laundry_c"),
                E("tucked", PlacementKind.Wall, "spot_tucked").With(e => e.preferNook = true),
                E("decoy", PlacementKind.Free, "spot_decoy").Count(0, 1, 0.5f).With(e => e.preferSightline = true),
            });

            return r;
        }

        /// A mess pile anywhere on the floor - middle of the room included (unlike tidy-ish edge clutter).
        private static RecipeEntry Mess(string key, int min, int max, params string[] defs) =>
            E(key, PlacementKind.Free, defs).Count(min, max, 0.7f);

        /// Junk strewn around an anchor (a mess pile, the sofa, the dining table): 0.1-0.9m beyond its edge.
        /// Always optional (min 0): the anchor itself may be absent this run.
        private static RecipeEntry Spill(string key, string anchor, int min, int max, params string[] defs) =>
            Rel(key, anchor, RelationKind.ScatterAround, 0.1f, 0.9f, defs).Count(min, max, 0.7f);

        /// Level 4 hazard: in the walking lanes, never in a doorway. Always attempted at "chance", never fatal.
        private static RecipeEntry Hazard(string key, int min, int max, float chance, params string[] defs) =>
            E(key, PlacementKind.Free, defs).Count(min, max, chance).With(e => e.preferLanes = true);

        /// Loose clutter: min..max pieces (each extra one at 70%), one of the options per piece, piled along walls/furniture.
        private static RecipeEntry Scatter(string key, int min, int max, params string[] defs) =>
            E(key, PlacementKind.Free, defs).Count(min, max, 0.7f).With(e => e.preferEdges = true);

        private static RecipeEntry With(this RecipeEntry e, System.Action<RecipeEntry> f)
        {
            f(e);
            return e;
        }

        // ------------------------------------------------------------------ build

        [MenuItem("Tidy Chaos/Generation/Build Furniture Catalog")]
        public static void BuildCatalog()
        {
            HouseBuilder.EnsureFolder(PrefabDir);
            HouseBuilder.EnsureFolder(DefDir);
            HouseBuilder.EnsureFolder(RecipeDir);

            int n = 0;
            foreach (Entry e in Catalog())
            {
                if (Bake(e)) n++;
            }

            AssetDatabase.SaveAssets();
            BuildRecipes();
            AssetDatabase.SaveAssets();
            Debug.Log($"[FurnitureCatalog] Baked {n} furniture prefabs + defs, wrote {Recipes().Count} room recipes.");
        }

        public static FurnitureDef LoadDef(string id) => AssetDatabase.LoadAssetAtPath<FurnitureDef>($"{DefDir}/FD_{id}.asset");

        public static List<RoomRecipe> LoadRecipes()
        {
            var list = new List<RoomRecipe>();
            foreach (Room room in new[] { Room.Bedroom, Room.Bathroom, Room.LivingRoom, Room.Kitchen, Room.Hallway })
            {
                var rr = AssetDatabase.LoadAssetAtPath<RoomRecipe>($"{RecipeDir}/Recipe_{room}.asset");
                if (rr != null) list.Add(rr);
            }

            return list;
        }

        private static void BuildRecipes()
        {
            foreach (var kv in Recipes())
            {
                string path = $"{RecipeDir}/Recipe_{kv.Key}.asset";
                var rr = AssetDatabase.LoadAssetAtPath<RoomRecipe>(path);
                if (rr == null)
                {
                    rr = ScriptableObject.CreateInstance<RoomRecipe>();
                    AssetDatabase.CreateAsset(rr, path);
                }

                rr.room = kv.Key;
                rr.entries = kv.Value;
                EditorUtility.SetDirty(rr);
            }
        }

        private static bool Bake(Entry e)
        {
            // def first, so the prefab can reference it
            string defPath = $"{DefDir}/FD_{e.id}.asset";
            var def = AssetDatabase.LoadAssetAtPath<FurnitureDef>(defPath);
            if (def == null)
            {
                def = ScriptableObject.CreateInstance<FurnitureDef>();
                AssetDatabase.CreateAsset(def, defPath);
            }

            GameObject prefab;
            Bounds b;
            var spots = new List<HidingSpot>();
            if (!string.IsNullOrEmpty(e.clutter))
            {
                // loose physics clutter: reuse the clutter prefab as-is (the generator adds PlacedFurniture at runtime)
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ClutterDir + e.clutter + ".prefab");
                if (prefab == null)
                {
                    Debug.LogWarning($"[FurnitureCatalog] clutter prefab '{e.clutter}' not found for '{e.id}'.");
                    return false;
                }

                var bc = prefab.GetComponent<BoxCollider>();
                Vector3 size = bc != null ? Vector3.Scale(bc.size, prefab.transform.localScale) : Vector3.one * 0.3f;
                b = new Bounds(new Vector3(0f, size.y * 0.5f, 0f), size);
            }
            else
            {
                GameObject wrap = !string.IsNullOrEmpty(e.hazard) ? HazardRoot(e) :
                    e.marker.HasValue ? MarkerRoot(e) :
                    !string.IsNullOrEmpty(e.synty) ? SyntyRoot(e) :
                    HouseBuilder.Place(e.model, null, Vector3.zero, 0f, snap: false, onFloor: false,
                        colliders: e.colliders, openables: true, label: e.label, modelYaw: e.modelYaw);
                if (wrap == null) return false;
                wrap.name = e.id;
                PaletteBuilder.ApplyTo(wrap);

                b = e.marker.HasValue ? new Bounds(new Vector3(0f, 0.025f, 0f), new Vector3(0.35f, 0.05f, 0.35f)) : HouseBuilder.RendererBounds(wrap);
                if (e.marker.HasValue)
                {
                    spots.Add(wrap.GetComponentInChildren<HidingSpot>());
                }

                foreach (SpotSpec s in e.spots)
                {
                    HidingSpot hs = BakeSpot(wrap, s, b);
                    if (hs != null) spots.Add(hs);
                }

                var pf = wrap.AddComponent<PlacedFurniture>();
                pf.def = def;
                pf.spots = spots.ToArray();

                string prefabPath = $"{PrefabDir}/{e.id}.prefab";
                prefab = PrefabUtility.SaveAsPrefabAsset(wrap, prefabPath);
                Object.DestroyImmediate(wrap);
                if (prefab == null) return false;
            }

            def.id = e.id;
            def.displayName = e.label;
            def.prefab = prefab;
            def.layer = e.layer;
            def.rooms = e.rooms;
            def.size = b.size;
            def.baseHeight = e.baseHeight;
            def.stackHeight = e.stackHeight;
            def.frontClearance = e.clearance;
            def.isRug = e.rug;
            def.maxPerHouse = e.maxPerHouse;
            def.hasHidingSpots = spots.Count > 0;
            def.sideGap = e.gap;
            def.sideAccess = e.sideAccess;
            def.isSurface = e.surface;
            def.privacy = e.privacy;
            def.hideFromDoors = e.hidden;
            def.doorWallMinDistance = e.doorWall;
            def.preferCentered = e.centered;
            def.keepOffLanes = e.offLanes;
            def.fitted = e.fitted;
            def.isLoose = !string.IsNullOrEmpty(e.clutter) || e.marker.HasValue;
            def.coverSpotChance = e.cover;
            def.tipChance = e.tip;
            def.yawJitter = e.jitter;
            def.isHazard = !string.IsNullOrEmpty(e.hazard);
            if (def.isHazard) def.isRug = true; // floor layer
            def.bundle = BakeBundle(e, b);
            EditorUtility.SetDirty(def);
            return true;
        }

        /// An invisible floor marker carrying one hiding spot (tucked-away / obvious-decoy floor spots).
        /// Level 4 hazard: a trigger volume with SlipperyHazard + its visuals (puddle disc / scattered bricks).
        private static GameObject HazardRoot(Entry e)
        {
            var root = new GameObject(e.id);
            var bc = root.AddComponent<BoxCollider>();
            bc.isTrigger = true;
            if (e.hazard == "puddle")
            {
                bc.size = new Vector3(1.2f, 0.6f, 0.8f);
                bc.center = new Vector3(0f, 0.3f, 0f);
                var disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                disc.name = "PuddleVisual";
                Object.DestroyImmediate(disc.GetComponent<Collider>());
                disc.transform.SetParent(root.transform, false);
                disc.transform.localPosition = new Vector3(0f, 0.004f, 0f);
                disc.transform.localScale = new Vector3(1.3f, 0.003f, 0.9f);
                disc.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/M_Puddle.mat");
            }
            else
            {
                bc.size = new Vector3(1.2f, 0.6f, 1.0f);
                bc.center = new Vector3(0f, 0.3f, 0f);
                var rng = new System.Random(7);
                for (int i = 0; i < 16; i++)
                {
                    var brick = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    brick.name = "Lego";
                    Object.DestroyImmediate(brick.GetComponent<Collider>());
                    brick.transform.SetParent(root.transform, false);
                    brick.transform.localPosition = new Vector3(((float)rng.NextDouble() * 2f - 1f) * 0.55f, 0.02f, ((float)rng.NextDouble() * 2f - 1f) * 0.45f);
                    brick.transform.localRotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    brick.transform.localScale = new Vector3(0.064f, 0.04f, 0.032f);
                    brick.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>($"Assets/_Project/Art/Materials/M_Lego_{i % 4}.mat");
                }
            }

            root.AddComponent<SlipperyHazard>();
            return root;
        }

        private static GameObject MarkerRoot(Entry e)
        {
            var root = new GameObject(e.id);
            var go = new GameObject("HS_" + e.marker.Value);
            go.transform.SetParent(root.transform, false);
            go.transform.localPosition = new Vector3(0f, 0.01f, 0f);
            go.AddComponent<HidingSpot>().concealmentType = e.marker.Value;
            return root;
        }

        /// Synty decor (posters, big potted plants): unpacked, scaled, bottom-centred, front = +Z.
        private static GameObject SyntyRoot(Entry e)
        {
            var src = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/SyntyStudios/" + e.synty + ".prefab");
            if (src == null)
            {
                Debug.LogWarning($"[FurnitureCatalog] Synty prefab missing: {e.synty}");
                return null;
            }

            var root = new GameObject(e.id);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(src);
            PrefabUtility.UnpackPrefabInstance(inst, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (!e.colliders)
            {
                foreach (Collider c in inst.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            }

            inst.transform.SetParent(root.transform, false);
            inst.transform.localScale *= e.syntyScale;
            inst.transform.localRotation = Quaternion.Euler(0f, e.modelYaw, 0f);
            Bounds b = HouseBuilder.RendererBounds(root);
            inst.transform.localPosition = new Vector3(-b.center.x, -b.min.y, -b.center.z);
            return root;
        }

        private static HidingSpot BakeSpot(GameObject wrap, SpotSpec s, Bounds body)
        {
            Transform parent = wrap.transform;
            Vector3 local;
            switch (s.kind)
            {
                case SpotKind.InsidePart:
                {
                    Transform part = HouseBuilder.FindPart(wrap.transform, s.part);
                    if (part == null)
                    {
                        Debug.LogWarning($"[FurnitureCatalog] '{wrap.name}' has no part '{s.part}'.");
                        return null;
                    }

                    Bounds pb = part.GetComponent<Renderer>().bounds;
                    if (part.name.ToLowerInvariant().Contains("drawer"))
                    {
                        parent = part; // rides with the drawer
                        local = new Vector3(pb.center.x, pb.min.y + 0.04f, pb.center.z);
                    }
                    else
                    {
                        local = new Vector3(body.center.x, pb.min.y + 0.04f, body.center.z);
                    }

                    break;
                }
                case SpotKind.Surface:
                    local = new Vector3(s.x, SurfaceHeight(wrap, s.x, s.z, body) + 0.01f, s.z);
                    break;
                default:
                    local = new Vector3(s.x, 0.01f, s.z);
                    break;
            }

            var go = new GameObject($"HS_{s.type}");
            go.transform.SetParent(parent, true);
            go.transform.position = local; // wrap sits at the origin, so local == world here
            var hs = go.AddComponent<HidingSpot>();
            hs.concealmentType = s.type;
            return hs;
        }

        private const string ClutterDir = "Assets/_Project/Prefabs/Furniture/Clutter/";

        /// Resolve prefab names and bake each on-top prop's height onto the piece's actual surface.
        private static List<BundleItem> BakeBundle(Entry e, Bounds body)
        {
            var list = new List<BundleItem>();
            if (e.bundle.Length == 0) return list;

            GameObject probe = null;
            foreach (BundleSpec s in e.bundle)
            {
                var options = new List<GameObject>();
                foreach (string n in s.prefabs)
                {
                    var p = AssetDatabase.LoadAssetAtPath<GameObject>(ClutterDir + n + ".prefab");
                    if (p != null) options.Add(p);
                    else Debug.LogWarning($"[FurnitureCatalog] '{e.id}' bundle: clutter prefab '{n}' not found.");
                }

                if (options.Count == 0) continue;
                float y = 0f;
                if (!s.floor)
                {
                    // the baked prefab has the colliders; measure on an instance far from the scene
                    if (probe == null) probe = (GameObject)PrefabUtility.InstantiatePrefab(LoadDef(e.id).prefab);
                    y = SurfaceHeight(probe, s.x, s.z, body);
                }

                list.Add(new BundleItem
                {
                    options = options.ToArray(),
                    localPosition = new Vector3(s.x, y, s.z),
                    localYaw = s.yaw,
                    yawJitter = s.jitter,
                    chance = s.chance,
                    onFloor = s.floor,
                    scatter = s.scatter,
                    tipChance = s.tip
                });
            }

            if (probe != null) Object.DestroyImmediate(probe);
            return list;
        }

        /// Top surface under (x, z), raycasting the piece's own colliders far away from the scene.
        private static float SurfaceHeight(GameObject wrap, float x, float z, Bounds body)
        {
            Vector3 far = new Vector3(1000f, 0f, 1000f);
            wrap.transform.position = far;
            Physics.SyncTransforms();
            float y = body.max.y;
            float best = float.MaxValue;
            foreach (RaycastHit h in Physics.RaycastAll(far + new Vector3(x, body.max.y + 0.5f, z), Vector3.down, body.max.y + 1f))
            {
                if (!h.collider.transform.IsChildOf(wrap.transform)) continue;
                if (h.distance < best)
                {
                    best = h.distance;
                    y = h.point.y - far.y;
                }
            }

            wrap.transform.position = Vector3.zero;
            Physics.SyncTransforms();
            return y;
        }
    }
}
