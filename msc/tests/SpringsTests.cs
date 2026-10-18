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
