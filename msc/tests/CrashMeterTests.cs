using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class CrashMeterTests
    {
        const float Dt = 0.02f;
        const float G = -9.81f;

        [Test]
        public void SeverityCurve()
        {
            Assert.That(CrashMeter.Severity(0f), Is.EqualTo(0f));
            Assert.That(CrashMeter.Severity(CrashMeter.Harmless), Is.EqualTo(0f));
            Assert.That(CrashMeter.Severity(float.NaN), Is.EqualTo(0f));
            Assert.That(CrashMeter.Severity(CrashMeter.Fatal), Is.EqualTo(CrashMeter.MaxSeverity).Within(1e-6f));
            Assert.That(CrashMeter.Severity(100f), Is.EqualTo(CrashMeter.MaxSeverity).Within(1e-6f));
            Assert.That(CrashMeter.MaxSeverity, Is.LessThan(1f));  // MSC's own death costs more

            // Rises, slowly at first: the midpoint costs well under half.
            float mid = CrashMeter.Severity((CrashMeter.Harmless + CrashMeter.Fatal) / 2f);
            Assert.That(mid, Is.GreaterThan(0f).And.LessThan(CrashMeter.MaxSeverity / 2f));
            float last = 0f;
            for (float i = CrashMeter.Harmless; i <= CrashMeter.Fatal; i += 0.5f)
            {
                float s = CrashMeter.Severity(i);
                Assert.That(s, Is.GreaterThanOrEqualTo(last));
                last = s;
            }
        }

        [Test]
        public void FallingAndBrakingAreNoImpact()
        {
            var meter = new CrashMeter();
            float vy = 0f, vz = 20f;
            for (int i = 0; i < 200; i++)
            {
                vy += G * Dt;           // free fall
                vz -= 9.81f * Dt;       // and a full 1 g brake
                Assert.That(meter.Step(0f, vy, vz, Dt, G), Is.EqualTo(0f));
            }
        }

        [Test]
        public void LandingIsOneImpactOverItsSteps()
        {
            var meter = new CrashMeter();
            meter.Step(0f, -10f, 5f, Dt, G);
            // The landing takes 10 m/s down over three steps, then the van rolls on.
            Assert.That(meter.Step(0f, -6f, 5f, Dt, G), Is.EqualTo(0f));
            Assert.That(meter.Step(0f, -2f, 5f, Dt, G), Is.EqualTo(0f));
            Assert.That(meter.Step(0f, 0f, 5f, Dt, G), Is.EqualTo(0f));
            float impact = meter.Step(0f, 0f, 5f, Dt, G);
            Assert.That(impact, Is.EqualTo(10f + 3 * -G * Dt).Within(0.01f));
            Assert.That(meter.Step(0f, 0f, 5f, Dt, G), Is.EqualTo(0f));
        }

        [Test]
        public void ResetForgetsTheLastVelocity()
        {
            var meter = new CrashMeter();
            meter.Step(0f, 0f, 20f, Dt, G);
            meter.Reset();  // parked: velocity zeroed by hand
            Assert.That(meter.Step(0f, 0f, 0f, Dt, G), Is.EqualTo(0f));
            Assert.That(meter.Step(0f, 0f, 0f, Dt, G), Is.EqualTo(0f));
        }

        [Test]
        public void OneHitPerCooldown()
        {
            var meter = new CrashMeter();
            Assert.That(meter.Hit(CrashMeter.Harmless, 0f), Is.EqualTo(0f));
            Assert.That(meter.Hit(CrashMeter.Fatal, 0f), Is.GreaterThan(0f));
            Assert.That(meter.Hit(CrashMeter.Fatal, CrashMeter.Cooldown / 2f), Is.EqualTo(0f));
            Assert.That(meter.Hit(CrashMeter.Fatal, CrashMeter.Cooldown + 0.01f), Is.GreaterThan(0f));
        }
    }
}
