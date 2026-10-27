using System.Collections.Generic;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class ThrowChargeTests
    {
        const int R = ThrowCharge.Mouse1;
        const int W = 119;

        [Test]
        public void DistanceFollowsTheHoldLinearlyFromHalfToDouble()
        {
            Assert.That(ThrowCharge.Factor(0f), Is.EqualTo(0.5f).Within(1e-6f));
            Assert.That(ThrowCharge.Factor(0.5f), Is.EqualTo(1.25f).Within(1e-6f));
            Assert.That(ThrowCharge.Factor(1f), Is.EqualTo(2f).Within(1e-6f));
            Assert.That(ThrowCharge.Factor(5f), Is.EqualTo(2f).Within(1e-6f));  // the max stays
            Assert.That(ThrowCharge.Factor(-1f), Is.EqualTo(0.5f).Within(1e-6f));
        }

        [Test]
        public void LaunchSpeedGoesWithTheSquareRootOfTheDistance()
        {
            // Range goes with speed squared: speed x sqrt(f) flies f times as far.
            foreach (var f in new[] { 0.5f, 1f, 1.25f, 2f })
                Assert.That(ThrowCharge.SpeedScale(f) * ThrowCharge.SpeedScale(f), Is.EqualTo(f).Within(1e-5f));
            Assert.That(Throwing.LaunchScale(false, 1f), Is.EqualTo(1f).Within(1e-6f));
            Assert.That(Throwing.LaunchScale(true, 1f), Is.EqualTo(Throwing.BottleBoost).Within(1e-6f));
            Assert.That(Throwing.LaunchScale(true, 2f), Is.EqualTo(Throwing.BottleBoost * 1.41421356f).Within(1e-4f));
            Assert.That(Throwing.LaunchScale(false, 0.5f), Is.EqualTo(0.70710678f).Within(1e-4f));
        }

        [Test]
        public void RangeOnALongArcScalesByTheFactorExactly()
        {
            // Flat throw from 1.6 m up, no drag: the fall time stays, so the reach only goes with the speed.
            float Reach(float speed) => speed * (float)System.Math.Sqrt(2 * 1.6f / 9.81f);
            float today = Reach(10f * Throwing.LaunchScale(true, 1f));
            Assert.That(Reach(10f * Throwing.LaunchScale(true, 2f)) / today, Is.EqualTo(1.41421356f).Within(1e-4f));
            // The range on a long arc (speed squared) is where the factor is exact.
            float Range(float speed) => speed * speed / 9.81f;
            Assert.That(Range(10f * Throwing.LaunchScale(true, 2f)) / Range(10f * Throwing.LaunchScale(true, 1f)), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(Range(10f * Throwing.LaunchScale(true, ThrowCharge.Factor(0f))) / Range(10f * Throwing.LaunchScale(true, 1f)), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void TheClickIsHeldBackUntilLetGoAndThenGoesToMscOnce()
        {
            var charge = new ThrowCharge();
            var keys = new List<int> { W, R };
            charge.Apply(keys, true, 10f);
            Assert.That(charge.Charging, Is.True);
            Assert.That(keys, Is.EquivalentTo(new[] { W }));

            keys = new List<int> { R };
            charge.Apply(keys, true, 10.4f);
            Assert.That(keys, Is.Empty);
            Assert.That(charge.Held(10.4f), Is.EqualTo(0.4f).Within(1e-5f));

            keys = new List<int> { W };
            charge.Apply(keys, true, 10.8f);
            Assert.That(charge.Charging, Is.False);
            Assert.That(keys, Is.EquivalentTo(new[] { W, R }));

            keys = new List<int> { W };
            charge.Apply(keys, true, 10.82f);
            Assert.That(keys, Is.EquivalentTo(new[] { W }));  // the click is one frame
            Assert.That(charge.Take(10.9f, Throwing.ThrowWindow), Is.EqualTo(ThrowCharge.Factor(0.8f)).Within(1e-5f));
        }

        [Test]
        public void ATapThrowsHalfAsFarAndAReleaseIsTakenOnce()
        {
            var charge = new ThrowCharge();
            charge.Apply(new List<int> { R }, true, 3f);
            charge.Apply(new List<int>(), true, 3f);
            Assert.That(charge.Take(3.2f, Throwing.ThrowWindow), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(charge.Take(3.2f, Throwing.ThrowWindow), Is.EqualTo(1f));  // used up
        }

        [Test]
        public void AnItemLeavingTheHandLongAfterTheReleaseIsMscsOwnThrow()
        {
            var charge = new ThrowCharge();
            charge.Apply(new List<int> { R }, true, 0f);
            charge.Apply(new List<int>(), true, 2f);
            Assert.That(charge.Take(2f + Throwing.ThrowWindow + 0.1f, Throwing.ThrowWindow), Is.EqualTo(1f));
        }

        [Test]
        public void LongerThanASecondStaysAtTheMax()
        {
            var charge = new ThrowCharge();
            charge.Apply(new List<int> { R }, true, 0f);
            charge.Apply(new List<int> { R }, true, 4f);
            charge.Apply(new List<int>(), true, 4.5f);
            Assert.That(charge.Take(4.5f, Throwing.ThrowWindow), Is.EqualTo(2f).Within(1e-6f));
        }

        [Test]
        public void EmptyHandsLeaveTheClickToTheBlock()
        {
            var charge = new ThrowCharge();
            var keys = new List<int> { R };
            charge.Apply(keys, false, 1f);
            Assert.That(charge.Charging, Is.False);
            Assert.That(keys, Is.EquivalentTo(new[] { R }));
            keys = new List<int>();
            charge.Apply(keys, false, 1.5f);
            Assert.That(keys, Is.Empty);
        }

        [Test]
        public void AClickHeldFromBeforeTheItemWasPickedUpIsNotACharge()
        {
            var charge = new ThrowCharge();
            charge.Apply(new List<int> { R }, false, 1f);
            var keys = new List<int> { R };
            charge.Apply(keys, true, 1.1f);  // picked up while the button is down
            Assert.That(charge.Charging, Is.False);
            Assert.That(keys, Is.EquivalentTo(new[] { R }));
        }

        [Test]
        public void DroppingTheItemEndsTheChargeWithoutAThrow()
        {
            var charge = new ThrowCharge();
            charge.Apply(new List<int> { R }, true, 1f);
            var keys = new List<int> { R };
            charge.Apply(keys, false, 1.3f);  // left click dropped it
            Assert.That(charge.Charging, Is.False);
            keys = new List<int>();
            charge.Apply(keys, false, 1.6f);
            Assert.That(keys, Is.Empty);
            Assert.That(charge.Take(1.7f, Throwing.ThrowWindow), Is.EqualTo(1f));
        }

        [Test]
        public void ResetEndsTheChargeAndForgetsTheRelease()
        {
            var charge = new ThrowCharge();
            charge.Apply(new List<int> { R }, true, 1f);
            charge.Reset();
            Assert.That(charge.Charging, Is.False);
            var keys = new List<int>();
            charge.Apply(keys, true, 1.2f);
            Assert.That(keys, Is.Empty);
        }

        [Test]
        public void SkyrimsHudGetsTheChargeInAByte()
        {
            var charge = new ThrowCharge();
            Assert.That(charge.WireCharge(5f), Is.EqualTo(0));  // not charging
            charge.Apply(new List<int> { R }, true, 5f);
            Assert.That(charge.WireCharge(5f), Is.EqualTo(1));  // just started: 1, not 0
            Assert.That(charge.WireCharge(5.5f), Is.EqualTo(128));
            Assert.That(charge.WireCharge(6f), Is.EqualTo(255));
            Assert.That(charge.WireCharge(9f), Is.EqualTo(255));
            charge.Apply(new List<int>(), true, 9f);  // let go
            Assert.That(charge.WireCharge(9f), Is.EqualTo(0));
        }

        [Test]
        public void TheItemIsPulledBackWithTheChargeButNotIntoTheFace()
        {
            Assert.That(ThrowCharge.WindBack(0f, 1f), Is.EqualTo(0f));
            Assert.That(ThrowCharge.WindBack(0.5f, 1f), Is.EqualTo(0.2f).Within(1e-6f));
            Assert.That(ThrowCharge.WindBack(1f, 1f), Is.EqualTo(ThrowCharge.WindBackMax).Within(1e-6f));
            Assert.That(ThrowCharge.WindBack(2f, 1f), Is.EqualTo(ThrowCharge.WindBackMax).Within(1e-6f));
            Assert.That(ThrowCharge.WindBack(1f, 0.5f), Is.EqualTo(0.25f).Within(1e-6f));  // clear of the camera
            Assert.That(ThrowCharge.WindBack(1f, 0.2f), Is.EqualTo(0f));
        }
    }
}
