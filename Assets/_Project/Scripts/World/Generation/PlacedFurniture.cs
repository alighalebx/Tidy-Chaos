using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// <summary>
    /// Marker on every generated furniture prefab: which def it is, where it ended up, and its baked hiding spots.
    /// Later generation levels (props, obstacles) use this to find surfaces and anchors.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlacedFurniture : MonoBehaviour
    {
        public FurnitureDef def;
        public Room room;
        public string entryKey;
        public HidingSpot[] spots = System.Array.Empty<HidingSpot>();

        public void Bind(FurnitureDef d, Room r, string key, bool spotsEnabled, ref int spotCounter)
        {
            def = d;
            room = r;
            entryKey = key;
            foreach (HidingSpot s in spots)
            {
                if (s == null) continue;
                s.room = r;
                s.spotID = $"{r}_G{++spotCounter:00}";
                s.isOccupied = false;
                s.gameObject.SetActive(spotsEnabled);
            }
        }
    }
}
