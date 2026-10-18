using System.Collections.Generic;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class KeyRemapTests
    {
        const int W = 119, X = 120, M = 109, N = 110, P = 112, J = 106, O = 111, I = 105, C = 99, G = 103, B = 98;
        const int Ctrl = KeyRemap.LeftControl, Shift = KeyRemap.LeftShift;

        static List<int> Remap(bool driving, params int[] held)
        {
            var keys = new List<int>(held);
            new KeyRemap().Apply(keys, driving);
            return keys;
        }

        [Test]
        public void OnFootEveryAliasBecomesMscsDefaultKey()
        {
            Assert.That(Remap(false, W, KeyRemap.V), Is.EquivalentTo(new[] { W, M }));
            Assert.That(Remap(false, KeyRemap.Mouse2), Is.EquivalentTo(new[] { N }));
            Assert.That(Remap(false, KeyRemap.C), Is.EquivalentTo(new[] { I }));
            Assert.That(Remap(false, KeyRemap.B), Is.EquivalentTo(new[] { Ctrl }));
            Assert.That(Remap(false, KeyRemap.LeftAlt), Is.EquivalentTo(new[] { P }));
            Assert.That(Remap(false, KeyRemap.G), Is.EquivalentTo(new[] { J }));
            Assert.That(Remap(false, Ctrl), Is.EquivalentTo(new[] { C }));
            Assert.That(Remap(false, Shift), Is.EquivalentTo(new[] { Shift }));  // run
        }

        [Test]
        public void TIsSkyrimsWaitMenuOnFootAndTheThumbStaysOnO()
        {
            Assert.That(Remap(false, KeyRemap.T), Is.EquivalentTo(new[] { KeyRemap.T }));  // no thumb alias
            Assert.That(Remap(false, O), Is.EquivalentTo(new[] { O }));
        }

        [Test]
        public void CrouchingAndSmokingAtOnceDoNotMixUp()
        {
            Assert.That(Remap(false, Ctrl, KeyRemap.C), Is.EquivalentTo(new[] { C, I }));
            Assert.That(Remap(false, Ctrl, KeyRemap.B), Is.EquivalentTo(new[] { C, Ctrl }));  // crouch and zoom
        }

        [Test]
        public void DrivingShiftAndCtrlChangeGearAndTheCarKeysStay()
        {
            Assert.That(Remap(true, Shift), Is.EquivalentTo(new[] { G }));
            Assert.That(Remap(true, Ctrl), Is.EquivalentTo(new[] { B }));
            Assert.That(Remap(true, KeyRemap.B), Is.EquivalentTo(new[] { Ctrl }));  // zoom
            Assert.That(Remap(true, KeyRemap.B, Ctrl), Is.EquivalentTo(new[] { Ctrl, B }));
            Assert.That(Remap(true, X, KeyRemap.T, KeyRemap.G), Is.EquivalentTo(new[] { X, KeyRemap.T, G }));
        }

        [Test]
        public void FingerSwearSmokeAndPissWorkDriving()
        {
            Assert.That(Remap(true, KeyRemap.V, KeyRemap.Mouse2, KeyRemap.C, KeyRemap.LeftAlt), Is.EquivalentTo(new[] { M, N, I, P }));
        }

        [Test]
        public void MscsOwnKeysStillWorkAndAreNotDoubled()
        {
            Assert.That(Remap(false, P, I, N), Is.EquivalentTo(new[] { P, I, N }));
            Assert.That(Remap(false, P, KeyRemap.LeftAlt), Is.EquivalentTo(new[] { P }));
        }

        [Test]
        public void FollowsThePlayersOwnBindings()
        {
            var remap = new KeyRemap();
            remap.Bind("Urinate", 117);  // U
            remap.Bind("Jump", 1);       // no alias, ignored
            Assert.That(remap.KeyOf("Urinate"), Is.EqualTo(117));
            var keys = new List<int> { KeyRemap.LeftAlt };
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
            Assert.That(clock.Apply(true, 0.5f), Is.EqualTo(PauseClock.FrozenScale));  // never 0: MSC divides by deltaTime
            Assert.That(clock.Frozen, Is.True);
            Assert.That(clock.Apply(true, PauseClock.FrozenScale), Is.EqualTo(PauseClock.FrozenScale));  // the frozen scale isn't saved
            Assert.That(clock.Apply(false, PauseClock.FrozenScale), Is.EqualTo(0.5f));
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
