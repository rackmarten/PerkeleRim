using System.Collections.Generic;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class HurtLineTests
    {
        static HurtSound Once(uint cause, float damage, double random) => new HurtLine().Decide(cause, damage, 0.0, random);

        [Test]
        public void HitsMostlyGrunt()
        {
            Assert.That(Once(Wire.HurtHit, 0f, HurtLine.HitSwears), Is.EqualTo(HurtSound.Grunt));
            Assert.That(Once(Wire.HurtHit, 0f, 0.99), Is.EqualTo(HurtSound.Grunt));
            Assert.That(Once(Wire.HurtHit, 0f, HurtLine.HitSwears - 0.01), Is.EqualTo(HurtSound.Swear));
            Assert.That(HurtLine.HitSwears, Is.LessThan(0.5));
        }

        [Test]
        public void BlockedHitsMostlySwear()
        {
            Assert.That(Once(Wire.HurtBlocked, 0f, HurtLine.BlockedSwears - 0.01), Is.EqualTo(HurtSound.Swear));
            Assert.That(Once(Wire.HurtBlocked, 0f, HurtLine.BlockedSwears), Is.EqualTo(HurtSound.Grunt));
            Assert.That(HurtLine.BlockedSwears, Is.GreaterThan(0.5));
        }

        [Test]
        public void FallsAndCrashesMostlySwearBadOnesAlways()
        {
            Assert.That(Once(Wire.HurtFall, 0.05f, HurtLine.AccidentSwears - 0.01), Is.EqualTo(HurtSound.Swear));
            Assert.That(Once(Wire.HurtFall, 0.05f, HurtLine.AccidentSwears), Is.EqualTo(HurtSound.Grunt));
            Assert.That(Once(Wire.HurtCrash, 0.1f, 0.99), Is.EqualTo(HurtSound.Grunt));
            Assert.That(Once(Wire.HurtFall, 0.3f, 0.99), Is.EqualTo(HurtSound.Swear));
            Assert.That(Once(Wire.HurtCrash, HurtLine.BadDamage, 0.99), Is.EqualTo(HurtSound.Swear));
            Assert.That(HurtLine.AccidentSwears, Is.GreaterThan(0.5));
        }

        [Test]
        public void LowHealthAlwaysSwears()
        {
            Assert.That(Once(Wire.HurtLow, 0f, 0.0), Is.EqualTo(HurtSound.Swear));
            Assert.That(Once(Wire.HurtLow, 0f, 0.99), Is.EqualTo(HurtSound.Swear));
        }

        [Test]
        public void QuietDuringTheCooldown()
        {
            var line = new HurtLine();
            Assert.That(line.Decide(Wire.HurtHit, 0f, 10.0, 0.99), Is.EqualTo(HurtSound.Grunt));
            Assert.That(line.Decide(Wire.HurtHit, 0f, 10.5, 0.99), Is.EqualTo(HurtSound.None));
            Assert.That(line.Decide(Wire.HurtBlocked, 0f, 11.4, 0.99), Is.EqualTo(HurtSound.None));
            Assert.That(line.Decide(Wire.HurtHit, 0f, 10.0 + HurtLine.Cooldown, 0.99), Is.EqualTo(HurtSound.Grunt));
        }

        [Test]
        public void LowHealthSwearsAtOnceWhenQuiet()
        {
            var line = new HurtLine();
            Assert.That(line.Decide(Wire.HurtLow, 0f, 5.0, 0.99), Is.EqualTo(HurtSound.Swear));
            Assert.That(line.Decide(Wire.HurtHit, 0f, 5.5, 0.99), Is.EqualTo(HurtSound.None));
            Assert.That(line.Due(7.0), Is.EqualTo(HurtSound.None));
        }

        [Test]
        public void LowHealthWaitsForTheCooldown()
        {
            var line = new HurtLine();
            Assert.That(line.Decide(Wire.HurtHit, 0f, 10.0, 0.99), Is.EqualTo(HurtSound.Grunt));
            Assert.That(line.Decide(Wire.HurtLow, 0f, 10.1, 0.99), Is.EqualTo(HurtSound.None));
            Assert.That(line.Due(11.0), Is.EqualTo(HurtSound.None));
            Assert.That(line.Due(11.5), Is.EqualTo(HurtSound.Swear));
            Assert.That(line.Due(11.6), Is.EqualTo(HurtSound.None));        // once
            Assert.That(line.Decide(Wire.HurtHit, 0f, 12.0, 0.99), Is.EqualTo(HurtSound.None));  // the swear's cooldown
        }

        [Test]
        public void ALateLowHealthSwearIsDropped()
        {
            var line = new HurtLine();
            line.Decide(Wire.HurtHit, 0f, 10.0, 0.99);
            line.Decide(Wire.HurtLow, 0f, 10.1, 0.99);
            Assert.That(line.Due(10.1 + HurtLine.LowWait + 0.1), Is.EqualTo(HurtSound.None));
            Assert.That(line.Due(20.0), Is.EqualTo(HurtSound.None));
        }

        [Test]
        public void ASwearInTheMeantimeCountsForLowHealth()
        {
            var line = new HurtLine();
            line.Decide(Wire.HurtHit, 0f, 10.0, 0.99);
            line.Decide(Wire.HurtLow, 0f, 10.1, 0.99);
            Assert.That(line.Decide(Wire.HurtBlocked, 0f, 11.5, 0.0), Is.EqualTo(HurtSound.Swear));
            Assert.That(line.Due(13.0), Is.EqualTo(HurtSound.None));
        }

        static KeyValuePair<string, string> L(string clip, string line) => new KeyValuePair<string, string>(clip, line);

        [Test]
        public void SwearsLeaveThePerkeleToTheShout()
        {
            var lines = new List<KeyValuePair<string, string>>
            {
                L("swear01", "Oh pussy!"),
                L("swear09", "God of thunder!"),
                L("Human_Perkele", null),
                L("Human_Pieru", null),
                L("Human_Vittu", null),
            };
            Assert.That(HurtLine.PickSwear(lines, 0.0), Is.EqualTo("swear01"));
            Assert.That(HurtLine.PickSwear(lines, 0.99), Is.EqualTo("Human_Vittu"));
            Assert.That(HurtLine.PickSwear(new List<KeyValuePair<string, string>> { L("Human_Perkele", null) }, 0.5), Is.Null);
        }

        [Test]
        public void GruntsAreTheOuchClips()
        {
            Assert.That(HurtLine.IsGrunt("ouch10"), Is.True);
            Assert.That(HurtLine.IsGrunt("ouch1"), Is.True);
            Assert.That(HurtLine.IsGrunt("swear01"), Is.False);
            Assert.That(HurtLine.IsGrunt(null), Is.False);
        }
    }
}
