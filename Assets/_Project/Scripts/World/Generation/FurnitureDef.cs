using System;
using System.Collections.Generic;
using UnityEngine;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// A prop that belongs to a piece and is placed with it as one unit (desk chair + laptop, chairs around
    /// the dining table, pillows on the bed). Positions are in the piece's local space (+Z = its front).
    /// </summary>
    [Serializable]
    public class BundleItem
    {
        [Tooltip("One of these is picked.")]
        public GameObject[] options = Array.Empty<GameObject>();
        public Vector3 localPosition;   // y is baked: the piece's surface for props on top, 0 for floor props
        public float localYaw;
        public float yawJitter;
        [Range(0f, 1f)] public float chance = 1f;
        [Tooltip("Stands on the floor next to the piece (chairs): dropped if that spot is blocked or outside the room.")]
        public bool onFloor;

        [Header("Mess")]
        [Tooltip("Random XZ offset (m) - things on top can end up half off the edge and fall.")]
        public float scatter;
        [Range(0f, 1f)] public float tipChance;
    }

    /// <summary>
    /// One placeable piece of furniture: prefab + measured footprint + the rules that describe where it may go.
    /// Prefab convention: origin at bottom-center, +Z is the front (the side you use / open).
    /// Created by "Tidy Chaos/Generation/Build Furniture Catalog" (rule values live in FurnitureCatalog.cs).
    /// </summary>
    [CreateAssetMenu(menuName = "Tidy Chaos/Generation/Furniture Def", fileName = "FD_New")]
    public class FurnitureDef : ScriptableObject
    {
        public string id;
        public string displayName;
        public GameObject prefab;
        public GenLayer layer = GenLayer.Base;

        [Tooltip("Rooms this piece may appear in. Hard rule: never placed anywhere else.")]
        public RoomMask rooms = RoomMask.None;

        [Tooltip("Measured: x = width, y = height, z = depth (front is +Z).")]
        public Vector3 size = Vector3.one;

        [Tooltip("Height above the floor of the piece's bottom (wall-mounted pieces).")]
        public float baseHeight;

        [Tooltip("Height that will be stacked on top later (e.g. the TV on its cabinet) - counts for window rules.")]
        public float stackHeight;

        [Tooltip("Free floor needed in front of the piece (drawers, doors, seats). Nothing else may stand there.")]
        public float frontClearance;

        [Tooltip("Rugs: floor layer, may sit under other furniture, never blocks walking.")]
        public bool isRug;

        [Tooltip("At most this many in the whole house.")]
        public int maxPerHouse = 99;

        [Tooltip("Baked: the prefab carries HidingSpots, so its front must stay reachable.")]
        public bool hasHidingSpots;

        [Tooltip("Breathing room to unrelated furniture on every side (the larger of the two pieces' gaps applies).")]
        public float sideGap = 0.12f;

        [Tooltip("May stand flush beside other fitted pieces on the same wall, like a fitted run (sink beside the washer).")]
        public bool fitted;

        [Tooltip("Beds: at least one long side keeps this much free floor (reserved once placed).")]
        public float sideAccess;

        [Tooltip("Loose props may be set down on top of it (tables, desks, counters).")]
        public bool isSurface;

        [Tooltip("A loose physics prop (clutter): pushable, so it never blocks walking - but props never stack, " +
                 "never stand in doorways or in front of drawers/doors.")]
        public bool isLoose;

        [Tooltip("Loose props only: chance an 'under clutter' hiding spot is put beneath it (laundry piles, towels, boxes).")]
        [Range(0f, 1f)] public float coverSpotChance;

        [Tooltip("Loose props only: chance it spawns knocked over (box on its side, toppled trash can).")]
        [Range(0f, 1f)] public float tipChance;

        [Tooltip("Free-standing pieces and rugs: random yaw (degrees) so the room isn't ruler-straight.")]
        public float yawJitter;

        [Tooltip("Level 4 hazard (puddle, legos): floor layer, kept out of doorways, off rugs, furniture and clutter.")]
        public bool isHazard;

        [Header("Architecture")]
        [Tooltip("+1 wants to be far from the room's main entrance (toilet, bed), -1 near it (sink, shoe bench).")]
        [Range(-1f, 1f)] public float privacy;

        [Tooltip("Never visible straight through a doorway (toilet).")]
        public bool hideFromDoors;

        [Tooltip("On a wall that has a doorway, keep at least this far from the opening (99 = never on a door wall).")]
        public float doorWallMinDistance;

        [Tooltip("Centred on its free stretch of wall, or centred under a window.")]
        public bool preferCentered;

        [Tooltip("Stay out of the walking lanes between the room's doorways (sofas, coffee tables).")]
        public bool keepOffLanes;

        [Header("Bundle")]
        [Tooltip("Props placed together with this piece, as one unit.")]
        public List<BundleItem> bundle = new List<BundleItem>();

        public float TopHeight => baseHeight + size.y + stackHeight;

        /// Occupies the walking band (0 .. LowBandTop above the floor).
        public bool OccupiesLow => !isRug && baseHeight < HouseShell.LowBandTop;

        /// Occupies the upper band (LowBandTop .. ceiling): may not cover windows or overlap wall-mounted pieces.
        public bool OccupiesHigh => !isRug && TopHeight > HouseShell.LowBandTop;
    }
}
