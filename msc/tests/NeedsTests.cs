using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class NeedsTests
    {
        [Test]
        public void AFullBladderSlowsKumpoFromTheMarkTo200()
        {
            Assert.That(Needs.UrineSpeed(0), Is.EqualTo(1f));
            Assert.That(Needs.UrineSpeed(100), Is.EqualTo(1f));
            Assert.That(Needs.UrineSpeed(150), Is.EqualTo(0.85f).Within(1e-5));
            Assert.That(Needs.UrineSpeed(200), Is.EqualTo(0.7f).Within(1e-5));
            Assert.That(Needs.UrineSpeed(260), Is.EqualTo(0.7f).Within(1e-5));
        }
    }
}
