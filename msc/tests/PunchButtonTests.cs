using System.Collections.Generic;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class PunchButtonTests
    {
        const int H = 104;  // KeyCode.H
        const int W = 119;

        [Test]
        public void ClickWithFreeHandsIsTheHitKeyUntilLetGo()
        {
            var punch = new PunchButton();
            var keys = new List<int> { W, PunchButton.Mouse0 };
            punch.Apply(keys, true, H, 1f);
            Assert.That(keys, Is.EquivalentTo(new[] { W, H }));

            // Still held: stays a punch even though something came under the crosshair meanwhile.
            keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, false, H, 2f);
            Assert.That(keys, Is.EquivalentTo(new[] { H }));

            keys = new List<int> { W };
            punch.Apply(keys, true, H, 3f);
            Assert.That(keys, Is.EquivalentTo(new[] { W }));
            Assert.That(punch.Punching, Is.False);
        }

        [Test]
        public void ClickOnSomethingStaysMscsClick()
        {
            var punch = new PunchButton();
            var keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, false, H, 4f);
            Assert.That(keys, Is.EquivalentTo(new[] { PunchButton.Mouse0 }));

            // Carrying it on with the hands free later (dropped the item) doesn't turn into a punch.
            keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, true, H, 5f);
            Assert.That(keys, Is.EquivalentTo(new[] { PunchButton.Mouse0 }));

            // A new press with free hands does.
            punch.Apply(new List<int>(), true, H, 6f);
            keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, true, H, 7f);
            Assert.That(keys, Is.EquivalentTo(new[] { H }));

            punch.Reset();
            Assert.That(punch.Punching, Is.False);
        }

        [Test]
        public void AFlickeringPromptStillKeepsTheClick()
        {
            var punch = new PunchButton();
            punch.Apply(new List<int>(), false, H, 10.0f);  // hand icon on for a frame
            punch.Apply(new List<int>(), true, H, 10.02f);  // and off again
            var keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, true, H, 10.3f);  // clicked in the gap: MSC's click (pick up)
            Assert.That(keys, Is.EquivalentTo(new[] { PunchButton.Mouse0 }));

            punch.Apply(new List<int>(), true, H, 11.0f);
            keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, true, H, 11.1f);  // no prompt for over half a second: a punch
            Assert.That(keys, Is.EquivalentTo(new[] { H }));
        }
    }
}
