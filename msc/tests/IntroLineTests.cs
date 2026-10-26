using System.Linq;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class IntroLineTests
    {
        static readonly string[] Names = { "drinking", "Human_Snore2", "Human_Snore1", "Human_Burp", "yawn01", "Human_Perkele", "swear09" };

        [Test]
        public void EachCueHasItsSteps()
        {
            Assert.That(IntroLine.For(Wire.IntroBlackout), Is.SameAs(IntroLine.Blackout));
            Assert.That(IntroLine.For(Wire.IntroWake), Is.SameAs(IntroLine.Wake));
            Assert.That(IntroLine.For(0), Is.Empty);
            Assert.That(IntroLine.For(99), Is.Empty);
        }

        [Test]
        public void StepsGoForwardInTime()
        {
            foreach (var steps in new[] { IntroLine.Blackout, IntroLine.Wake })
            {
                Assert.That(steps[0].At, Is.EqualTo(0f));
                for (int i = 1; i < steps.Length; i++) Assert.That(steps[i].At, Is.GreaterThan(steps[i - 1].At));
            }
        }

        [Test]
        public void AnExactNameWinsOverAPart()
        {
            Assert.That(IntroLine.Pick(new[] { "drinking" }, Names), Is.EqualTo("drinking"));
            Assert.That(IntroLine.Pick(new[] { "tosleep", "yawn01" }, Names), Is.EqualTo("yawn01"));
            Assert.That(IntroLine.Pick(new[] { ShoutLine.Perkele }, Names), Is.EqualTo("Human_Perkele"));
        }

        [Test]
        public void APartMatchesIgnoringCaseAndPicksTheFirstInOrdinalOrder()
        {
            Assert.That(IntroLine.Pick(new[] { "snore" }, Names), Is.EqualTo("Human_Snore1"));
            Assert.That(IntroLine.Pick(new[] { "burp" }, Names), Is.EqualTo("Human_Burp"));
        }

        [Test]
        public void NothingFoundIsNull()
        {
            Assert.That(IntroLine.Pick(new[] { "sneeze" }, Names), Is.Null);
            Assert.That(IntroLine.Pick(new[] { "snore" }, new string[0]), Is.Null);
        }

        [Test]
        public void TheWholeBlackoutFindsItsClipsInMscsNames()
        {
            var picks = IntroLine.Blackout.Select(s => IntroLine.Pick(s.Clips, Names)).ToArray();
            Assert.That(picks, Is.EqualTo(new[] { "drinking", "Human_Burp", "yawn01", "Human_Snore1" }));
        }
    }
}
