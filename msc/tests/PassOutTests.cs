using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class PassOutTests
    {
        [Test]
        public void EachBlackoutWaitsInItsOwnIdleState()
        {
            Assert.That(PassOut.Idle(PassOut.PassOutObject), Is.EqualTo("Test alc"));
            Assert.That(PassOut.Idle(PassOut.KnockOutObject), Is.EqualTo("State 1"));
            Assert.That(PassOut.Idle("Death"), Is.Null);
        }

        [Test]
        public void ABlackoutRunsFromTheFirstStatePastIdle()
        {
            foreach (var state in new[] { "Check hunger", "Pass out", "Where?", "Set position", "AnimateWake", "State 5" })
                Assert.That(PassOut.Running(PassOut.PassOutObject, state), Is.True, state);
            Assert.That(PassOut.Running(PassOut.KnockOutObject, "Move"), Is.True);
        }

        [Test]
        public void IdleOrUnknownIsntRunning()
        {
            Assert.That(PassOut.Running(PassOut.PassOutObject, "Test alc"), Is.False);
            Assert.That(PassOut.Running(PassOut.KnockOutObject, "State 1"), Is.False);
            Assert.That(PassOut.Running(PassOut.PassOutObject, ""), Is.False);
            Assert.That(PassOut.Running(PassOut.PassOutObject, null), Is.False);
            Assert.That(PassOut.Running("Death", "Permadeath 2"), Is.False);
        }
    }
}
