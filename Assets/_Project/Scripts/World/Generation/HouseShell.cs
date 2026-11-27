using System.Collections.Generic;
using UnityEngine;
using WheresMyLeftSock.Items;

namespace WheresMyLeftSock.World.Generation
{
    public enum Side
    {
        South, // inward = +z
        North, // inward = -z
        West,  // inward = +x
        East   // inward = -x
    }

    /// One 2.1m tile of a room's boundary.
    public sealed class WallSegment
    {
        public Side side;
        public EdgeKind kind;
        public float from, to;               // coordinate range along the side (x for S/N, z for W/E)
        public float line;                   // the wall line (z for S/N, x for W/E)
        public float clearFrom, clearTo;     // the open part of a doorway / window, same axis as from/to

        public bool IsWall => kind == EdgeKind.Solid || kind == EdgeKind.Window;
        public bool IsOpening => kind == EdgeKind.Doorway || kind == EdgeKind.FrontDoor;
    }

    public sealed class RoomArea
    {
        public Room room;
        public Rect rect; // world XZ (x = x, y = z)
        public readonly List<WallSegment> segments = new List<WallSegment>();

        public IEnumerable<WallSegment> On(Side side)
        {
            foreach (WallSegment s in segments)
            {
                if (s.side == side) yield return s;
            }
        }

        public bool SideIsOpen(Side side)
        {
            foreach (WallSegment s in On(side))
            {
                if (s.kind != EdgeKind.Open) return false;
            }

            return true;
        }
    }

    /// <summary>
    /// The fixed house shell: room rectangles, walls, doorways and windows. Single source of truth for
    /// HouseBuilder (which builds the walls) and the furniture generator (which plans around them).
    /// Grid: 2.1m tiles, x = west->east (6 tiles), z = south (front door) -> north (8 tiles).
    /// </summary>
    public static class HouseShell
    {
        public const float ModelScale = 0.21f;     // Kenney FBX units -> meters
        public const float Tile = 2.1f;
        public const float WallHeight = 12.9f * ModelScale; // 2.709m
        public const float FloorTop = 0.5f * ModelScale;     // 0.105m
        public const float WallInset = 0.07f;      // gap between a wall line and a furniture back
        public const float LowBandTop = 1.05f;     // above-floor height splitting "low" and "high" occupancy
        public const float WindowSill = 0.72f;     // above floor top (measured from wallWindow.fbx)

        // Clear openings inside a 2.1m tile (measured from the Kenney meshes; symmetric around the tile center).
        public const float DoorwayClearFrom = 0.15f, DoorwayClearTo = 1.95f;     // wallDoorwayWide
        public const float FrontDoorClearFrom = 0.60f, FrontDoorClearTo = 1.50f; // wallDoorway
        public const float WindowClearFrom = 0.67f, WindowClearTo = 1.42f;       // wallWindow

        // '#' wall, 'w' window, 'd' wide open doorway, 'D' front door, '.'/missing = nothing
        // HLines[z line] -> chars per x tile. VLines[x line] -> chars per z tile.
        public static readonly string[] HLines =
        {
            "wwD#w#",   // z=0    front (living/kitchen)
            "", "", "",
            "#d##d#",   // z=8.4  living | hallway
            "##d#d#",   // z=10.5 hallway | bedroom, bathroom
            "", "",
            "#ww#w#"    // z=16.8 back
        };

        public static readonly string[] VLines =
        {
            "#ww###w#", // x=0    west
            "", "", "",
            ".....#d#", // x=8.4  bedroom | bathroom
            "",
            "##w###w#"  // x=12.6 east
        };

        // room -> tile rect (x, z, width, depth)
        private static readonly (Room room, int x, int z, int w, int d)[] RoomTiles =
        {
            (Room.LivingRoom, 0, 0, 4, 4),
            (Room.Kitchen, 4, 0, 2, 4),
            (Room.Hallway, 0, 4, 6, 1),
            (Room.Bedroom, 0, 5, 4, 3),
            (Room.Bathroom, 4, 5, 2, 3)
        };

        private static List<RoomArea> _rooms;

        public static IReadOnlyList<RoomArea> Rooms => _rooms ??= BuildRooms();

        public static RoomArea Get(Room room)
        {
            foreach (RoomArea r in Rooms)
            {
                if (r.room == room) return r;
            }

            return null;
        }

        public static Room RoomAt(Vector3 p)
        {
            if (p.z < 4 * Tile) return p.x >= 4 * Tile ? Room.Kitchen : Room.LivingRoom;
            if (p.z < 5 * Tile) return Room.Hallway;
            return p.x < 4 * Tile ? Room.Bedroom : Room.Bathroom;
        }

        public static EdgeKind KindFromChar(char c)
        {
            switch (c)
            {
                case '#': return EdgeKind.Solid;
                case 'w': return EdgeKind.Window;
                case 'd': return EdgeKind.Doorway;
                case 'D': return EdgeKind.FrontDoor;
                default: return EdgeKind.Open;
            }
        }

