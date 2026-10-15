using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class FusTriggerTests
    {
        [Test]
        public void FiresWhenTheFingerStarts()
        {
            var fus = new FusTrigger();
            Assert.That(fus.Step("Initiate", 0f), Is.False);
            Assert.That(fus.Step("Check hand 3", 0.1f), Is.False);
            Assert.That(fus.Step("Finger", 0.2f), Is.True);
            Assert.That(fus.Step("Finger", 0.3f), Is.False);  // still the same gesture
            Assert.That(fus.Step("Fuck singer", 1.2f), Is.False);
        }

        [Test]
        public void OtherGesturesDontFire()
        {
            var fus = new FusTrigger();
            foreach (var state in new[] { "Initiate", "Check hand 4", "Fist", "Check hand 1", "Finger 2", "Thumb Up", "Finger 3", "Activate" })
                Assert.That(fus.Step(state, 0f), Is.False, state);
        }

        [Test]
        public void FiresAtMostOncePerCooldown()
        {
            var fus = new FusTrigger();
            Assert.That(fus.Step("Finger", 10f), Is.True);
            Assert.That(fus.Step("Initiate", 10.5f), Is.False);
            Assert.That(fus.Step("Finger", 11f), Is.False);  // 1 s after: too soon, and this gesture is spent
            Assert.That(fus.Step("Finger", 11.6f), Is.False);
            Assert.That(fus.Step("Initiate", 11.7f), Is.False);
            Assert.That(fus.Step("Finger", 11.8f), Is.True);  // the rejected one didn't restart the cooldown
        }

        [Test]
        public void AGestureAlreadyPlayingAfterAResetDoesntFire()
        {
            var fus = new FusTrigger();
            fus.Reset();
            Assert.That(fus.Step("Finger", 0f), Is.False);
            Assert.That(fus.Step("Initiate", 1f), Is.False);
            Assert.That(fus.Step("Finger", 2f), Is.True);
        }
    }
}
