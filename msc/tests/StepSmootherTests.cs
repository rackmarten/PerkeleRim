using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class StepSmootherTests
    {
        const float Frame = 1f / 60f;

        [Test]
        public void AStepUpIsTakenOutAndFadesIn()
        {
            var s = new StepSmoother();
            s.Update(new Vec3(0f, 0f, 0f), true, Frame);
            float offset = s.Update(new Vec3(0f, 0.3f, 0.1f), true, Frame);
            Assert.That(offset, Is.LessThan(-0.25f));  // the eye stays about where it was
            for (int i = 0; i < 60; i++) offset = s.Update(new Vec3(0f, 0.3f, 0.1f), true, Frame);
            Assert.That(offset, Is.EqualTo(0f).Within(0.01f));  // and is back on the feet within a second
        }

        [Test]
        public void ASlopeIsLeftAlone()
        {
            var s = new StepSmoother();
            s.Update(new Vec3(0f, 0f, 0f), true, Frame);
            Assert.That(s.Update(new Vec3(0f, 0.05f, 0.15f), true, Frame), Is.EqualTo(0f));
        }

        [Test]
        public void AirborneAndTallDropsAreLeftAlone()
        {
            var s = new StepSmoother();
            s.Update(new Vec3(0f, 0f, 0f), true, Frame);
            Assert.That(s.Update(new Vec3(0f, -0.3f, 0f), false, Frame), Is.EqualTo(0f));  // a jump or a fall
            Assert.That(s.Update(new Vec3(0f, -2f, 0f), true, Frame), Is.EqualTo(0f));     // landing from high up
        }

        [Test]
        public void ResetForgetsTheOffset()
        {
            var s = new StepSmoother();
            s.Update(new Vec3(0f, 0f, 0f), true, Frame);
            s.Update(new Vec3(0f, 0.3f, 0f), true, Frame);
            s.Reset();
            Assert.That(s.Offset, Is.EqualTo(0f));
            Assert.That(s.Update(new Vec3(0f, 0.6f, 0f), true, Frame), Is.EqualTo(0f));  // no last position to compare with
        }
    }
}