        private static EdgeKind Kind(string[] lines, int line, int tile)
        {
            if (line < 0 || line >= lines.Length) return EdgeKind.Open;
            string s = lines[line];
            return tile >= 0 && tile < s.Length ? KindFromChar(s[tile]) : EdgeKind.Open;
        }

        private static List<RoomArea> BuildRooms()
        {
            var list = new List<RoomArea>();
            foreach (var rt in RoomTiles)
            {
                var area = new RoomArea
                {
                    room = rt.room,
                    rect = new Rect(rt.x * Tile, rt.z * Tile, rt.w * Tile, rt.d * Tile)
                };

                for (int i = rt.x; i < rt.x + rt.w; i++)
                {
                    area.segments.Add(Segment(Side.South, Kind(HLines, rt.z, i), i * Tile, rt.z * Tile));
                    area.segments.Add(Segment(Side.North, Kind(HLines, rt.z + rt.d, i), i * Tile, (rt.z + rt.d) * Tile));
                }

                for (int j = rt.z; j < rt.z + rt.d; j++)
                {
                    area.segments.Add(Segment(Side.West, Kind(VLines, rt.x, j), j * Tile, rt.x * Tile));
                    area.segments.Add(Segment(Side.East, Kind(VLines, rt.x + rt.w, j), j * Tile, (rt.x + rt.w) * Tile));
                }

                list.Add(area);
            }

            return list;
        }

        private static WallSegment Segment(Side side, EdgeKind kind, float from, float line)
        {
            float cf = 0f, ct = 0f;
            switch (kind)
            {
                case EdgeKind.Doorway: cf = DoorwayClearFrom; ct = DoorwayClearTo; break;
                case EdgeKind.FrontDoor: cf = FrontDoorClearFrom; ct = FrontDoorClearTo; break;
                case EdgeKind.Window: cf = WindowClearFrom; ct = WindowClearTo; break;
                case EdgeKind.Open: cf = 0f; ct = Tile; break;
            }

            return new WallSegment
            {
                side = side,
                kind = kind,
                from = from,
                to = from + Tile,
                line = line,
                clearFrom = from + cf,
                clearTo = from + ct
            };
        }

        /// Inward unit normal of a room side (XZ).
        public static Vector2 Inward(Side side)
        {
            switch (side)
            {
                case Side.South: return new Vector2(0f, 1f);
                case Side.North: return new Vector2(0f, -1f);
                case Side.West: return new Vector2(1f, 0f);
                default: return new Vector2(-1f, 0f);
            }
        }

        /// Yaw (degrees) of a piece whose front faces into the room from this side.
        public static float InwardYaw(Side side)
        {
            switch (side)
            {
                case Side.South: return 0f;
                case Side.North: return 180f;
                case Side.West: return 90f;
                default: return 270f;
            }
        }

        public static bool IsHorizontal(Side side) => side == Side.South || side == Side.North;

        public static Rect HouseRect => new Rect(0f, 0f, 6 * Tile, 8 * Tile);

        /// Center of an opening's clear span, on the wall line (XZ).
        public static Vector2 OpeningCenter(WallSegment s)
        {
            float mid = (s.clearFrom + s.clearTo) * 0.5f;
            return IsHorizontal(s.side) ? new Vector2(mid, s.line) : new Vector2(s.line, mid);
        }

        /// The room on the other side of a segment, or null when that is outside the house.
        public static RoomArea Beyond(WallSegment s)
        {
            Vector2 c = OpeningCenter(s) - Inward(s.side) * 0.3f;
            if (!HouseRect.Contains(c)) return null;
            return Get(RoomAt(new Vector3(c.x, 0f, c.y)));
        }

        private static Dictionary<Room, int> _depth;

        /// Rooms from the front door: living 0, hallway/kitchen 1, bedroom/bathroom 2.
        public static int Depth(Room room)
        {
            if (_depth == null)
            {
                _depth = new Dictionary<Room, int>();
                var queue = new Queue<RoomArea>();
                foreach (RoomArea a in Rooms)
                {
                    foreach (WallSegment s in a.segments)
                    {
                        if (s.kind == EdgeKind.FrontDoor && !_depth.ContainsKey(a.room))
                        {
                            _depth[a.room] = 0;
                            queue.Enqueue(a);
                        }
                    }
                }

                while (queue.Count > 0)
                {
                    RoomArea a = queue.Dequeue();
                    foreach (WallSegment s in a.segments)
                    {
                        if (!s.IsOpening && s.kind != EdgeKind.Open) continue;
                        RoomArea b = Beyond(s);
                        if (b == null || b == a || _depth.ContainsKey(b.room)) continue;
                        _depth[b.room] = _depth[a.room] + 1;
                        queue.Enqueue(b);
                    }
                }
            }

            return _depth.TryGetValue(room, out int d) ? d : 99;
        }

        /// How "public" an opening is: -1 for the front door, else the depth of the room it leads to.
        public static int OpeningDepth(WallSegment s)
        {
            if (s.kind == EdgeKind.FrontDoor) return -1;
            RoomArea b = Beyond(s);
            return b == null ? -1 : Depth(b.room);
        }
    }
}
