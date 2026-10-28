using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class JunkBodyTests
    {
        [Test]
        public void TheBoxIsTheBoundsInUnitysAxesAndMeters()
        {
            Vec3 center, size;
            // 35 units long along Skyrim's x, 14 deep along y, 28 tall along z, standing on its origin.
            JunkBody.Box(new Vec3(-17.5f, -7f, 0f), new Vec3(17.5f, 7f, 28f), out center, out size);
            Assert.That(size.x, Is.EqualTo(35f * 0.0142875f).Within(1e-5f));
            Assert.That(size.y, Is.EqualTo(28f * 0.0142875f).Within(1e-5f));  // Skyrim's z is Unity's y
            Assert.That(size.z, Is.EqualTo(14f * 0.0142875f).Within(1e-5f));
            Assert.That(center.x, Is.EqualTo(0f).Within(1e-6f));
            Assert.That(center.y, Is.EqualTo(14f * 0.0142875f).Within(1e-5f));
            Assert.That(center.z, Is.EqualTo(0f).Within(1e-6f));
        }

        [Test]
        public void AFlatOrEmptyBoxStillHasSize()
        {
            Vec3 center, size;
            JunkBody.Box(new Vec3(0f, 0f, 0f), new Vec3(0f, 0f, 0f), out center, out size);
            Assert.That(size.x, Is.EqualTo(JunkBody.SizeMin));
            Assert.That(size.y, Is.EqualTo(JunkBody.SizeMin));
            Assert.That(size.z, Is.EqualTo(JunkBody.SizeMin));
        }

        [Test]
        public void ABigItemIsCappedToFitTheHand()
        {
            Vec3 center, size;
            JunkBody.Box(new Vec3(-100f, -10f, 0f), new Vec3(100f, 10f, 10f), out center, out size);  // 2.9 m long
            Assert.That(size.x, Is.EqualTo(JunkBody.SizeMax));
            Assert.That(size.z, Is.EqualTo(20f * 0.0142875f).Within(1e-5f));
        }

        [Test]
        public void FarBelowTheFeetIsLostNotLanded()
        {
            Assert.That(JunkBody.Lost(1.5f), Is.False);  // down a slope
            Assert.That(JunkBody.Lost(-30f), Is.False);  // up on a cliff
            Assert.That(JunkBody.Lost(JunkBody.FallMax + 0.1f), Is.True);
        }

        [Test]
        public void ItLandsOnceItLiesStillOutOfTheHand()
        {
            var landing = new JunkLanding();
            Assert.That(landing.Update(true, 0f, 0f), Is.False);   // in the hand, still: not landed
            Assert.That(landing.Update(true, 0f, 10f), Is.False);
            Assert.That(landing.Update(false, 20f, 10.1f), Is.False);  // thrown
            Assert.That(landing.Update(false, 0.05f, 11f), Is.False);  // stopped
            Assert.That(landing.Update(false, 2f, 11.2f), Is.False);   // a bounce: starts over
            Assert.That(landing.Update(false, 0.05f, 11.3f), Is.False);
            Assert.That(landing.Update(false, 0.05f, 11.7f), Is.False);
            Assert.That(landing.Update(false, 0.05f, 11.8f), Is.True);
        }

        [Test]
        public void ItLandsWhereverItIsAfterTooLongOutOfTheHand()
        {
            var landing = new JunkLanding();
            Assert.That(landing.Update(false, 5f, 0f), Is.False);
            Assert.That(landing.Update(false, 5f, JunkBody.LooseMax - 0.1f), Is.False);  // rolling down a hill
            Assert.That(landing.Update(false, 5f, JunkBody.LooseMax), Is.True);
        }

        [Test]
        public void PickedUpAgainStartsOver()
        {
            var landing = new JunkLanding();
            landing.Update(false, 0f, 0f);
            landing.Update(true, 0f, 0.3f);  // caught
            Assert.That(landing.Update(false, 0f, 0.5f), Is.False);
            Assert.That(landing.Update(false, 0f, 0.9f), Is.False);
            Assert.That(landing.Update(false, 0f, 1.0f), Is.True);
        }
    }
}
