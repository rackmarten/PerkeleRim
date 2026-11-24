#nullable disable  // esp-gen compiles this file too, with nullable checks on
using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // The Satsuma bay next to the garage near Whiterun: where its pieces stand (tools/esp-gen builds them
    // from this, linking this file) and where each of the Satsuma's loose parts goes (MSC's GarageMover
    // puts them there). One layout for both, in the bay's own frame: Skyrim units (70 a meter), origin on
    // the bay's floor in its middle (the bay marker Skyrim sends), +X out of the open front, +Y to the left
    // seen from inside looking out (away from the garage), Z up from the floor. Unity-free, so the tests
    // cover the slot map.
    public static class SatsumaBay
    {
        // ---- The pieces (vanilla Skyrim statics, by EditorID) ----

        // The bay's floor: wooden quads (512 square, flat at their origin) laid at Z 0, the rest of the
        // bay stands on it, so the shelves' boards and the stand are at known heights whatever the
        // ground under them.
        public const string FloorPiece = "WRIntCastleFloorWoodMid01Quad";
        public const float FloorHalf = 256f;
        public static readonly float[] FloorX = { -192f, 192f };  // two quads: x -448..448 under a 720 long shed

        // CommonShelf01: 134 wide (along its local X), 54 deep, boards at these heights above its origin
        // (measured on the island in game: the top at 153 and three below).
        public const string ShelfPiece = "CommonShelf01";
        public const float ShelfWidth = 134f;
        public const float ShelfDepth = 54f;
        public static readonly float[] BoardHeights = { 18f, 63f, 108f, 153f };

        // The shelves: three against the back wall (inner face at x -341, as the garage's) and four
        // along the far side wall (+Y, inner face at y 223), all facing into the bay. Along is also
        // the rotation esp-gen places the shelf with (at 0 its width runs along +X and its front
        // faces +Y, as the garage's shelves).
        public struct Shelf
        {
            public float X, Y;      // its middle, bay frame
            public float Along;     // radians: the way its width runs (its local +X), clockwise from the bay's +X
            public Shelf(float x, float y, float along) { X = x; Y = y; Along = along; }
        }

        const float Quarter = (float)(Math.PI / 2), Half = (float)Math.PI;
        public static readonly Shelf[] Shelves =
        {
            new Shelf(-311f, -150f, Quarter), new Shelf(-311f, 0f, Quarter), new Shelf(-311f, 150f, Quarter),  // back wall
            new Shelf(-150f, 193f, Half), new Shelf(-10f, 193f, Half), new Shelf(130f, 193f, Half), new Shelf(270f, 193f, Half),  // far wall
        };

        // The stand: two long crates (CrateSmallLong01: 50 x 142 x 48, its origin in its middle) across
        // the bay under the body's sills, its top StandTop above the floor; the body's middle on Body.
        public const string StandPiece = "CrateSmallLong01";
        public const float StandTop = 48f;
        public static readonly float[] StandX = { -85f, 85f };  // along the body, from its middle
        public const float BodyX = 30f, BodyY = 10f;  // the body's middle; nose toward +X (out of the front)
        public const float BodyHalfLength = 130f, BodyHalfWidth = 56f;

        // ---- Slots ----

        public enum Kind { Small, Medium, Floor }

        public struct Slot
        {
            public int Index;      // in Slots
            public Kind Kind;
            public float X, Y, Z;  // bay frame: where the part's middle goes, Z the surface it lies on
            public float Along;    // radians, clockwise from the bay's +X: the way the part's long side lies
            public int Shelf;      // -1 on the floor
            public int Board;      // 0 bottom .. 3 top, -1 on the floor
        }

        // What each board holds, shelf by shelf, bottom board first: a small board has 4 slots (about
        // 0.45 m each), a medium one 2 (0.95 m). Parts by MSC's object names (a name listed n times
        // takes n slots: MSC has n of them). "" is a free slot, for a part not listed here.
        static readonly string[][][] Boards =
        {
            // back wall: the engine
            new[]
            {
                new[] { "gearbox(Clone)", "headers(Clone)" },
                new[] { "piston1(Clone)", "piston2(Clone)", "piston3(Clone)", "piston4(Clone)" },
                new[] { "crankshaft(Clone)", "crankshaft pulley(Clone)", "camshaft(Clone)", "camshaft gear(Clone)" },
                new[] { "main bearing1(Clone)", "main bearing2(Clone)", "main bearing3(Clone)", "oil filter(Clone)" },
            },
            new[]
            {
                new[] { "oilpan(Clone)", "electrics(Clone)" },
                new[] { "cylinder head(Clone)", "head gasket(Clone)", "rocker shaft(Clone)", "rocker cover(Clone)" },
                new[] { "timing chain(Clone)", "timing cover(Clone)", "drive gear(Clone)", "engine plate(Clone)" },
                new[] { "water pump(Clone)", "water pump pulley(Clone)", "inspection cover(Clone)", "radiator hose2(Clone)" },
            },
            new[]
            {
                new[] { "flywheel(Clone)", "clutch disc(Clone)", "clutch cover plate(Clone)", "clutch pressure plate(Clone)" },
                new[] { "alternator(Clone)", "starter(Clone)", "distributor(Clone)", "carburator(Clone)" },
                new[] { "airfilter(Clone)", "fuel pump(Clone)", "clock gauge(Clone)", "fur dices(Clone)" },
                new[] { "", "", "", "" },
            },
            // far wall: running gear, then the car's small parts
            new[]
            {
                new[] { "strut fl(Clone)", "strut fr(Clone)" },
                new[] { "shock absorber(Clone)", "shock absorber(Clone)" },
                new[] { "disc brake(Clone)", "disc brake(Clone)", "drum brake(Clone)", "drum brake(Clone)" },
                new[] { "wishbone fl(Clone)", "wishbone fr(Clone)", "coil spring(Clone)", "coil spring(Clone)" },
            },
            new[]
            {
                new[] { "trail arm rl(Clone)", "trail arm rr(Clone)" },
                new[] { "halfshaft(Clone)", "halfshaft(Clone)" },
                new[] { "spindle fl(Clone)", "spindle fr(Clone)", "steering rod fl(Clone)", "steering rod fr(Clone)" },
                new[] { "steering rack(Clone)", "stock steering wheel(Clone)", "gear stick(Clone)", "fuel tank pipe(Clone)" },
            },
            new[]
            {
                new[] { "battery(Clone)", "radiator(Clone)", "fuel strainer(Clone)", "radiator hose1(Clone)" },
                new[] { "brake lining(Clone)", "clutch lining(Clone)" },
                new[] { "gear linkage(Clone)", "dashboard meters(Clone)" },
                new[] { "brake master cylinder(Clone)", "clutch master cylinder(Clone)", "radiator hose3(Clone)", "radio(Clone)" },
            },
            new[]
            {
                new[] { "mudflap fl(Clone)", "mudflap fr(Clone)", "mudflap rl(Clone)", "mudflap rr(Clone)" },
                new[] { "hubcap(Clone)", "hubcap(Clone)", "hubcap(Clone)", "hubcap(Clone)" },
                new[] { "headlight left(Clone)", "headlight right(Clone)", "rear light left(Clone)", "rear light right(Clone)" },
                new[] { "exhaust muffler(Clone)", "" },
            },
        };

        // The floor: body panels, seats and the heavy pieces, in rows along the near side (toward the
        // garage) and in front of the back shelves' corner, and the open front corner. Each spot
        // (x, y, along) in the bay frame.
        static readonly float[][] FloorSpots =
        {
            // near side, panels lying flat, long side along X (clear of the back shelves, x -284 on)
            new[] { -220f, -195f, 0f }, new[] { -150f, -195f, 0f }, new[] { -80f, -195f, 0f }, new[] { -10f, -195f, 0f },
            new[] { 60f, -195f, 0f }, new[] { 130f, -195f, 0f }, new[] { 200f, -195f, 0f }, new[] { 270f, -195f, 0f },
            new[] { 340f, -195f, 0f },
            // a second row nearer the stand
            new[] { -230f, -115f, 0f }, new[] { -150f, -115f, 0f }, new[] { 120f, -115f, 0f }, new[] { 200f, -115f, 0f },
            // behind the body, in front of the back shelves
            new[] { -200f, -30f, Quarter }, new[] { -200f, 60f, Quarter },
            // the open front corner, beside the nose
            new[] { 260f, 40f, Quarter }, new[] { 260f, 110f, Quarter }, new[] { 320f, 40f, Quarter }, new[] { 320f, 110f, Quarter },
            new[] { 290f, -60f, Quarter }, new[] { 280f, -130f, 0f },
        };

        static readonly string[] FloorParts =
        {
            "door left(Clone)", "door right(Clone)", "fender left(Clone)", "fender right(Clone)", "hood(Clone)",
            "bootlid(Clone)", "bumper front(Clone)", "bumper rear(Clone)", "exhaust pipe(Clone)",
            "block(Clone)", "seat driver(Clone)", "fuel tank(Clone)", "sub frame(Clone)",
            "seat passenger(Clone)", "seat rear(Clone)",
            "dashboard(Clone)", "grille(Clone)", "back panel(Clone)", "subwoofer panel(Clone)",
            "steering column(Clone)", "handbrake(Clone)",
        };

        // Free spots past every slot, for parts with nowhere else to go: a grid on the floor's apron in
        // front of the shed (the floor reaches x 448), then on top of each other there.
        public const float PileX = 375f, PileY = 150f, PileStep = 40f;
        public const int PileColumns = 2, PileRows = 6;

        public static readonly Slot[] Slots;
        static readonly Dictionary<string, List<int>> byName = new Dictionary<string, List<int>>();

        static SatsumaBay()
        {
            var slots = new List<Slot>();
            for (int s = 0; s < Boards.Length; s++)
            {
                var shelf = Shelves[s];
                float ca = (float)Math.Cos(shelf.Along), sa = (float)Math.Sin(shelf.Along);
                for (int b = 0; b < Boards[s].Length; b++)
                {
                    var names = Boards[s][b];
                    var kind = names.Length == 4 ? Kind.Small : Kind.Medium;
                    float step = ShelfWidth / names.Length;
                    for (int k = 0; k < names.Length; k++)
                    {
                        float along = -ShelfWidth / 2 + step * (k + 0.5f);
                        // Clockwise headings: a run along heading a moves (cos a, -sin a) in (x, y).
                        Add(slots, names[k], new Slot
                        {
                            Kind = kind, Shelf = s, Board = b, Along = shelf.Along,
                            X = shelf.X + along * ca, Y = shelf.Y - along * sa, Z = BoardHeights[b],
                        });
                    }
                }
            }
            for (int i = 0; i < FloorSpots.Length; i++)
            {
                var f = FloorSpots[i];
                Add(slots, i < FloorParts.Length ? FloorParts[i] : "", new Slot { Kind = Kind.Floor, Shelf = -1, Board = -1, X = f[0], Y = f[1], Z = 0f, Along = f[2] });
            }
            Slots = slots.ToArray();
        }

        static void Add(List<Slot> slots, string name, Slot slot)
        {
            slot.Index = slots.Count;
            slots.Add(slot);
            if (name.Length == 0) return;
            List<int> list;
            if (!byName.TryGetValue(name, out list)) byName[name] = list = new List<int>();
            list.Add(slot.Index);
        }

        // Whether MSC's object name is one of the Satsuma's parts with slots of its own.
        public static bool Listed(string name) => byName.ContainsKey(name);

        // The slots listed for that part name, in order (empty if none).
        public static IList<int> SlotsFor(string name)
        {
            List<int> list;
            return byName.TryGetValue(name, out list) ? (IList<int>)list : new int[0];
        }

        // The kind of free slot a part of that size takes (meters: its longest extent, and how tall it
        // lies): boards are 0.64 m apart.
        public static Kind KindFor(float longest, float height)
        {
            if (longest <= 0.42f && height <= 0.4f) return Kind.Small;
            if (longest <= 0.9f && height <= 0.4f) return Kind.Medium;
            return Kind.Floor;
        }

        public struct Part
        {
            public string Name;
            public float Longest;  // meters, its longest extent
            public float Height;   // meters, how tall it lies
        }

        public struct Placement
        {
            public int Slot;       // in Slots, -1 when it went on the pile
            public int Pile;       // its place on the pile when Slot is -1
        }

        // Where each part goes: a listed part takes the next of its own slots still free, a part that
        // isn't listed (or a second of one MSC should have only one of) the first free slot of its size
        // or bigger (small, medium, then the floor), and the rest the pile. taken: slots already held
        // (a part already lying there), left as they are. Same parts, same answer.
        public static Placement[] Assign(IList<Part> parts, ICollection<int> taken = null)
        {
            var used = new bool[Slots.Length];
            if (taken != null) foreach (var t in taken) if (t >= 0 && t < used.Length) used[t] = true;
            var result = new Placement[parts.Count];
            var later = new List<int>();
            for (int i = 0; i < parts.Count; i++)
            {
                result[i] = new Placement { Slot = -1, Pile = -1 };
                int slot = -1;
                foreach (var s in SlotsFor(parts[i].Name))
                    if (!used[s]) { slot = s; break; }
                if (slot < 0) { later.Add(i); continue; }
                used[slot] = true;
                result[i].Slot = slot;
            }
            int pile = 0;
            foreach (var i in later)
            {
                int slot = FreeSlot(used, KindFor(parts[i].Longest, parts[i].Height));
                if (slot >= 0)
                {
                    used[slot] = true;
                    result[i].Slot = slot;
                }
                else result[i].Pile = pile++;
            }
            return result;
        }

        // The first slot not listed for any part, free, of that kind or a bigger one.
        static int FreeSlot(bool[] used, Kind kind)
        {
            for (var k = kind; k <= Kind.Floor; k++)
                for (int s = 0; s < Slots.Length; s++)
                    if (!used[s] && Slots[s].Kind == k && !IsListed(s)) return s;
            // Then any free slot of that kind or bigger, listed or not: a part that isn't there leaves its slot.
            for (var k = kind; k <= Kind.Floor; k++)
                for (int s = 0; s < Slots.Length; s++)
                    if (!used[s] && Slots[s].Kind == k) return s;
            return -1;
        }

        static bool IsListed(int slot)
        {
            foreach (var list in byName.Values) if (list.Contains(slot)) return true;
            return false;
        }

        // The pile's spot n (bay frame): a grid on the floor, then stacked, 0.3 m a layer.
        public static void PileSpot(int n, out float x, out float y, out float z)
        {
            int per = PileColumns * PileRows;
            int layer = n / per, at = n % per;
            x = PileX + (at % PileColumns) * PileStep;
            y = PileY - (at / PileColumns) * PileStep;
            z = layer * 21f;
        }

        // Bay frame to Skyrim world: the bay marker's position and heading (radians clockwise from north,
        // the way its +X points).
        public static void ToWorld(float bx, float by, float bz, float mx, float my, float mz, float heading, out float x, out float y, out float z)
        {
            // +X points along heading: (sin h, cos h) in Skyrim's (x east, y north); +Y is 90 degrees
            // counterclockwise from it: (-cos h, sin h).
            float s = (float)Math.Sin(heading), c = (float)Math.Cos(heading);
            x = mx + bx * s - by * c;
            y = my + bx * c + by * s;
            z = mz + bz;
        }
    }
}
