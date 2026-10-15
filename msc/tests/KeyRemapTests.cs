using System.Collections.Generic;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class KeyRemapTests
    {
        const int W = 119, M = 109, N = 110, P = 112, J = 106, O = 111, I = 105;

        static List<int> Remap(bool driving, params int[] held)
        {
            var keys = new List<int>(held);
            new KeyRemap().Apply(keys, driving);
            return keys;
        }

        [Test]
        public void OnFootEveryAliasBecomesMscsDefaultKey()
        {
            Assert.That(Remap(false, W, KeyRemap.Z), Is.EquivalentTo(new[] { W, M }));
            Assert.That(Remap(false, KeyRemap.Mouse2), Is.EquivalentTo(new[] { N }));
            Assert.That(Remap(false, KeyRemap.X), Is.EquivalentTo(new[] { P }));
            Assert.That(Remap(false, KeyRemap.G), Is.EquivalentTo(new[] { J }));
            Assert.That(Remap(false, KeyRemap.T), Is.EquivalentTo(new[] { O }));
            Assert.That(Remap(false, KeyRemap.B), Is.EquivalentTo(new[] { I }));
        }

        [Test]
        public void DrivingKeepsTheCarKeysButFingerAndSwearStillWork()
        {
            var car = new[] { KeyRemap.X, KeyRemap.G, KeyRemap.T, KeyRemap.B };
            Assert.That(Remap(true, car), Is.EquivalentTo(car));
            Assert.That(Remap(true, KeyRemap.Z, KeyRemap.Mouse2), Is.EquivalentTo(new[] { M, N }));
        }

        [Test]
        public void MscsOwnKeysStillWorkAndAreNotDoubled()
        {
            Assert.That(Remap(false, P, I, N), Is.EquivalentTo(new[] { P, I, N }));
            Assert.That(Remap(false, P, KeyRemap.X), Is.EquivalentTo(new[] { P }));
        }

        [Test]
        public void FollowsThePlayersOwnBindings()
        {
            var remap = new KeyRemap();
            remap.Bind("Urinate", 117);  // U
            remap.Bind("Jump", 1);       // no alias, ignored
            Assert.That(remap.KeyOf("Urinate"), Is.EqualTo(117));
            var keys = new List<int> { KeyRemap.X };
            remap.Apply(keys, false);
            Assert.That(keys, Is.EquivalentTo(new[] { 117 }));
        }

        [Test]
        public void NoAliasesHeldChangesNothing()
        {
            Assert.That(Remap(false, W, 97), Is.EquivalentTo(new[] { W, 97 }));
            Assert.That(Remap(false), Is.Empty);
        }
    }

    public class PauseClockTests
    {
        [Test]
        public void FreezesWhilePausedAndGivesBackMscsScale()
        {
            var clock = new PauseClock();
            Assert.That(clock.Apply(false, 1f), Is.EqualTo(1f));
            Assert.That(clock.Apply(true, 0.5f), Is.EqualTo(0f));
            Assert.That(clock.Frozen, Is.True);
            Assert.That(clock.Apply(true, 0f), Is.EqualTo(0f));  // its own 0 isn't saved again
            Assert.That(clock.Apply(false, 0f), Is.EqualTo(0.5f));
            Assert.That(clock.Frozen, Is.False);
            Assert.That(clock.Apply(false, 2f), Is.EqualTo(2f));  // MSC's own changes pass through
        }
    }

    public class InputFramePauseTests
    {
        [Test]
        public void PausedOnlyCountsWhileMscOwnsThePlayer()
        {
            var frame = new InputFrame();
            var s = new Protocol.InputState { flags = Protocol.Wire.InputOwnsPlayer | Protocol.Wire.InputPaused };
            frame.Update(ref s);
            Assert.That(frame.Paused, Is.True);
            s.flags = Protocol.Wire.InputPaused;
            frame.Update(ref s);
            Assert.That(frame.Paused, Is.False);
            s.flags = Protocol.Wire.InputOwnsPlayer | Protocol.Wire.InputPaused;
            frame.Update(ref s);
            frame.Reset();
            Assert.That(frame.Paused, Is.False);
        }
    }
}
