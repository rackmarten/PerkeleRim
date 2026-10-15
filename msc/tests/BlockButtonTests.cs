using System.Collections.Generic;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class BlockButtonTests
    {
        const int H = 104;  // KeyCode.H
        const int U = 117;  // KeyCode.U
        const int W = 119;
        const int R = BlockButton.Mouse1;
        const int L = PunchButton.Mouse0;

        [Test]
        public void RightClickWithFreeHandsIsTheWatchKeyUntilLetGo()
        {
            var block = new BlockButton();
            var keys = new List<int> { W, R };
            block.Apply(keys, true, U, H, "Initiate", 1f);
            Assert.That(block.Blocking, Is.True);
            Assert.That(keys, Is.EquivalentTo(new[] { W, U }));

            // The raised watch marks the left hand busy: still guarding.
            keys = new List<int> { R };
            block.Apply(keys, false, U, H, "Watch ON 2", 2f);
            Assert.That(keys, Is.EquivalentTo(new[] { U }));

            keys = new List<int> { W };
            block.Apply(keys, true, U, H, "Watch ON 2", 3f);
            Assert.That(block.Blocking, Is.False);
            Assert.That(keys, Is.EquivalentTo(new[] { W }));
        }

        [Test]
        public void NoPunchingWhileGuarding()
        {
            var block = new BlockButton();
            var punch = new PunchButton();
            var keys = new List<int> { R, L, H };
            block.Apply(keys, true, U, H, "Initiate", 1f);
            punch.Apply(keys, true, H, 1f);
            Assert.That(keys, Is.EquivalentTo(new[] { U }));
            Assert.That(punch.Punching, Is.False);
        }

        [Test]
        public void ItemInHandPromptOrVehicleKeepsMscsRightClick()
        {
            // Each of them makes the hands not free (PlayerLink.HandsFree): the click stays MSC's
            // (with an item in hand, its throw), and left click is untouched.
            var block = new BlockButton();
            var keys = new List<int> { R, L };
            block.Apply(keys, false, U, H, "Initiate", 1f);
            Assert.That(block.Blocking, Is.False);
            Assert.That(keys, Is.EquivalentTo(new[] { R, L }));

            // Held on after the hands came free: still not a guard. A new press is.
            keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Initiate", 2f);
            Assert.That(keys, Is.EquivalentTo(new[] { R }));
            block.Apply(new List<int>(), true, U, H, "Initiate", 3f);
            keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Initiate", 3.1f);
            Assert.That(keys, Is.EquivalentTo(new[] { U }));
        }

        [Test]
        public void AFlickeringPromptStillKeepsTheClick()
        {
            var block = new BlockButton();
            block.Apply(new List<int>(), false, U, H, "Initiate", 10.0f);
            block.Apply(new List<int>(), true, U, H, "Initiate", 10.02f);
            var keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Initiate", 10.3f);
            Assert.That(keys, Is.EquivalentTo(new[] { R }));
        }

        [Test]
        public void PressedAgainAsTheWatchGoesDownTheKeyIsLetGoForAFrame()
        {
            var block = new BlockButton();
            var keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Initiate", 1f);
            Assert.That(keys, Is.EquivalentTo(new[] { U }));
            block.Apply(new List<int>(), true, U, H, "Anim off", 1.1f);  // let go

            keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Anim off", 1.2f);  // pressed again while it lowers
            Assert.That(keys, Is.EquivalentTo(new[] { U }));
            keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Initiate", 1.3f);  // back in Initiate with U held: a frame off
            Assert.That(keys, Is.Empty);
            keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Initiate", 1.31f);  // a fresh press
            Assert.That(keys, Is.EquivalentTo(new[] { U }));
            keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Anim on", 1.32f);
            Assert.That(keys, Is.EquivalentTo(new[] { U }));
        }

        [Test]
        public void WatchStatesAreTheGuard()
        {
            Assert.That(BlockButton.Watching("Watch ON 2"), Is.True);
            Assert.That(BlockButton.Watching("Anim off"), Is.True);
            Assert.That(BlockButton.Watching("Initiate"), Is.False);
            Assert.That(BlockButton.Watching("Finger"), Is.False);
            Assert.That(BlockButton.Watching(null), Is.False);

            var block = new BlockButton();
            var keys = new List<int> { R };
            block.Apply(keys, true, U, H, "Initiate", 1f);
            block.Reset();
            Assert.That(block.Blocking, Is.False);
        }
    }
}
