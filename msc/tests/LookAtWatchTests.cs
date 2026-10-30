using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class LookAtWatchTests
    {
        [Test]
        public void ReportsATargetWithoutPromptOnceAfterASecond()
        {
            var watch = new LookAtWatch();
            Assert.That(watch.Step("Handle", false, 10f), Is.False);
            Assert.That(watch.Step("Handle", false, 10.5f), Is.False);
            Assert.That(watch.Step("Handle", false, 11f), Is.True);
            Assert.That(watch.Step("Handle", false, 20f), Is.False);
        }

        [Test]
        public void APromptKeepsItQuiet()
        {
            var watch = new LookAtWatch();
            for (float t = 0f; t < 5f; t += 0.25f)
                Assert.That(watch.Step("Handle", true, t), Is.False);
            Assert.That(watch.Step("Handle", false, 5.5f), Is.False);  // the prompt only just went
            Assert.That(watch.Step("Handle", false, 6f), Is.True);
        }

        [Test]
        public void NothingUnderTheCrosshairIsNeverReported()
        {
            var watch = new LookAtWatch();
            Assert.That(watch.Step(null, false, 0f), Is.False);
            Assert.That(watch.Step(null, false, 5f), Is.False);
        }

        [Test]
        public void ANewTargetStartsOver()
        {
            var watch = new LookAtWatch();
            watch.Step("Handle", false, 0f);
            Assert.That(watch.Step("Handle", false, 1f), Is.True);
            Assert.That(watch.Step("Switch", false, 1.5f), Is.False);
            Assert.That(watch.Step("Switch", false, 2.5f), Is.True);
            Assert.That(watch.Step(null, false, 3f), Is.False);
            watch.Step("Handle", false, 3.25f);
            Assert.That(watch.Step("Handle", false, 4.25f), Is.True);  // back on it later: reported again
        }
    }
}
