using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class BumpsTests
    {
        [Test]
        public void SmallCollisionsAreQuiet()
        {
            Assert.That(Bumps.QuietCollision(0.5f), Is.True);
            Assert.That(Bumps.QuietCollision(Bumps.QuietBelow - 0.01f), Is.True);
            Assert.That(Bumps.QuietCollision(Bumps.QuietBelow), Is.False);
            Assert.That(Bumps.QuietCollision(12f), Is.False);
        }

        [Test]
        public void OnlyHardImpactsAreCrashes()
        {
            Assert.That(Bumps.Crash(2f), Is.False);
            Assert.That(Bumps.Crash(Bumps.Stalls - 0.1f), Is.False);
            Assert.That(Bumps.Crash(Bumps.Stalls), Is.True);
            Assert.That(Bumps.Crash(15f), Is.True);
            // Crashes stay above what hurts nothing.
            Assert.That(Bumps.Stalls, Is.GreaterThan(CrashMeter.Harmless));
        }

        [Test]
        public void AnImpactShieldsTheEngineForAWhileAfterIt()
        {
            var guard = new StallGuard();
            Assert.That(guard.Protects(0f), Is.False);
            Assert.That(guard.Step(true, 0f, 10f), Is.False);   // an impact begins
            Assert.That(guard.Protects(10f), Is.True);
            Assert.That(guard.Step(false, 2.5f, 10.1f), Is.False);  // a bump ends
            Assert.That(guard.Protects(10.1f + Bumps.Hold - 0.01f), Is.True);
            Assert.That(guard.Protects(10.1f + Bumps.Hold), Is.False);
        }

        [Test]
        public void StepsWithNoImpactLeaveTheGuardAlone()
        {
            var guard = new StallGuard();
            Assert.That(guard.Step(false, 0f, 5f), Is.False);
            Assert.That(guard.Protects(5f), Is.False);
        }

        [Test]
        public void ACrashStallsTheEngineAndLiftsTheGuard()
        {
            var guard = new StallGuard();
            guard.Step(true, 0f, 3f);
            Assert.That(guard.Protects(3f), Is.True);
            Assert.That(guard.Step(false, Bumps.Stalls + 1f, 3.1f), Is.True);
            Assert.That(guard.Protects(3.1f), Is.False);
        }

        [Test]
        public void ResetLiftsTheGuard()
        {
            var guard = new StallGuard();
            guard.Step(true, 0f, 1f);
            guard.Reset();
            Assert.That(guard.Protects(1f), Is.False);
        }

        [Test]
        public void ABounceOnItsWheelsCountsHalf()
        {
            Assert.That(CrashMeter.Soften(8f, 0.95f, 1f), Is.EqualTo(4f));           // along its up axis, upright
            Assert.That(CrashMeter.Soften(8f, 0.3f, 1f), Is.EqualTo(8f));            // into a wall
            Assert.That(CrashMeter.Soften(8f, 0.95f, 0.2f), Is.EqualTo(8f));         // on its side
            Assert.That(CrashMeter.Soften(8f, CrashMeter.LandingAlongUp, CrashMeter.LandingUpY), Is.EqualTo(4f));
        }

        [Test]
        public void TheImpactSaysWhichWayItHit()
        {
            var meter = new CrashMeter();
            const float g = -9.81f, dt = 0.02f;
            meter.Step(0, 0, 10, dt, g);
            meter.Step(0, 6 + g * dt, 10, dt, g);          // 6 m/s straight up
            meter.Step(0, 6 + 2 * g * dt, 10, dt, g);      // quiet step ends it
            Assert.That(meter.AlongUp(0, 1, 0), Is.EqualTo(1f).Within(1e-4f));
            Assert.That(meter.AlongUp(0, 0, 1), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(meter.AlongUp(0.6f, 0.8f, 0), Is.EqualTo(0.8f).Within(1e-4f));
        }

        [Test]
        public void CrashMeterSaysWhenAnImpactIsGoingOn()
        {
            var meter = new CrashMeter();
            const float g = -9.81f, dt = 0.02f;
            Assert.That(meter.Step(0, 0, 10, dt, g), Is.EqualTo(0f));
            Assert.That(meter.InImpact, Is.False);
            // A 3 m/s sideways step is part of an impact, the quiet step after it ends it.
            Assert.That(meter.Step(3, g * dt, 10, dt, g), Is.EqualTo(0f));
            Assert.That(meter.InImpact, Is.True);
            float impact = meter.Step(3, 2 * g * dt, 10, dt, g);
            Assert.That(impact, Is.EqualTo(3f).Within(0.01f));
            Assert.That(meter.InImpact, Is.False);
        }
    }
}
