using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class RightingTests
    {
        [Test]
        public void HelpsFullyNearTheWantedLean()
        {
            var r = new Righting();
            Assert.That(r.Update(0f, 4), Is.EqualTo(1f));
            Assert.That(r.Update(Righting.Full, 4), Is.EqualTo(1f));
            Assert.That(r.Fallen, Is.False);
        }

        [Test]
        public void EasesOffBetweenFullAndFall()
        {
            float mid = Righting.Gain((Righting.Full + Righting.Fall) / 2f);
            Assert.That(mid, Is.InRange(0.4f, 0.6f));
            Assert.That(Righting.Gain(Righting.Full + 1f), Is.GreaterThan(Righting.Gain(Righting.Full + 5f)));
            Assert.That(Righting.Gain(Righting.Fall), Is.EqualTo(0f));
            Assert.That(Righting.Gain(80f), Is.EqualTo(0f));
        }

        [Test]
        public void FallingIsLeftAloneEvenWhenItSwingsBackThroughTheEaseOffBand()
        {
            var r = new Righting();
            Assert.That(r.Update(Righting.Fall + 1f, 4), Is.EqualTo(0f));
            Assert.That(r.Fallen, Is.True);
            // A slide that rocks between 15 and 25 degrees gets no help.
            Assert.That(r.Update(25f, 4), Is.EqualTo(0f));
            Assert.That(r.Update(Righting.Full, 4), Is.EqualTo(0f));
            Assert.That(r.Fallen, Is.True);
        }

        [Test]
        public void StandsUpAgainNearlyUprightOnItsWheels()
        {
            var r = new Righting();
            r.Update(60f, 4);
            Assert.That(r.Update(Righting.GetUp - 1f, 1), Is.EqualTo(0f));  // one wheel on the ground: no
            Assert.That(r.Fallen, Is.True);
            Assert.That(r.Update(Righting.GetUp - 1f, Righting.MinWheels), Is.EqualTo(1f));
            Assert.That(r.Fallen, Is.False);
        }

        [Test]
        public void NeedsWheelsOnTheGround()
        {
            var r = new Righting();
            Assert.That(r.Update(0f, 0), Is.EqualTo(0f));
            Assert.That(r.Update(0f, Righting.MinWheels - 1), Is.EqualTo(0f));
            Assert.That(r.Fallen, Is.False);
            Assert.That(r.Update(0f, Righting.MinWheels), Is.EqualTo(1f));
        }

        [Test]
        public void NaNGivesNoHelp()
        {
            var r = new Righting();
            Assert.That(r.Update(float.NaN, 4), Is.EqualTo(0f));
        }

        [Test]
        public void ResetStandsItUp()
        {
            var r = new Righting();
            r.Update(90f, 4);
            r.Reset();
            Assert.That(r.Fallen, Is.False);
        }
    }

    public class LeanTests
    {
        [Test]
        public void NoLeanAtACrawl()
        {
            Assert.That(Righting.WantedLean(1f, 2f), Is.EqualTo(0f));
            Assert.That(Righting.WantedLean(Righting.LeanFrom, 3f), Is.EqualTo(0f));
        }

        [Test]
        public void LeanFadesInWithSpeedAndIsCapped()
        {
            float slow = Righting.WantedLean(3f, 1.5f);
            float mid = Righting.WantedLean(4.5f, 1.5f);
            float fast = Righting.WantedLean(Righting.LeanFull, 1.5f);
            Assert.That(slow, Is.GreaterThan(0f).And.LessThan(mid));
            Assert.That(mid, Is.LessThan(fast));
            Assert.That(fast, Is.EqualTo(Righting.MaxLean).Within(1e-4f));   // 6 m/s at 1.5 rad/s asks for more than the cap
            Assert.That(Righting.WantedLean(20f, 3f), Is.EqualTo(Righting.MaxLean).Within(1e-4f));
        }

        [Test]
        public void LeansIntoTheTurnBothWays()
        {
            Assert.That(Righting.WantedLean(8f, 0.3f), Is.GreaterThan(0f));
            Assert.That(Righting.WantedLean(8f, -0.3f), Is.EqualTo(-Righting.WantedLean(8f, 0.3f)).Within(1e-5f));
            Assert.That(Righting.WantedLean(8f, 0f), Is.EqualTo(0f));
        }

        [Test]
        public void SlidingMeansSidewaysSpeed()
        {
            Assert.That(Bumps.Sliding(0.3f), Is.False);
            Assert.That(Bumps.Sliding(Bumps.SlideSpeed), Is.True);
            Assert.That(Bumps.Sliding(-2f), Is.True);
        }
    }

    public class LeavingTests
    {
        [Test]
        public void TippedMopedLetsTheRiderOffAtAnySpeed()
        {
            Assert.That(Leaving.Tipped(0f), Is.True);    // on its side
            Assert.That(Leaving.Tipped(-1f), Is.True);   // on its roof
            Assert.That(Leaving.Tipped(Leaving.TippedUpY - 0.01f), Is.True);
            Assert.That(Leaving.Limit(0f), Is.EqualTo(Leaving.Anything));
        }

        [Test]
        public void StandingMopedLetsTheRiderOffAtAWalkingPace()
        {
            Assert.That(Leaving.Tipped(1f), Is.False);
            Assert.That(Leaving.Tipped(0.8f), Is.False);  // a steep road
            Assert.That(Leaving.Limit(1f), Is.EqualTo(Leaving.WalkingSpeed));
            Assert.That(Leaving.WalkingSpeed, Is.GreaterThan(0.15f));  // more than MSC's own limit
        }
    }
}
