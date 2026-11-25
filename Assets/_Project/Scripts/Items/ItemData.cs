using UnityEngine;

namespace WheresMyLeftSock.Items
{
    public enum WearSlot { LeftFoot, RightFoot, Legs, Torso, Accessory }

    public enum ItemQuality { Matching, Trap }

    public enum ItemShape { Sock, WetSock, Pants, ClownPants, Shirt, InsideOutShirt, Keys, ToyKeys }

    /// <summary>One wearable / objective item (spec section 5 schema). Visual is built from primitives by ItemVisuals.</summary>
    [CreateAssetMenu(menuName = "Tidy Chaos/Item Data", fileName = "Item_")]
    public class ItemData : ScriptableObject
    {
        public string itemId = "item";
        public string displayName = "Item";
        public WearSlot slot;
        public ItemQuality quality = ItemQuality.Matching;
        public int dignityPoints = 20;
        [Tooltip("Funny end-screen tag for trap items, e.g. 'Squishy Foot'.")]
        public string funnyTag;
        [Tooltip("Empty = can spawn in any room.")]
        public Room[] allowedRooms = new Room[0];

        [Header("Look")]
        public ItemShape shape;
        public Color color = Color.white;
        public Color accentColor = Color.white;
        [Tooltip("Procedural SFX name played on pickup (see Sfx).")]
        public string pickupSfx = "pop";

        public bool IsTrap => quality == ItemQuality.Trap;

        public bool AllowsRoom(Room room)
        {
            if (allowedRooms == null || allowedRooms.Length == 0)
            {
                return true;
            }

            foreach (Room r in allowedRooms)
            {
                if (r == room)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
