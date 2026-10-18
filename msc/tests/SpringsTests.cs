using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class SpringsTests
    {
        const float Weight = 1000f;

        [Test]
        public void SoftInTheFirstPartOfTheTravel()
        {
            Assert.That(Springs.Push(0f, 0f, Weight, 0.6f, 1f), Is.EqualTo(0f));
            Assert.That(Springs.Push(0.6f, 0f, Weight, 0.6f, 1f), Is.EqualTo(0f));
            Assert.That(Springs.Push(0.3f, 5f, Weight, 0.6f, 1f), Is.EqualTo(0f));   // squeezing fast, still soft
        }

        [Test]
        public void HarderAndHarderTowardsBottomingOut()
        {
            float a = Springs.Push(0.7f, 0f, Weight, 0.6f, 1f);
            float b = Springs.Push(0.8f, 0f, Weight, 0.6f, 1f);
            float c = Springs.Push(1f, 0f, Weight, 0.6f, 1f);
            Assert.That(a, Is.GreaterThan(0f));
            Assert.That(b, Is.GreaterThan(a));
            Assert.That(c, Is.GreaterThan(b));
            Assert.That(c, Is.EqualTo(Weight).Within(1e-3f));   // one weight at full travel with Strength 1
            Assert.That(b - a, Is.LessThan(c - b));             // grows with the square
        }

        [Test]
        public void StrengthScalesItAndDampsTheSqueeze()
        {
            Assert.That(Springs.Push(1f, 0f, Weight, 0.6f, 2f), Is.EqualTo(2f * Weight).Within(1e-3f));
            Assert.That(Springs.Push(0.8f, 1f, Weight, 0.6f, 1f), Is.GreaterThan(Springs.Push(0.8f, 0f, Weight, 0.6f, 1f)));
            Assert.That(Springs.Push(0.8f, -3f, Weight, 0.6f, 1f), Is.EqualTo(Springs.Push(0.8f, 0f, Weight, 0.6f, 1f)));  // rebounding adds none
        }

        [Test]
        public void StopsGrowingPastTheTravel()
        {
            Assert.That(Springs.Push(5f, 0f, Weight, 0.6f, 1f), Is.EqualTo(Springs.Push(10f, 0f, Weight, 0.6f, 1f)));
        }

        [Test]
        public void NoStrengthNoPush()
        {
            Assert.That(Springs.Push(1f, 3f, Weight, 0.6f, 0f), Is.EqualTo(0f));
            Assert.That(Springs.Push(float.NaN, 0f, Weight, 0.6f, 1f), Is.EqualTo(0f));
        }

        [Test]
        public void ParkedVehiclesDontPush()
        {
            Assert.That(Springs.Applies(false), Is.False);
            Assert.That(Springs.Applies(true), Is.True);
        }

        [Test]
        public void ALandingOnTheSeatDoesntFlingIt()
        {
            // A wheel squeezed at 10 m/s all the way down: capped at a few weights, not tens.
            float push = Springs.Push(1f, 10f, Weight, 0.75f, 1.5f);
            Assert.That(push, Is.EqualTo(Springs.MaxWeights * Weight).Within(1e-3f));
        }

        [Test]
        public void TheMopedAtRestSitsBelowTheProgressivePart()
        {
            // 140 kg on four wheels, its front spring 3150 N/m (7000 x 0.6 x 0.75) over 0.16 m of travel
            // (0.08 x 2): the heaviest wheel sags 0.68 of it, which the old From of 0.6 pushed on.
            float load = 140f * 9.81f / 4f;
            float share = Springs.RestShare(load, 3150f, 0.16f);
            Assert.That(share, Is.InRange(0.6f, 0.75f));
            Assert.That(Springs.Push(share, 0f, 140f * 9.81f, Springs.MopedFrom, 1.5f), Is.EqualTo(0f));
            Assert.That(Springs.Push(share, 0f, 140f * 9.81f, 0.6f, 1.5f), Is.GreaterThan(0f));
        }

        [Test]
        public void AirAssistSkipsABikeLyingDown()
        {
            Assert.That(Righting.AirHelps(1f, 8f), Is.True);        // flying level
            Assert.That(Righting.AirHelps(0.7f, 4f), Is.True);      // pitched in a jump
            Assert.That(Righting.AirHelps(0f, 8f), Is.False);       // on its side
            Assert.That(Righting.AirHelps(-1f, 8f), Is.False);      // on its roof
            Assert.That(Righting.AirHelps(1f, 0.5f), Is.False);     // hardly moving
        }
    }
}
