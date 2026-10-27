using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class FingerTriggerTests
    {
        [Test]
        public void EachFingerCountsOnce()
        {
            var trigger = new FingerTrigger();
            Assert.That(trigger.Step("Initiate"), Is.False);
            Assert.That(trigger.Step("Finger"), Is.True);
            Assert.That(trigger.Step("Finger"), Is.False);
            Assert.That(trigger.Step("Fuck singer"), Is.False);
            Assert.That(trigger.Step("Initiate"), Is.False);
            Assert.That(trigger.Step("Finger"), Is.True);
        }

        [Test]
        public void TheThumbIsNoFinger()
        {
            var trigger = new FingerTrigger();
            Assert.That(trigger.Step("Finger 2"), Is.False);
            Assert.That(trigger.Step("Thumb Up"), Is.False);
            Assert.That(trigger.Step("Finger 3"), Is.False);
        }

        [Test]
        public void AFingerStillUpAtTheHandoverIsNotNew()
        {
            var trigger = new FingerTrigger();
            trigger.Reset();
            Assert.That(trigger.Step("Finger"), Is.False);
            Assert.That(trigger.Step("Initiate"), Is.False);
            Assert.That(trigger.Step("Finger"), Is.True);
        }
    }
}
