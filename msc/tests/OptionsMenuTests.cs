using NUnit.Framework;
using PerkeleRim;

namespace PerkeleRim.Tests
{
    public class OptionsMenuTests
    {
        [Test]
        public void OpensOnceWhenAskedAndHolds()
        {
            var m = new OptionsMenu();
            Assert.That(m.Step(false, false, 1f), Is.False);
            Assert.That(m.Holding, Is.False);
            Assert.That(m.Step(true, false, 2f), Is.True);
            Assert.That(m.Holding, Is.True);
            Assert.That(m.Step(true, false, 2.5f), Is.False);  // gives the press a moment
            Assert.That(m.Step(true, true, 2.6f), Is.False);   // up: nothing to do
        }

        [Test]
        public void SkyrimsPauseFreezesMscExceptUnderItsOwnMenu()
        {
            Assert.That(OptionsMenu.Freezes(true, false), Is.True);   // Escape, the map
            Assert.That(OptionsMenu.Freezes(true, true), Is.False);   // F1: toggles and sliders need MSC's clock
            Assert.That(OptionsMenu.Freezes(false, false), Is.False);
        }

        [Test]
        public void PressesAgainWhenTheMenuDidNotComeUp()
        {
            var m = new OptionsMenu();
            Assert.That(m.Step(true, false, 10f), Is.True);
            Assert.That(m.Step(true, false, 10f + OptionsMenu.RetrySeconds), Is.True);
        }

        [Test]
        public void DroppingTheRequestLetsGoAndOpensAtOnceNextTime()
        {
            var m = new OptionsMenu();
            Assert.That(m.Step(true, false, 5f), Is.True);
            Assert.That(m.Step(false, true, 5.1f), Is.False);
            Assert.That(m.Holding, Is.False);
            Assert.That(m.Step(true, false, 5.2f), Is.True);
        }

        [TestCase("Btn_Quit", true)]
        [TestCase("Btn_ConfirmQuit", true)]
        [TestCase("Btn_Resume", false)]
        [TestCase("Btn_Graphics", false)]
        [TestCase("Btn_Reset", false)]
        public void QuitButtonsAreIgnored(string name, bool ignored)
        {
            Assert.That(OptionsMenu.IsIgnored(name), Is.EqualTo(ignored));
        }
    }
}
