using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class HangoverTests
    {
        [Test]
        public void ASavedHangoverStartsWhenMscHasNone()
        {
            Assert.That(Hangover.StateToEnter("Shake", 800f, "State 1", 0f), Is.EqualTo("Shake"));
        }

        [Test]
        public void ARunningHangoverOnlyTakesTheSavedVariables()
        {
            Assert.That(Hangover.StateToEnter("Shake", 800f, "Randomize", 300f), Is.Null);
            Assert.That(Hangover.StateToEnter("Shake", 800f, "Shake", 0f), Is.Null);
        }

        [Test]
        public void ASaveWithoutOneLetsItRunOut()
        {
            Assert.That(Hangover.StateToEnter("State 1", 0f, "Shake", 300f), Is.Null);
            Assert.That(Hangover.StateToEnter(null, 0f, "Shake", 300f), Is.Null);
            Assert.That(Hangover.StateToEnter("", 500f, "State 1", 0f), Is.Null);
        }
    }
}
