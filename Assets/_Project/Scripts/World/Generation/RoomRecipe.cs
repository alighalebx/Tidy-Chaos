using System;
using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// What a room is furnished with, in placement order (anchors first, then the pieces that relate to them).
    /// </summary>
    [CreateAssetMenu(menuName = "Tidy Chaos/Generation/Room Recipe", fileName = "Recipe_New")]
    public class RoomRecipe : ScriptableObject
    {
        public Room room;
        public List<RecipeEntry> entries = new List<RecipeEntry>();
    }

    [Serializable]
    public class RecipeEntry
    {
        [Tooltip("Unique within the recipe. Relative entries point at this.")]
        public string key;

        [Tooltip("One of these is picked (at random) per instance.")]
        public FurnitureDef[] options = Array.Empty<FurnitureDef>();

        [Min(0)] public int min = 1;
        [Min(0)] public int max = 1;

        [Tooltip("Chance each instance above 'min' is attempted.")]
        [Range(0f, 1f)] public float chance = 1f;

        public PlacementKind placement = PlacementKind.Wall;

        [Header("Relative placement")]
        public string anchorKey;
        public RelationKind relation;
        public float minDistance;
        public float maxDistance;

        [Header("Preferences (soft rules)")]
        public bool preferCorner;
        public bool preferWindow;
        public bool avoidWindow;
        public bool avoidFacingDoorway;
        public bool preferNearDoorway;
        public bool alignLongAxis;
        public bool preferCenterLine;
        [Tooltip("Clutter: gathers along walls and beside furniture, not in the middle of the floor.")]
        public bool preferEdges;
        [Tooltip("Tucked away: between furniture and a wall, beside tall storage.")]
        public bool preferNook;
        [Tooltip("In plain view from a doorway (obvious decoy spots).")]
        public bool preferSightline;
        [Tooltip("Wall decor: centred above a low piece on the same wall (poster over the sofa/bed/desk).")]
        public bool preferAboveFurniture;
        [Tooltip("Hazards: right in the walking lanes between doorways (that's where sprinting players are).")]
        public bool preferLanes;

        [Header("AtOpening")]
        public EdgeKind openingKind = EdgeKind.FrontDoor;

        [Header("Kitchen run")]
        public CounterRunSettings counter;

        public GenLayer Layer => options != null && options.Length > 0 && options[0] != null ? options[0].layer : GenLayer.Base;
    }

    /// <summary>
    /// A continuous kitchen run along one wall, optionally turning a corner (L-shape).
    /// Fridge at an end, stove never next to the fridge, hood above the stove, uppers only above base modules.
    /// </summary>
    [Serializable]
    public class CounterRunSettings
    {
        public FurnitureDef fridge;
        public FurnitureDef stove;
        public FurnitureDef sink;
        public FurnitureDef corner;
        public FurnitureDef hood;
        public FurnitureDef microwave;
        public FurnitureDef[] cabinets = Array.Empty<FurnitureDef>();
        public FurnitureDef[] uppers = Array.Empty<FurnitureDef>();

        [Min(2)] public int minModules = 4;   // base modules on the main wall, fridge included
        [Min(2)] public int maxModules = 9;
        [Range(0f, 1f)] public float lShapeChance = 0.5f;
        [Min(1)] public int legMax = 3;
        [Range(0f, 1f)] public float upperChance = 0.65f;
        [Range(0f, 1f)] public float microwaveChance = 0.75f;
    }
}
