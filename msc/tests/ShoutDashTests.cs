using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class ShoutDashTests
    {
        const uint FireBreath = 0x03F9EA;

        static float Run(ShoutDash dash, float step)
        {
            float total = 0f;
            for (int i = 0; i < 1000 && dash.Active; i++) total += dash.Step(step);
            return total;
        }

        [Test]
        public void EachWordGoesFurther()
        {
            Assert.That(ShoutDash.Distance(1), Is.EqualTo(15f));
            Assert.That(ShoutDash.Distance(2), Is.EqualTo(23f));
            Assert.That(ShoutDash.Distance(3), Is.EqualTo(30f));
            Assert.That(ShoutDash.Distance(4), Is.EqualTo(30f));
        }

        [Test]
        public void OnlyWhirlwindSprintDashes()
        {
            var dash = new ShoutDash();
            Assert.That(dash.Start(FireBreath, 3), Is.False);
            Assert.That(dash.Active, Is.False);
            Assert.That(dash.Start(ShoutDash.WhirlwindSprint, 0), Is.False);
            Assert.That(dash.Start(ShoutDash.WhirlwindSprint, 1), Is.True);
            Assert.That(dash.Active, Is.True);
        }

        [Test]
        public void CoversItsDistanceAtSpeedThenStops()
        {
            var dash = new ShoutDash();
            dash.Start(ShoutDash.WhirlwindSprint, 2);
            Assert.That(dash.Step(0.02f), Is.EqualTo(ShoutDash.Speed * 0.02f).Within(1e-5f));
            float total = ShoutDash.Speed * 0.02f + Run(dash, 0.02f);
            Assert.That(total, Is.EqualTo(23f).Within(1e-3f));
            Assert.That(dash.Active, Is.False);
            Assert.That(dash.Step(0.02f), Is.EqualTo(0f));
        }

        [Test]
        public void StopEndsIt()
        {
            var dash = new ShoutDash();
            dash.Start(ShoutDash.WhirlwindSprint, 3);
            dash.Stop();
            Assert.That(dash.Active, Is.False);
            Assert.That(dash.Step(0.02f), Is.EqualTo(0f));
        }
    }
}
