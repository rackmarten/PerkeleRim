using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class RestTests
    {
        [Test]
        public void BedSleepsAsLongAsMscsBedDoes()
        {
            Assert.That(Rest.BedHours(10), Is.EqualTo(2));
            Assert.That(Rest.BedHours(19.9), Is.EqualTo(2));
            Assert.That(Rest.BedHours(20), Is.EqualTo(4));
            Assert.That(Rest.BedHours(59.9), Is.EqualTo(6));
            Assert.That(Rest.BedHours(60), Is.EqualTo(8));
            Assert.That(Rest.BedHours(80), Is.EqualTo(10));
            Assert.That(Rest.BedHours(150), Is.EqualTo(10));
        }

        [Test]
        public void EightHoursInABedTakeAnyFatigueToZeroAndAddMscsRate()
        {
            var n = Rest.Hours(87, 10, true);  // 87 fatigue is MSC's 10 h sleep
            Assert.That(Rest.Hours(150, 8, true).Fatigue, Is.EqualTo(-150).Within(1e-9));
            Assert.That(Rest.Hours(87, 5, true).Fatigue, Is.EqualTo(-87).Within(1e-9));  // twice MSC's rate
            Assert.That(n.Fatigue, Is.EqualTo(-87).Within(1e-9));
            Assert.That(n.Hunger, Is.EqualTo(87 / 5.0).Within(1e-9));  // Calc rates: Rate = fatigue / 5
            Assert.That(n.Thirst, Is.EqualTo(n.Hunger));
            Assert.That(n.Urine, Is.EqualTo(n.Hunger));
        }

        [Test]
        public void WaitingRestsAtHalfMscsRateAndABedAtTwice()
        {
            var bed = Rest.Hours(87, 4, true);
            var wait = Rest.Hours(87, 4, false);
            Assert.That(bed.Fatigue, Is.EqualTo(-87 * 0.8).Within(1e-9));
            Assert.That(wait.Fatigue, Is.EqualTo(bed.Fatigue / 4).Within(1e-9));
            Assert.That(wait.Hunger, Is.EqualTo(bed.Hunger));  // hunger, thirst and urine rise at the sleep rate either way
        }

        [Test]
        public void FatigueNeverGoesBelowZeroAndTheNeedsKeepRising()
        {
            var n = Rest.Hours(15, 24, true);  // 15 fatigue is a 2 h sleep
            Assert.That(n.Fatigue, Is.EqualTo(-15).Within(1e-9));
            Assert.That(n.Hunger, Is.EqualTo(15 / 5.0 / 2 * 24).Within(1e-9));
        }

        [Test]
        public void NothingHappensForAnUntiredPlayerOrNoTime()
        {
            Assert.That(Rest.Hours(0, 8, true).Fatigue, Is.EqualTo(0));
            Assert.That(Rest.Hours(-5, 8, true).Hunger, Is.EqualTo(0));
            Assert.That(Rest.Hours(50, 0, true).Fatigue, Is.EqualTo(0));
        }

        [Test]
        public void TheClockMovesOnAndTheDayCountsMidnights()
        {
            var t = Rest.Advance(new Rest.Time { Sun = 19 * 60.0, Clock = 20 * 60.0 + 5, Day = 3 }, 8);
            Assert.That(t.Sun, Is.EqualTo(3 * 60.0).Within(1e-9));
            Assert.That(t.Clock, Is.EqualTo(4 * 60.0 + 5).Within(1e-9));
            Assert.That(t.Day, Is.EqualTo(4));
            Assert.That(Rest.Advance(new Rest.Time { Sun = 600, Clock = 600, Day = 3 }, 2).Day, Is.EqualTo(3));
        }

        [Test]
        public void TheWeekWrapsAfterSunday()
        {
            Assert.That(Rest.Advance(new Rest.Time { Sun = 23 * 60.0, Clock = 23 * 60.0, Day = 7 }, 2).Day, Is.EqualTo(1));
            Assert.That(Rest.Advance(new Rest.Time { Sun = 0, Clock = 0, Day = 2 }, 48).Day, Is.EqualTo(4));
        }

        [Test]
        public void SunBlocksAndTheirAnglesAreMscs()
        {
            Assert.That(Rest.ColorState(19 * 60.0 + 59), Is.EqualTo("18-20"));
            Assert.That(Rest.ColorState(22 * 60.0), Is.EqualTo("22-00"));
            Assert.That(Rest.ColorState(0), Is.EqualTo("00-02"));
            Assert.That(Rest.ColorState(23.5 * 60 + 90), Is.EqualTo("00-02"));  // wraps
            Assert.That(Rest.ColorTime(30), Is.EqualTo(24));  // "00-02" has Time 24
            Assert.That(Rest.ColorTime(18 * 60.0), Is.EqualTo(18));
            // the dumped states: 10-12 300, 12-14 330, 14-16 0, 18-20 60, 20-22 90, 22-00 120, 00-02 150, 02-04 180, 08-10 270
            Assert.That(Rest.RotationStart(10 * 60.0), Is.EqualTo(300));
            Assert.That(Rest.RotationStart(12 * 60.0), Is.EqualTo(330));
            Assert.That(Rest.RotationStart(14 * 60.0), Is.EqualTo(0));
            Assert.That(Rest.RotationStart(18 * 60.0), Is.EqualTo(60));
            Assert.That(Rest.RotationStart(20 * 60.0), Is.EqualTo(90));
            Assert.That(Rest.RotationStart(22 * 60.0), Is.EqualTo(120));
            Assert.That(Rest.RotationStart(0), Is.EqualTo(150));
            Assert.That(Rest.RotationStart(2 * 60.0), Is.EqualTo(180));
            Assert.That(Rest.RotationStart(8 * 60.0), Is.EqualTo(270));
            Assert.That(Rest.Rotation(18 * 60.0 + 30), Is.EqualTo(75).Within(1e-9));
        }

        [Test]
        public void ClockStatesAreOneToTwentyFour()
        {
            Assert.That(Rest.ClockState(0), Is.EqualTo("24"));
            Assert.That(Rest.ClockState(59), Is.EqualTo("24"));
            Assert.That(Rest.ClockState(60), Is.EqualTo("1"));
            Assert.That(Rest.ClockState(12 * 60.0), Is.EqualTo("12"));
            Assert.That(Rest.ClockState(23 * 60.0 + 59), Is.EqualTo("23"));
            Assert.That(Rest.ClockHours(0), Is.EqualTo(24));
            Assert.That(Rest.ClockMinutes(21 * 60.0 + 5.5), Is.EqualTo(5.5).Within(1e-9));
            Assert.That(Rest.ClockOf(24, 5), Is.EqualTo(5).Within(1e-9));
            Assert.That(Rest.SunOf(24, 30), Is.EqualTo(30).Within(1e-9));
        }
    }
}
