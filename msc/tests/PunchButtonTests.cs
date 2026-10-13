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
            punch.Apply(keys, true, H);
            Assert.That(keys, Is.EquivalentTo(new[] { W, H }));

            // Still held: stays a punch even though something came under the crosshair meanwhile.
            keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, false, H);
            Assert.That(keys, Is.EquivalentTo(new[] { H }));

            keys = new List<int> { W };
            punch.Apply(keys, true, H);
            Assert.That(keys, Is.EquivalentTo(new[] { W }));
            Assert.That(punch.Punching, Is.False);
        }

        [Test]
        public void ClickOnSomethingStaysMscsClick()
        {
            var punch = new PunchButton();
            var keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, false, H);
            Assert.That(keys, Is.EquivalentTo(new[] { PunchButton.Mouse0 }));

            // Carrying it on with the hands free later (dropped the item) doesn't turn into a punch.
            keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, true, H);
            Assert.That(keys, Is.EquivalentTo(new[] { PunchButton.Mouse0 }));

            // A new press with free hands does.
            punch.Apply(new List<int>(), true, H);
            keys = new List<int> { PunchButton.Mouse0 };
            punch.Apply(keys, true, H);
            Assert.That(keys, Is.EquivalentTo(new[] { H }));

            punch.Reset();
            Assert.That(punch.Punching, Is.False);
        }
    }
}
