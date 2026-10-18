using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class FallRescueTests
    {
        [Test]
        public void VehicleAndSeatedUseTheLowestSurfaceAbove()
        {
            Assert.That(FallRescue.Target(false, true, 5f, 0f, 9f), Is.EqualTo(9f));
        }

        [Test]
        public void OnFootGoesBackWhereItLastStood()
        {
            Assert.That(FallRescue.Target(true, true, 0.2f, -0.4f, 9f), Is.EqualTo(0.2f));
        }

        [Test]
        public void OnFootWithoutAMemoryIgnoresACeiling()
        {
            Assert.That(float.IsNaN(FallRescue.Target(true, false, 0f, 0f, 9f)));
        }

        [Test]
        public void OnFootWithoutAMemoryTakesTheFloorJustAbove()
        {
            Assert.That(FallRescue.Target(true, false, 0f, 0f, 0.5f), Is.EqualTo(0.5f));
        }
    }
}
