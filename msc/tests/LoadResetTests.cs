using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class LoadResetTests
    {
        [Test]
        public void PissingStopsAtTheStartState()
        {
            foreach (var s in new[] { "Full power", "Pump", "State 4" })
                Assert.That(LoadReset.Target(LoadReset.Piss, s), Is.EqualTo("State 1"), s);  // MSC's own ending
            foreach (var s in new[] { "Check car", "State 2", "State 5" })
                Assert.That(LoadReset.Target(LoadReset.Piss, s), Is.EqualTo("Start urinate"), s);
            Assert.That(LoadReset.Target(LoadReset.Piss, "State 1"), Is.Null);
            Assert.That(LoadReset.Target(LoadReset.Piss, "Start urinate"), Is.Null);
            Assert.That(LoadReset.Target(LoadReset.Piss, "Seated"), Is.Null);
        }

        [Test]
        public void HandActionsAreLetGoOfButTheComputerStays()
        {
            foreach (var s in new[] { "On", "Off", "Finger 2", "Thumb Up", "Finger", "Fist", "Watch ON 2", "Activate" })
                Assert.That(LoadReset.Target(LoadReset.Hands, s), Is.EqualTo("Initiate"), s);
            Assert.That(LoadReset.Target(LoadReset.Hands, "Initiate"), Is.Null);
            Assert.That(LoadReset.Target(LoadReset.Hands, "Computer"), Is.Null);
        }

        [Test]
        public void DrinkingGoesBackToIdle()
        {
            Assert.That(LoadReset.Target(LoadReset.Drink, "Activate"), Is.EqualTo("State 4"));
            Assert.That(LoadReset.Target(LoadReset.Drink, "Drink anim"), Is.EqualTo("State 4"));
            Assert.That(LoadReset.Target(LoadReset.Drink, "State 4"), Is.Null);
        }

        [Test]
        public void ZoomEasesOutTheWayItWentIn()
        {
            Assert.That(LoadReset.Target(LoadReset.Zoom, "Zoomed"), Is.EqualTo("Zoom out"));
            Assert.That(LoadReset.Target(LoadReset.Zoom, "Zoom in"), Is.EqualTo("Zoom out"));
            Assert.That(LoadReset.Target(LoadReset.Zoom, "Zoomed in"), Is.EqualTo("Zoom out 2"));
            Assert.That(LoadReset.Target(LoadReset.Zoom, "State 1"), Is.Null);
            Assert.That(LoadReset.Target(LoadReset.Zoom, "Zoom out"), Is.Null);
        }

        [Test]
        public void CrouchStandsUpFromAnyDepth()
        {
            foreach (var s in new[] { "Move down 1", "Crouched 1", "Move down 2", "Crouched 2" })
                Assert.That(LoadReset.Target(LoadReset.Crouch, s), Is.EqualTo("Move up"), s);
            foreach (var s in new[] { "Wait key", "Move up", "Incar", "Seated", "In water" })
                Assert.That(LoadReset.Target(LoadReset.Crouch, s), Is.Null, s);
        }

        [Test]
        public void UnknownFsmsAndStatesAreLeftAlone()
        {
            Assert.That(LoadReset.Target("Other", "Anything"), Is.Null);
            Assert.That(LoadReset.Target(LoadReset.Piss, null), Is.Null);
            Assert.That(LoadReset.Target(LoadReset.Piss, ""), Is.Null);
        }

        [Test]
        public void TheResetListNamesEveryTransientAction()
        {
            Assert.That(LoadReset.Objects, Does.Contain("Smoking"));
            Assert.That(LoadReset.Objects, Does.Contain("Hand Push"));
            Assert.That(LoadReset.Objects, Does.Contain("MiddleFinger"));
            Assert.That(LoadReset.Flags, Does.Contain("PlayerHandLeft"));
            Assert.That(LoadReset.Flags, Does.Contain("PlayerHandRight"));
            Assert.That(LoadReset.Flags, Does.Contain("PlayerDrinkOn"));
            Assert.That(LoadReset.Drops, Does.Contain("DROP_PART"));
        }
    }
}
