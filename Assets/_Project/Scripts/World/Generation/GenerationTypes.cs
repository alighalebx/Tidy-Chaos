using System;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    /// Which rooms a piece of furniture is allowed in (rule H1: no bed in the bathroom).
    [Flags]
    public enum RoomMask
    {
        None = 0,
        Bedroom = 1 << 0,
        Bathroom = 1 << 1,
        LivingRoom = 1 << 2,
        Hallway = 1 << 3,
        Kitchen = 1 << 4,
        All = Bedroom | Bathroom | LivingRoom | Hallway | Kitchen
    }

    /// Generation level. Each level is planned after the previous one and builds on its placements.
    public enum GenLayer
    {
        Base = 0,      // beds, sofas, bathroom fixtures, kitchen run, tables
        Hiding = 1,    // standalone storage: wardrobes, closets, bookcases, vanity...
        Props = 2,     // pillows, small clutter, decor
        Obstacles = 3  // puddles, legos, chaos hazards
    }

    /// What a room boundary is made of, per 2.1m wall tile.
    public enum EdgeKind
    {
        Solid,
        Window,
        Doorway,
        FrontDoor,
        Open // no wall at all (living room <-> kitchen)
    }

    public enum PlacementKind
    {
        Wall,      // back flush against a solid/window wall, facing into the room
        Free,      // anywhere on the floor
        Relative,  // positioned from another entry (see RelationKind)
        CounterRun, // composite kitchen run of modules
        AtOpening   // centred just inside a doorway / the front door (doormat)
    }

    public enum RelationKind
    {
        BesideFlush,  // next to the anchor, backs aligned (nightstand by the bed, speakers by the TV)
        InFront,      // in front of the anchor at a distance (coffee table, bath mat, rug at the bed foot)
        FacingAcross, // across from the anchor, facing it (sofa facing the TV)
        SideOf,       // at the anchor's left/right end, facing it (armchair at the coffee table)
        OnTop,        // standing on the anchor's top surface (TV on its cabinet)
        Above,        // wall-mounted above the anchor, backs aligned (mirror above the sink)
        Under,        // centered under the anchor (rug under the coffee table)
        ScatterAround // strewn around the anchor within min..max distance (junk spilling off a laundry pile)
    }

    public static class RoomMaskUtil
    {
        public static RoomMask ToMask(Room room) => (RoomMask)(1 << (int)room);
        public static bool Allows(RoomMask mask, Room room) => (mask & ToMask(room)) != 0;
    }
}
