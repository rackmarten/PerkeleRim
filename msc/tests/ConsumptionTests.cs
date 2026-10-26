using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class ConsumptionTests
    {
        [Test]
        public void FinishedAlcoholicDrinksCountByKind()
        {
            Assert.That(Consumption.Kind("Drink", "Drink anim", "Throw bottle", false), Is.EqualTo(Wire.ConsumedBeer));
            Assert.That(Consumption.Kind("Drink", "Drink anim 1", "Throw bottle 1", false), Is.EqualTo(Wire.ConsumedBooze));
            Assert.That(Consumption.Kind("Drink", "Drink anim 2", "Throw glass", false), Is.EqualTo(Wire.ConsumedShot));
            Assert.That(Consumption.Kind("Drink", "Drink anim 8", "Throw bottle 2", false), Is.EqualTo(Wire.ConsumedSpirit));
            Assert.That(Consumption.Kind("Drink", "Kilju", "Calc", false), Is.EqualTo(Wire.ConsumedKilju));
        }

        [Test]
        public void StartedInterruptedOrSoftDrinksDoNot()
        {
            Assert.That(Consumption.Kind("Drink", "State 4", "Activate", false), Is.Zero);      // DRINKBEER: just started
            Assert.That(Consumption.Kind("Drink", "Activate", "Drink anim", false), Is.Zero);
            Assert.That(Consumption.Kind("Drink", "Drink anim", "Activate", false), Is.Zero);    // a new DRINKBEER mid-drink
            Assert.That(Consumption.Kind("Drink", "Drink anim 3", "Throw milk", false), Is.Zero);
            Assert.That(Consumption.Kind("Drink", "Juice", "Burb", false), Is.Zero);
            Assert.That(Consumption.Kind("Drink", "Drink anim 5", "Throw glass 2", false), Is.Zero);  // coffee
            Assert.That(Consumption.Kind("Drink", "Throw bottle", "Burb", false), Is.Zero);      // after the beer: counted once
        }

        [Test]
        public void ACigaretteCountsWhenLit()
        {
            Assert.That(Consumption.Kind("PlayerFunctions", "Check tobacco", "Activate", false), Is.EqualTo(Wire.ConsumedCigarette));
            Assert.That(Consumption.Kind("PlayerFunctions", "Check tobacco", "No cigaretes", false), Is.Zero);
            Assert.That(Consumption.Kind("PlayerFunctions", "Check hand 4", "Fist", false), Is.Zero);
        }

        [Test]
        public void FoodCountsWhenEaten()
        {
            Assert.That(Consumption.Kind("Use", "Eat", "Destroy", false), Is.EqualTo(Wire.ConsumedFood));     // pizza, sausages, chips
            Assert.That(Consumption.Kind("Use", "Eat 2", "Destroy", false), Is.EqualTo(Wire.ConsumedFood));   // macaron box
            Assert.That(Consumption.Kind("Use", "Fresh", "Destroy", false), Is.EqualTo(Wire.ConsumedFood));   // one sausage
            Assert.That(Consumption.Kind("Use", "Grilled", "Destroy", false), Is.EqualTo(Wire.ConsumedFood));
            Assert.That(Consumption.Kind("Use", "Wait button 2", "Eat 2", false), Is.Zero);  // started
            Assert.That(Consumption.Kind("Use", "Eat 2", "Wait player 2", false), Is.Zero);  // STOP
            Assert.That(Consumption.Kind("Use", "Load", "Destroy self", false), Is.Zero);    // a saved, eaten item going away
        }

        [Test]
        public void BottlesLeaveTheirDrinkToTheDrinkFsm()
        {
            Assert.That(Consumption.Kind("Use", "Eat 2", "Destroy", true), Is.Zero);
            Assert.That(Consumption.Kind("Use", "Eat 2", "Play anim", true), Is.Zero);
        }

        [Test]
        public void OtherFsmsAndMissingNamesAreIgnored()
        {
            Assert.That(Consumption.Kind("Death", "Drink anim", "Throw bottle", false), Is.Zero);
            Assert.That(Consumption.Kind("Use", null, "Destroy", false), Is.Zero);
            Assert.That(Consumption.Kind(null, "Eat", "Destroy", false), Is.Zero);
        }

        [Test]
        public void SkyrimFoodLowersNeedsDownToZero()
        {
            Assert.That(Consumption.AddNeed(60f, -15f), Is.EqualTo(45f));   // a sweet roll
            Assert.That(Consumption.AddNeed(10f, -15f), Is.EqualTo(0f));    // full
            Assert.That(Consumption.AddNeed(-5f, -15f), Is.EqualTo(-5f));   // already below 0 from MSC: stays
            Assert.That(Consumption.AddNeed(30f, 5f), Is.EqualTo(35f));     // chips make him thirsty
            Assert.That(Consumption.AddNeed(-5f, 2f), Is.EqualTo(-3f));
        }

        [Test]
        public void SkyrimAlcoholAddsDrunkUpToEight()
        {
            Assert.That(Consumption.AddDrunk(0.4f, 0.08f), Is.EqualTo(0.48f).Within(0.0001f));  // an ale
            Assert.That(Consumption.AddDrunk(7f, 2.5f), Is.EqualTo(8f));                          // Skooma on top
            Assert.That(Consumption.AddDrunk(0f, 0f), Is.EqualTo(0f));
        }
    }
}
