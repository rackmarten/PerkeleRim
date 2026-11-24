using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class SatsumaModelTests
    {
        [Test]
        public void PartsAreTheCopiesTheLooseOnesAndWhatHasARemovalFsm()
        {
            Assert.That(SatsumaModel.IsPart("radiator(xxxxx)", false), "a copy MSC switches on");
            Assert.That(SatsumaModel.IsPart("wiring_coil(xxxxx)", false), "wiring has no Removal FSM");
            Assert.That(SatsumaModel.IsPart("door left(Clone)", true), "a loose part hung on a pivot");
            Assert.That(SatsumaModel.IsPart("wheel_regula", true), "the wheels go by their Removal FSM");
        }

        [Test]
        public void TheBodyAndItsOwnObjectsAreNotParts()
        {
            Assert.That(SatsumaModel.IsPart("car body(xxxxx)", false), Is.False, "the body shell");
            Assert.That(SatsumaModel.IsPart("car body masse(xxxxx)", false), Is.False);
            Assert.That(SatsumaModel.IsPart("pivot_door_left", false), Is.False);
            Assert.That(SatsumaModel.IsPart("trigger_radiator", false), Is.False);
            Assert.That(SatsumaModel.IsPart("panel_door_left2", false), Is.False);
            Assert.That(SatsumaModel.IsPart(null, false), Is.False);
        }

        [Test]
        public void HalfBuiltPutsOnEachTriggerOnce()
        {
            Assert.That(SatsumaModel.HalfBuilt, Is.Unique);
            Assert.That(SatsumaModel.HalfBuilt, Has.All.StartsWith("trigger_"));
        }
    }
}
