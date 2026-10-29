using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class HeldBottleTests
    {
        [Test]
        public void EveryShownDrinkHasItsBottle()
        {
            Assert.That(HeldBottle.Path(Wire.DrinkBeer), Is.EqualTo("Hand/BeerBottle"));
            Assert.That(HeldBottle.Path(Wire.DrinkBooze), Is.EqualTo("Hand/BoozeBottle"));
            Assert.That(HeldBottle.Path(Wire.DrinkSpirit), Is.EqualTo("Hand/SpiritBottle"));
            Assert.That(HeldBottle.Path(Wire.DrinkMilk), Is.EqualTo("Hand/HandMilk/Milk"));
            Assert.That(HeldBottle.Path(Wire.DrinkJuice), Is.EqualTo("Hand/HandJuice/JuiceCan"));
            Assert.That(HeldBottle.Path(Wire.DrinkNone), Is.Null);
            Assert.That(HeldBottle.Hand(Wire.DrinkBeer), Is.EqualTo("Hand/HandBottles"));
            Assert.That(HeldBottle.Hand(Wire.DrinkSpirit), Is.EqualTo("Hand/HandBottles"));
            Assert.That(HeldBottle.Hand(Wire.DrinkMilk), Is.EqualTo("Hand/HandMilk"));
            Assert.That(HeldBottle.Hand(Wire.DrinkJuice), Is.EqualTo("Hand/HandJuice"));
        }

        [Test]
        public void TheGripIsOnTheBottleNearestTheFist()
        {
            var bottom = new Vec3(0f, 0f, 0.1f);
            var top = new Vec3(0f, 0.2f, 0.1f);
            var g = HeldBottle.Grip(bottom, top, new Vec3(0.05f, 0.08f, 0.12f));  // the fist beside it, 40 % up
            Assert.That(g.x, Is.EqualTo(0f).Within(1e-6));
            Assert.That(g.y, Is.EqualTo(0.08f).Within(1e-6));
            Assert.That(g.z, Is.EqualTo(0.1f).Within(1e-6));
            Assert.That(HeldBottle.Grip(bottom, top, new Vec3(0f, 0.5f, 0f)).y, Is.EqualTo(0.2f).Within(1e-6));  // not past the top
            Assert.That(HeldBottle.Grip(bottom, top, new Vec3(0f, -1f, 0f)).y, Is.EqualTo(0f).Within(1e-6));    // or the base
        }

        [Test]
        public unsafe void TheBottleGoesToSkyrimsCameraSpace()
        {
            // A bottle 0.2 m ahead, 0.1 m below the eye and upright, label to the right.
            var m = HeldBottle.Message(0x0003133B, new Vec3(0f, -0.1f, 0.2f), new Vec3(0f, 0.1f, 0.2f), new Vec3(1f, 0f, 0f), new Vec3(0f, 0.05f, 0.2f));
            Assert.That(m.form, Is.EqualTo(0x0003133Bu));
            float ahead = (float)(0.2 * Coords.UnitsPerMeter), low = (float)(-0.1 * Coords.UnitsPerMeter);
            Assert.That(m.bottom[0], Is.EqualTo(0f).Within(1e-3));
            Assert.That(m.bottom[1], Is.EqualTo(ahead).Within(1e-3));  // Skyrim's camera: +Y forward
            Assert.That(m.bottom[2], Is.EqualTo(low).Within(1e-3));    // and +Z up
            Assert.That(m.top[2], Is.EqualTo(-low).Within(1e-3));
            Assert.That(m.side[0], Is.GreaterThan(0f));
            Assert.That(m.side[1], Is.EqualTo(0f));
            Assert.That(m.side[2], Is.EqualTo(0f));
            Assert.That(m.grip[1], Is.EqualTo(ahead).Within(1e-3));
            Assert.That(m.grip[2], Is.EqualTo((float)(0.05 * Coords.UnitsPerMeter)).Within(1e-3));
        }

        [Test]
        public void NoneDropsIt()
        {
            Assert.That(HeldBottle.None().form, Is.Zero);
        }
    }
}
