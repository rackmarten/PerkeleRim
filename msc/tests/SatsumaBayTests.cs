using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class SatsumaBayTests
    {
        // The Satsuma's loose parts at MSC's home in a fresh game (dumped from the starter save: spike
        // "satsuma"), plus the body panels and seats a fresh game also has loose.
        static readonly string[] FreshGame =
        {
            "back panel(Clone)", "battery(Clone)", "brake lining(Clone)", "brake master cylinder(Clone)", "clock gauge(Clone)",
            "clutch lining(Clone)", "clutch master cylinder(Clone)", "coil spring(Clone)", "coil spring(Clone)",
            "dashboard meters(Clone)", "disc brake(Clone)", "disc brake(Clone)", "drum brake(Clone)", "drum brake(Clone)",
            "exhaust muffler(Clone)", "exhaust pipe(Clone)", "fuel strainer(Clone)", "fuel tank pipe(Clone)", "fuel tank(Clone)",
            "fur dices(Clone)", "gear linkage(Clone)", "gear stick(Clone)", "halfshaft(Clone)", "halfshaft(Clone)",
            "handbrake(Clone)", "hubcap(Clone)", "hubcap(Clone)", "hubcap(Clone)", "hubcap(Clone)",
            "mudflap fl(Clone)", "mudflap fr(Clone)", "mudflap rl(Clone)", "mudflap rr(Clone)", "radiator hose1(Clone)",
            "radiator hose3(Clone)", "radiator(Clone)", "radio(Clone)", "rear light left(Clone)", "rear light right(Clone)",
            "seat rear(Clone)", "shock absorber(Clone)", "shock absorber(Clone)", "spindle fl(Clone)", "spindle fr(Clone)",
            "steering column(Clone)", "steering rack(Clone)", "steering rod fl(Clone)", "steering rod fr(Clone)",
            "stock steering wheel(Clone)", "strut fl(Clone)", "strut fr(Clone)", "sub frame(Clone)", "subwoofer panel(Clone)",
            "trail arm rl(Clone)", "trail arm rr(Clone)", "wishbone fl(Clone)", "wishbone fr(Clone)",
            "airfilter(Clone)", "alternator(Clone)", "block(Clone)", "camshaft gear(Clone)", "camshaft(Clone)", "carburator(Clone)",
            "clutch cover plate(Clone)", "clutch disc(Clone)", "clutch pressure plate(Clone)", "crankshaft pulley(Clone)",
            "crankshaft(Clone)", "cylinder head(Clone)", "distributor(Clone)", "drive gear(Clone)", "electrics(Clone)",
            "engine plate(Clone)", "flywheel(Clone)", "fuel pump(Clone)", "gearbox(Clone)", "head gasket(Clone)", "headers(Clone)",
            "inspection cover(Clone)", "main bearing1(Clone)", "main bearing2(Clone)", "main bearing3(Clone)", "oil filter(Clone)",
            "oilpan(Clone)", "piston1(Clone)", "piston2(Clone)", "piston3(Clone)", "piston4(Clone)", "radiator hose2(Clone)",
            "rocker cover(Clone)", "rocker shaft(Clone)", "starter(Clone)", "timing chain(Clone)", "timing cover(Clone)",
            "water pump pulley(Clone)", "water pump(Clone)",
            "door left(Clone)", "door right(Clone)", "fender left(Clone)", "fender right(Clone)", "hood(Clone)", "bootlid(Clone)",
            "bumper front(Clone)", "bumper rear(Clone)", "grille(Clone)", "headlight left(Clone)", "headlight right(Clone)",
            "seat driver(Clone)", "seat passenger(Clone)", "dashboard(Clone)",
        };

        static List<SatsumaBay.Part> Parts(IEnumerable<string> names, float longest = 0.3f, float height = 0.2f) =>
            names.Select(n => new SatsumaBay.Part { Name = n, Longest = longest, Height = height }).ToList();

        [Test]
        public void EveryPartOfAFreshGameHasASlotOfItsOwn()
        {
            var parts = Parts(FreshGame);
            var placed = SatsumaBay.Assign(parts);
            for (int i = 0; i < parts.Count; i++)
            {
                Assert.That(placed[i].Slot, Is.GreaterThanOrEqualTo(0), parts[i].Name);
                Assert.That(SatsumaBay.SlotsFor(parts[i].Name), Does.Contain(placed[i].Slot), parts[i].Name);
            }
            Assert.That(placed.Select(p => p.Slot).Distinct().Count(), Is.EqualTo(parts.Count));
        }

        [Test]
        public void TheSameAnswerForTheSameParts()
        {
            var a = SatsumaBay.Assign(Parts(FreshGame));
            var b = SatsumaBay.Assign(Parts(FreshGame));
            Assert.That(a.Select(p => p.Slot), Is.EqualTo(b.Select(p => p.Slot)));
        }

        [Test]
        public void PartsOfTheSameNameTakeTheirSlotsInTurn()
        {
            var placed = SatsumaBay.Assign(Parts(Enumerable.Repeat("hubcap(Clone)", 4)));
            Assert.That(placed.Select(p => p.Slot), Is.EqualTo(SatsumaBay.SlotsFor("hubcap(Clone)")));
        }

        [Test]
        public void ALaterPartTakesAFreeSlotOfItsSize()
        {
            // A fifth hubcap (bought later) and a part the map doesn't know, small and big.
            var parts = Parts(Enumerable.Repeat("hubcap(Clone)", 5));
            parts.Add(new SatsumaBay.Part { Name = "racing harness(Clone)", Longest = 0.3f, Height = 0.1f });
            parts.Add(new SatsumaBay.Part { Name = "fiberglass hood(Clone)", Longest = 1.4f, Height = 0.2f });
            var placed = SatsumaBay.Assign(parts);
            foreach (var i in new[] { 4, 5 })
            {
                Assert.That(placed[i].Slot, Is.GreaterThanOrEqualTo(0));
                Assert.That(SatsumaBay.Slots[placed[i].Slot].Kind, Is.EqualTo(SatsumaBay.Kind.Small));
            }
            Assert.That(placed[4].Slot, Is.Not.EqualTo(placed[5].Slot));
            Assert.That(SatsumaBay.Slots[placed[6].Slot].Kind, Is.EqualTo(SatsumaBay.Kind.Floor));
            // A free slot is one no listed part has, while there is one (the floor's are all listed: the
            // big one takes the slot of a part that isn't there).
            var listed = new HashSet<int>(FreshGame.SelectMany(n => SatsumaBay.SlotsFor(n)));
            Assert.That(listed, Does.Not.Contain(placed[4].Slot));
            Assert.That(listed, Does.Not.Contain(placed[5].Slot));
        }

        [Test]
        public void SlotsTakenAlreadyAreLeftAlone()
        {
            int first = SatsumaBay.SlotsFor("piston1(Clone)")[0];
            var placed = SatsumaBay.Assign(Parts(new[] { "piston1(Clone)" }), new[] { first });
            Assert.That(placed[0].Slot, Is.Not.EqualTo(first));
            Assert.That(placed[0].Slot, Is.GreaterThanOrEqualTo(0));
        }

        [Test]
        public void WithEveryFloorSlotTakenBigPartsGoOnThePile()
        {
            var floors = Enumerable.Range(0, SatsumaBay.Slots.Length).Where(s => SatsumaBay.Slots[s].Kind == SatsumaBay.Kind.Floor).ToList();
            var placed = SatsumaBay.Assign(Parts(Enumerable.Repeat("a crate of something(Clone)", 3), 2f, 1f), floors);
            Assert.That(placed.Select(p => p.Slot), Is.All.EqualTo(-1));
            Assert.That(placed.Select(p => p.Pile), Is.EqualTo(new[] { 0, 1, 2 }));
        }

        [Test]
        public void SizesPickTheKind()
        {
            Assert.That(SatsumaBay.KindFor(0.2f, 0.1f), Is.EqualTo(SatsumaBay.Kind.Small));
            Assert.That(SatsumaBay.KindFor(0.8f, 0.2f), Is.EqualTo(SatsumaBay.Kind.Medium));
            Assert.That(SatsumaBay.KindFor(0.3f, 0.6f), Is.EqualTo(SatsumaBay.Kind.Floor));  // too tall between boards
            Assert.That(SatsumaBay.KindFor(1.2f, 0.1f), Is.EqualTo(SatsumaBay.Kind.Floor));
        }

        [Test]
        public void SlotsOnOneSurfaceDontOverlap()
        {
            var slots = SatsumaBay.Slots;
            for (int a = 0; a < slots.Length; a++)
                for (int b = a + 1; b < slots.Length; b++)
                {
                    if (slots[a].Z != slots[b].Z) continue;
                    float d = (float)Math.Sqrt(Math.Pow(slots[a].X - slots[b].X, 2) + Math.Pow(slots[a].Y - slots[b].Y, 2));
                    float need = slots[a].Kind == SatsumaBay.Kind.Small && slots[b].Kind == SatsumaBay.Kind.Small ? 30f : 45f;
                    Assert.That(d, Is.GreaterThanOrEqualTo(need), $"slots {a} and {b}");
                }
        }

        [Test]
        public void FloorSlotsAndThePileStayOnTheFloorAndClearOfTheStandAndShelves()
        {
            const float margin = 30f;  // about half a part
            var spots = SatsumaBay.Slots.Where(s => s.Kind == SatsumaBay.Kind.Floor).Select(s => (s.X, s.Y)).ToList();
            for (int n = 0; n < SatsumaBay.PileColumns * SatsumaBay.PileRows; n++)
            {
                SatsumaBay.PileSpot(n, out var x, out var y, out _);
                spots.Add((x, y));
            }
            foreach (var (x, y) in spots)
            {
                Assert.That(Math.Abs(x), Is.LessThanOrEqualTo(SatsumaBay.FloorX.Max() + SatsumaBay.FloorHalf - margin));
                Assert.That(Math.Abs(y), Is.LessThanOrEqualTo(SatsumaBay.FloorHalf - margin));
                bool onBody = Math.Abs(x - SatsumaBay.BodyX) < SatsumaBay.BodyHalfLength + margin && Math.Abs(y - SatsumaBay.BodyY) < SatsumaBay.BodyHalfWidth + margin;
                Assert.That(onBody, Is.False, $"({x}, {y}) under the body");
                foreach (var shelf in SatsumaBay.Shelves)
                    Assert.That(InShelf(shelf, x, y, margin), Is.False, $"({x}, {y}) in a shelf");
            }
        }

        [Test]
        public void ShelvesDontOverlapEachOtherOrTheBody()
        {
            var shelves = SatsumaBay.Shelves;
            for (int a = 0; a < shelves.Length; a++)
            {
                Assert.That(InShelf(shelves[a], SatsumaBay.BodyX, SatsumaBay.BodyY, 0f), Is.False);
                for (int b = a + 1; b < shelves.Length; b++)
                {
                    // Corners of b inside a.
                    foreach (var (x, y) in Corners(shelves[b]))
                        Assert.That(InShelf(shelves[a], x, y, -1f), Is.False, $"shelves {a} and {b}");
                }
            }
        }

        [Test]
        public void TheBayFrameTurnsLikeTheGarages()
        {
            // Facing east (heading 90 degrees): +X is east, +Y (left) is north.
            SatsumaBay.ToWorld(10f, 0f, 0f, 100f, 200f, 5f, (float)(Math.PI / 2), out var x, out var y, out var z);
            Assert.That(x, Is.EqualTo(110f).Within(1e-3f));
            Assert.That(y, Is.EqualTo(200f).Within(1e-3f));
            Assert.That(z, Is.EqualTo(5f));
            SatsumaBay.ToWorld(0f, 10f, 3f, 100f, 200f, 5f, (float)(Math.PI / 2), out x, out y, out z);
            Assert.That(x, Is.EqualTo(100f).Within(1e-3f));
            Assert.That(y, Is.EqualTo(210f).Within(1e-3f));
            Assert.That(z, Is.EqualTo(8f));
        }

        static (float, float)[] Corners(SatsumaBay.Shelf s)
        {
            float ca = (float)Math.Cos(s.Along), sa = (float)Math.Sin(s.Along);
            var list = new List<(float, float)>();
            foreach (var u in new[] { -SatsumaBay.ShelfWidth / 2, SatsumaBay.ShelfWidth / 2 })
                foreach (var v in new[] { -SatsumaBay.ShelfDepth / 2, SatsumaBay.ShelfDepth / 2 })
                    list.Add((s.X + u * ca + v * sa, s.Y - u * sa + v * ca));
            return list.ToArray();
        }

        // Whether (x, y) is within the shelf's footprint grown by margin.
        static bool InShelf(SatsumaBay.Shelf s, float x, float y, float margin)
        {
            float ca = (float)Math.Cos(s.Along), sa = (float)Math.Sin(s.Along);
            float dx = x - s.X, dy = y - s.Y;
            float u = dx * ca - dy * sa, v = dx * sa + dy * ca;
            return Math.Abs(u) < SatsumaBay.ShelfWidth / 2 + margin && Math.Abs(v) < SatsumaBay.ShelfDepth / 2 + margin;
        }
    }
}
