using System.Linq;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class IntroLineTests
    {
        static readonly string[] Names = { "drinking", "Human_Snore2", "Human_Snore1", "Human_Burp", "burb01", "yawn01", "Human_Perkele", "swear09", "hangover01", "hangover05" };

        [Test]
        public void EachCueHasItsSteps()
        {
            Assert.That(IntroLine.For(Wire.IntroBlackout), Is.SameAs(IntroLine.Blackout));
            Assert.That(IntroLine.For(Wire.IntroWake), Is.SameAs(IntroLine.Wake));
            Assert.That(IntroLine.For(Wire.IntroHangover), Is.SameAs(IntroLine.Hangover));
            Assert.That(IntroLine.For(0), Is.Empty);
            Assert.That(IntroLine.For(99), Is.Empty);
        }

        [Test]
        public void StepsGoForwardInTime()
        {
            foreach (var steps in new[] { IntroLine.Blackout, IntroLine.Wake, IntroLine.Hangover })
            {
                Assert.That(steps[0].At, Is.GreaterThanOrEqualTo(0f));  // the blackout opens on a quiet moment
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
        public void TheCartSnoresThenGroansTheHangoverLineThenSaysTheGoodPerkele()
        {
            var blackout = IntroLine.Blackout.Select(s => IntroLine.Pick(s.Clips, Names)).ToArray();
            Assert.That(blackout, Is.EqualTo(new[] { "Human_Snore1", "hangover05" }));
            Assert.That(IntroLine.Blackout.Last().At, Is.EqualTo(IntroLine.HangoverAt));
            Assert.That(IntroLine.Pick(IntroLine.Wake[0].Clips, Names), Is.EqualTo("swear09"));
            Assert.That(IntroLine.Hangover.Select(s => IntroLine.Pick(s.Clips, Names)), Is.EqualTo(new[] { "hangover05" }));
            foreach (var step in IntroLine.Blackout.Concat(IntroLine.Wake).Concat(IntroLine.Hangover))
                Assert.That(step.Clips, Has.None.Contains("Perkele"), "the distorted Human_Perkele isn't used");
        }

        [Test]
        public void TheBlackoutsClipsNeverOverlap()
        {
            // The longest each step can play (its Max, or the hangover line's 9.7 s) ends before the next starts.
            var steps = IntroLine.Blackout;
            for (int i = 0; i + 1 < steps.Length; i++)
            {
                Assert.That(steps[i].Max, Is.GreaterThan(0f), $"step {i} must be cut, MSC's clips are long loops");
                Assert.That(steps[i].At + steps[i].Max, Is.LessThanOrEqualTo(steps[i + 1].At), $"step {i} runs into step {i + 1}");
            }
            Assert.That(IntroLine.Seconds(steps[0], 24.5f), Is.EqualTo(9.5f));
            Assert.That(IntroLine.Seconds(steps[0], 2f), Is.EqualTo(2f));
            Assert.That(IntroLine.Seconds(steps[1], 9.7f), Is.EqualTo(9.7f));
        }

        [Test]
        public void TheHangoverTimeMatchesTheCaptions()
        {
            var header = System.IO.File.ReadAllText(System.IO.Path.Combine(Paths.ProtocolDir, "cpp", "intro_cues.h"));
            var at = System.Text.RegularExpressions.Regex.Match(header, @"kHangoverAt = ([\d.]+)f;");
            Assert.That(at.Success, Is.True);
            Assert.That(float.Parse(at.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture), Is.EqualTo(IntroLine.HangoverAt));
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

    }
}
