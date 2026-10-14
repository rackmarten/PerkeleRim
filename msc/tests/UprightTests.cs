using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class UprightTests
    {
        [Test]
        public void RolledOnlyOnItsSideOrRoof()
        {
            Assert.That(Upright.Rolled(1f), Is.False);       // on its wheels
            Assert.That(Upright.Rolled(0.8f), Is.False);     // 37 degrees, a steep road
            Assert.That(Upright.Rolled(0f), Is.True);        // on its side
            Assert.That(Upright.Rolled(-1f), Is.True);       // on its roof
            Assert.That(Upright.Rolled(float.NaN), Is.False);
        }

        [Test]
        public void RightsOnlyWhenStillAndClose()
        {
            Assert.That(Upright.CanRight(-1f, 0.2f, 0.1f, 2f), Is.True);
            Assert.That(Upright.CanRight(1f, 0f, 0f, 2f), Is.False);                    // not rolled: MSC's push
            Assert.That(Upright.CanRight(0f, 3f, 0f, 2f), Is.False);                    // still sliding
            Assert.That(Upright.CanRight(0f, 0f, 2f, 2f), Is.False);                    // still tumbling
            Assert.That(Upright.CanRight(0f, 0f, 0f, Upright.Reach + 0.1f), Is.False);  // too far
            Assert.That(Upright.CanRight(0f, 0f, 0f, Upright.Reach), Is.True);
        }

        [Test]
        public void FacesItsFlattenedForward()
        {
            Assert.That(Upright.Heading(0f, 1f, 0f, 0f), Is.EqualTo(0f).Within(1e-4f));
            Assert.That(Upright.Heading(1f, 0f, 0f, 1f), Is.EqualTo(90f).Within(1e-4f));
            // On its roof facing -Z: forward still -Z.
            Assert.That(System.Math.Abs(Upright.Heading(0f, -1f, 0f, 0f)), Is.EqualTo(180f).Within(1e-4f));
            // On its nose after going +X: forward points down, the roof +X.
            Assert.That(Upright.Heading(0.01f, 0f, 1f, 0f), Is.EqualTo(90f).Within(1e-4f));
        }
    }
}
