using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class BlackoutTests
    {
        static bool Step(Blackout b, bool owned, float drunk, float now, out uint kind, bool knockedOut = false) =>
            b.Update(owned, drunk, knockedOut, now, out kind);

        [Test]
        public void PassesOutAfterTheDelayAboveTheLine()
        {
            var b = new Blackout();
            uint kind;
            Assert.That(Step(b, true, 3.9f, 0f, out kind), Is.False);
            Assert.That(Step(b, true, 3.9f, Blackout.PassOutDelay - 0.1f, out kind), Is.False);
            Assert.That(Step(b, true, 3.9f, Blackout.PassOutDelay, out kind), Is.True);
            Assert.That(kind, Is.EqualTo(Wire.BlackoutDrunk));
            Assert.That(b.Out, Is.True);
            Assert.That(Step(b, true, 5f, 100f, out kind), Is.False, "once out, nothing more");
        }

        [Test]
        public void DroppingBelowTheLineStartsTheWaitOver()
        {
            var b = new Blackout();
            uint kind;
            Step(b, true, 3.9f, 0f, out kind);
            Assert.That(Step(b, true, 3.8f, 5f, out kind), Is.False, "3.8 itself isn't above");
            Assert.That(Step(b, true, 3.9f, 6f, out kind), Is.False);
            Assert.That(Step(b, true, 3.9f, 6f + Blackout.PassOutDelay - 0.1f, out kind), Is.False);
            Assert.That(Step(b, true, 3.9f, 6f + Blackout.PassOutDelay, out kind), Is.True);
        }

        [Test]
        public void OnlyWhileHandedOver()
        {
            var b = new Blackout();
            uint kind;
            Step(b, true, 4f, 0f, out kind);
            Assert.That(Step(b, false, 4f, 20f, out kind), Is.False);
            Assert.That(Step(b, true, 4f, 21f, out kind), Is.False, "handing over starts the wait over");
            Assert.That(Step(b, true, 4f, 21f + Blackout.PassOutDelay, out kind), Is.True);
            Assert.That(Step(new Blackout(), false, 4f, 0f, out kind, true), Is.False);
        }

        [Test]
        public void AKnockoutIsAtOnce()
        {
            var b = new Blackout();
            uint kind;
            Assert.That(Step(b, true, 0f, 3f, out kind, true), Is.True);
            Assert.That(kind, Is.EqualTo(Wire.BlackoutKnockout));
        }

        [Test]
        public void WithoutAnAnswerHeComesToAndWaitsBeforeTheNext()
        {
            var b = new Blackout();
            uint kind;
            Step(b, true, 0f, 0f, out kind, true);
            Assert.That(b.TimedOut(Blackout.WakeTimeout - 1f), Is.False);
            Assert.That(b.TimedOut(Blackout.WakeTimeout), Is.True);
            b.Woke(Blackout.WakeTimeout, false);
            Assert.That(b.Out, Is.False);
            Assert.That(b.TimedOut(1000f), Is.False);
            float t = Blackout.WakeTimeout;
            Assert.That(Step(b, true, 5f, t + Blackout.Retry - 1f, out kind), Is.False);
            Assert.That(Step(b, true, 5f, t + Blackout.Retry, out kind), Is.False);
            Assert.That(Step(b, true, 5f, t + Blackout.Retry + Blackout.PassOutDelay, out kind), Is.True);
        }

        [Test]
        public void WokenUpBySkyrimTheNextCanComeAtOnce()
        {
            var b = new Blackout();
            uint kind;
            Step(b, true, 0f, 0f, out kind, true);
            b.Woke(30f, true);
            Assert.That(Step(b, true, 0f, 30f, out kind, true), Is.True);
        }

        [Test]
        public void SleepingItOffSobersByMetabolismAndBelowTheLine()
        {
            Assert.That(Blackout.SoberPerHour, Is.EqualTo(0.3975f).Within(1e-4f));
            // 79 kg, adjusted 6 (PlayerDrunk 6.3): 8 hours take 3.18 off, then capped at 2.5 drunk.
            float after = Blackout.Sober(6f, 79f, 8f);
            Assert.That(after, Is.EqualTo(2.5f * 79f / 83f).Within(1e-4f));
            Assert.That(after / (79f / 83f), Is.LessThan(Blackout.PassOutDrunk));
            // A little drunk sobers by the hours only.
            Assert.That(Blackout.Sober(2f, 83f, 2f), Is.EqualTo(2f - 2f * Blackout.SoberPerHour).Within(1e-4f));
            Assert.That(Blackout.Sober(0.5f, 83f, 10f), Is.EqualTo(0f));
            Assert.That(Blackout.Sober(1f, 83f, -3f), Is.EqualTo(1f));
        }
    }
}
