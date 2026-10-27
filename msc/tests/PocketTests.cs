using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class PocketTests
    {
        const int Y = Pocket.PocketKey;
        const int Key1 = 49, Key2 = 50, Key3 = 51, Key4 = 52, Key5 = 53, Key8 = 56, Key9 = 57;
        const int Beer = (int)Wire.PocketBeer, Bottle = (int)Wire.PocketBottle, Cigs = (int)Wire.PocketCigarettes, Junk = (int)Wire.PocketJunk;

        [Test]
        public void ItemNamesMapToKinds()
        {
            Assert.That(Pocket.KindOf("beer case(itemx)"), Is.EqualTo(Beer));
            Assert.That(Pocket.KindOf("empty bottle(Clone)"), Is.EqualTo(Bottle));
            Assert.That(Pocket.KindOf("sausages(itemx)"), Is.EqualTo((int)Wire.PocketSausages));
            Assert.That(Pocket.KindOf("pizza(itemx)"), Is.EqualTo((int)Wire.PocketPizza));
            Assert.That(Pocket.KindOf("potato chips(itemx)"), Is.EqualTo((int)Wire.PocketChips));
            Assert.That(Pocket.KindOf("macaron box(itemx)"), Is.EqualTo((int)Wire.PocketMacaron));
            Assert.That(Pocket.KindOf("spoiled sausages(itemx)"), Is.EqualTo(Pocket.None));
            Assert.That(Pocket.KindOf("empty(itemx)"), Is.EqualTo(Pocket.None));  // a drunk-out beer case
            Assert.That(Pocket.KindOf("ax(itemx)"), Is.EqualTo(Pocket.None));
            Assert.That(Pocket.KindOf(Pocket.JunkName), Is.EqualTo(Junk));
        }

        [Test]
        public void SlotsFollowTheFixedOrderFromThree()
        {
            Assert.That(Pocket.SlotOfKey(Key1), Is.EqualTo(Pocket.None));  // MSC's hands
            Assert.That(Pocket.SlotOfKey(Key2), Is.EqualTo(Pocket.None));  // MSC's tools
            Assert.That(Pocket.KindOfSlot(Pocket.SlotOfKey(Key4)), Is.EqualTo(Beer));
            Assert.That(Pocket.KindOfSlot(Pocket.SlotOfKey(Key3)), Is.EqualTo(Bottle));
            Assert.That(Pocket.KindOfSlot(Pocket.SlotOfKey(Key5)), Is.EqualTo((int)Wire.PocketSausages));
            Assert.That(Pocket.KindOfSlot(Pocket.SlotOfKey(Key8)), Is.EqualTo((int)Wire.PocketMacaron));
            Assert.That(Pocket.SlotOfKey(Key9), Is.EqualTo(Pocket.None));
            Assert.That(Pocket.SlotOfKind(Cigs), Is.EqualTo(Pocket.None));
            Assert.That(Pocket.IsKey(Y) && Pocket.IsKey(Key4) && Pocket.IsKey(Key8), Is.True);
            Assert.That(Pocket.IsKey(Key1) || Pocket.IsKey(Key2) || Pocket.IsKey(Key9), Is.False);
        }

        [Test]
        public void YPocketsWhatIsHeldOrAimedAt()
        {
            Assert.That(Pocket.Decide(Y, Beer, true, Pocket.None, 0), Is.EqualTo(Pocket.Action.Pocket));
            Assert.That(Pocket.Decide(Y, Beer, true, Bottle, 0), Is.EqualTo(Pocket.Action.Pocket));  // the hand first
            Assert.That(Pocket.Decide(Y, Pocket.None, false, Bottle, 0), Is.EqualTo(Pocket.Action.PocketAimed));
            Assert.That(Pocket.Decide(Y, Pocket.None, true, Bottle, 0), Is.EqualTo(Pocket.Action.Nothing));  // a tool in hand
            Assert.That(Pocket.Decide(Y, Pocket.None, false, Pocket.None, 5), Is.EqualTo(Pocket.Action.Nothing));
        }

        [Test]
        public void BeerAndFoodAreUsedAtOnce()
        {
            Assert.That(Pocket.Decide(Key4, Pocket.None, false, Pocket.None, 3), Is.EqualTo(Pocket.Action.Consume));
            Assert.That(Pocket.Decide(Key4, Beer, true, Pocket.None, 3), Is.EqualTo(Pocket.Action.Consume));  // a case in hand stays there
            Assert.That(Pocket.Decide(Key5, Bottle, true, Pocket.None, 1), Is.EqualTo(Pocket.Action.PocketAndConsume));  // the bottle isn't lost
            Assert.That(Pocket.Decide(Key5, (int)Wire.PocketPizza, true, Pocket.None, 1), Is.EqualTo(Pocket.Action.Consume));
            Assert.That(Pocket.Decide(Key4, Pocket.None, false, Pocket.None, 0), Is.EqualTo(Pocket.Action.Nothing));
            Assert.That(Pocket.Consumable(Bottle) || Pocket.Consumable(Cigs) || Pocket.Consumable(Junk), Is.False);
            Assert.That(Pocket.Decide(Key4, Junk, true, Pocket.None, 1), Is.EqualTo(Pocket.Action.PocketAndConsume));  // junk goes back first
        }

        [Test]
        public void InAVehicleOnlyBeerAndFoodWork()
        {
            Assert.That(Pocket.WorksInVehicle(Key4) && Pocket.WorksInVehicle(Key5) && Pocket.WorksInVehicle(Key8), Is.True);
            Assert.That(Pocket.WorksInVehicle(Y) || Pocket.WorksInVehicle(Key3) || Pocket.WorksInVehicle(Key9), Is.False);
        }

        [Test]
        public void EmptyBottlesComeOutSwapAndGoBack()
        {
            Assert.That(Pocket.Decide(Key3, Pocket.None, false, Pocket.None, 2), Is.EqualTo(Pocket.Action.Take));
            Assert.That(Pocket.Decide(Key3, Bottle, true, Pocket.None, 2), Is.EqualTo(Pocket.Action.Pocket));  // same number again
            Assert.That(Pocket.Decide(Key3, (int)Wire.PocketPizza, true, Pocket.None, 1), Is.EqualTo(Pocket.Action.PocketAndTake));
            Assert.That(Pocket.Decide(Key3, Pocket.None, true, Pocket.None, 1), Is.EqualTo(Pocket.Action.DropAndTake));
            Assert.That(Pocket.Decide(Key3, Beer, true, Pocket.None, 0), Is.EqualTo(Pocket.Action.Nothing));
        }

        [Test]
        public void ThreeThrowsBottlesFirstThenJunk()
        {
            Assert.That(Pocket.ThrowKind(2, 5), Is.EqualTo(Bottle));
            Assert.That(Pocket.ThrowKind(0, 5), Is.EqualTo(Junk));
            Assert.That(Pocket.ThrowKind(0, 0), Is.EqualTo(Bottle));  // nothing: the bottle slot, which then has none
            Assert.That(Pocket.Thrown(Bottle) && Pocket.Thrown(Junk), Is.True);
            Assert.That(Pocket.Thrown(Beer), Is.False);
        }

        [Test]
        public void JunkInTheHandGoesBackWithThreeOrY()
        {
            // 3 with junk in hand: back, whether bottles turned up meanwhile or not.
            Assert.That(Pocket.Decide(Key3, Junk, true, Pocket.None, 4), Is.EqualTo(Pocket.Action.Pocket));
            Assert.That(Pocket.Decide(Key3, Bottle, true, Pocket.None, 4), Is.EqualTo(Pocket.Action.Pocket));
            Assert.That(Pocket.Decide(Y, Junk, true, Pocket.None, 0), Is.EqualTo(Pocket.Action.Pocket));
            Assert.That(Pocket.Decide(Key3, Pocket.None, false, Pocket.None, 1), Is.EqualTo(Pocket.Action.Take));  // the one piece of junk left
        }

        static uint[] Counts(uint beer, uint bottles = 0, uint cigs = 0, uint gold = 0, uint junk = 0) => new uint[] { beer, bottles, 0, 0, 0, 0, cigs, gold, 0, junk };

        [Test]
        public void LedgerCountsJunkTakenOutLikeAnyKind()
        {
            var ledger = new PocketLedger(3);
            ledger.OnCounts(3, 0, Counts(0, 0, 0, 0, 2));
            Assert.That(ledger.Available(Junk), Is.EqualTo(2));
            ledger.Sent(ledger.Next(Junk, -1));
            Assert.That(ledger.Available(Junk), Is.EqualTo(1));  // before Skyrim's answer
            ledger.OnCounts(3, 1, Counts(0, 0, 0, 0, 1));  // Skyrim set it aside
            Assert.That(ledger.Available(Junk), Is.EqualTo(1));
        }

        [Test]
        public void LedgerCountsChangesSkyrimHasNotAppliedYet()
        {
            var ledger = new PocketLedger(7);
            Assert.That(ledger.Available(Beer), Is.EqualTo(0));  // nothing known yet
            ledger.OnCounts(0, 0, Counts(2));
            Assert.That(ledger.Available(Beer), Is.EqualTo(2));

            var take = ledger.Next(Beer, -1);
            Assert.That(take.session, Is.EqualTo(7u));
            Assert.That(take.seq, Is.EqualTo(1u));
            ledger.Sent(take);
            ledger.Sent(ledger.Next(Beer, -1));
            Assert.That(ledger.Available(Beer), Is.EqualTo(0));  // two taken before Skyrim answered

            ledger.OnCounts(7, 1, Counts(1));  // Skyrim applied the first only
            Assert.That(ledger.Available(Beer), Is.EqualTo(0));
            ledger.OnCounts(7, 2, Counts(0));
            Assert.That(ledger.Available(Beer), Is.EqualTo(0));
            ledger.Sent(ledger.Next(Bottle, 1));
            Assert.That(ledger.Available(Bottle), Is.EqualTo(1));
        }

        [Test]
        public void LedgerIgnoresAnotherSessionsAppliedChanges()
        {
            var ledger = new PocketLedger(5);
            ledger.Sent(ledger.Next(Beer, 1));
            ledger.OnCounts(4, 9, Counts(3));  // an older MSC's session: our change isn't in there
            Assert.That(ledger.Available(Beer), Is.EqualTo(4));
            ledger.Reset(6);
            Assert.That(ledger.Known, Is.False);
            Assert.That(ledger.Next(Beer, 1).seq, Is.EqualTo(1u));
        }

        [Test]
        public void CigarettesStartFromMscThenFollowEitherSide()
        {
            var mirror = new CountMirror(false);
            bool set;
            long value;
            Assert.That(mirror.Step(15, 0, out set, out value), Is.EqualTo(15));  // MSC's count wins at link up
            Assert.That(set, Is.False);
            Assert.That(mirror.Step(15, 15, out set, out value), Is.EqualTo(0));
            Assert.That(mirror.Step(14, 15, out set, out value), Is.EqualTo(-1));  // smoked one
            Assert.That(mirror.Step(14, 14, out set, out value), Is.EqualTo(0));
            Assert.That(mirror.Step(34, 14, out set, out value), Is.EqualTo(20));  // bought a pack
            Assert.That(mirror.Step(34, 30, out set, out value), Is.EqualTo(0));   // dropped 4 in Skyrim
            Assert.That(set && value == 30, Is.True);
            Assert.That(mirror.Step(30, 30, out set, out value), Is.EqualTo(0));
            Assert.That(set, Is.False);
        }

        [Test]
        public void UnsentCigaretteChangesComeBack()
        {
            var mirror = new CountMirror(false);
            bool set;
            long value;
            int delta = mirror.Step(10, 4, out set, out value);
            mirror.Unsent(delta);  // ring full
            Assert.That(mirror.Step(10, 4, out set, out value), Is.EqualTo(6));
            delta = mirror.Step(9, 10, out set, out value);
            Assert.That(delta, Is.EqualTo(-1));
            mirror.Unsent(delta);
            Assert.That(mirror.Step(9, 10, out set, out value), Is.EqualTo(-1));
            mirror.Reset();
            Assert.That(mirror.Step(9, 3, out set, out value), Is.EqualTo(6));
        }

        [Test]
        public void GoldStartsFromSkyrimThenFollowsEitherSide()
        {
            var mirror = new CountMirror(true);
            bool set;
            long value;
            Assert.That(mirror.Step(3000, 120, out set, out value), Is.EqualTo(0));  // Skyrim's gold wins at link up
            Assert.That(set && value == 120, Is.True);
            Assert.That(mirror.Step(120, 120, out set, out value), Is.EqualTo(0));
            Assert.That(set, Is.False);
            Assert.That(mirror.Step(68, 120, out set, out value), Is.EqualTo(-52));  // a beer case bought in MSC
            Assert.That(mirror.Step(68, 168, out set, out value), Is.EqualTo(0));    // 100 gold looted in Skyrim
            Assert.That(set && value == 168, Is.True);
            Assert.That(mirror.Step(168, 168, out set, out value), Is.EqualTo(0));
            Assert.That(set, Is.False);
            mirror.Reset();
            Assert.That(mirror.Step(168, 168, out set, out value), Is.EqualTo(0));   // equal at link up: nothing to set
            Assert.That(set, Is.False);
        }

        [Test]
        public void MoneyIsWholeMarksAndKeepsThePennies()
        {
            Assert.That(Pocket.Gold(152.75f), Is.EqualTo(152));
            Assert.That(Pocket.Gold(0.5f), Is.EqualTo(0));
            Assert.That(Pocket.Gold(-10f), Is.EqualTo(0));
            Assert.That(Pocket.Money(200, 152.75f), Is.EqualTo(200.75f).Within(0.001f));
            Assert.That(Pocket.Money(0, 152.75f), Is.EqualTo(0.75f).Within(0.001f));
            Assert.That(Pocket.Money(40, -3f), Is.EqualTo(40f));
            Assert.That(Pocket.Gold(Pocket.Money(41, 152.75f)), Is.EqualTo(41));
            Assert.That(Pocket.Gold(Pocket.Money(16777, 152.99999f)), Is.EqualTo(16777));  // no mark minted by rounding
        }
    }
}
