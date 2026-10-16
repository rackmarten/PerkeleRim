using System.Collections.Generic;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class ShoutLineTests
    {
        static KeyValuePair<string, string> L(string clip, string line) => new KeyValuePair<string, string>(clip, line);

        static readonly List<KeyValuePair<string, string>> Lines = new List<KeyValuePair<string, string>>
        {
            L("swear01", "Satan!"),
            L("swear05", "God of thunder!"),
            L("Human_Vittu", null),
            L("Human_Pieru", null),
            L("swear07", "Perkele!"),
            L("Human_Perkele", null),
            L("fuck01", "You dork of the pussy."),
            L("Human_Saatana", ""),
        };

        [Test]
        public void TheGodOfThunderIsSwear09()
        {
            Assert.That(ShoutLine.SwearLines.Length, Is.EqualTo(16));
            Assert.That(ShoutLine.SwearClip(8), Is.EqualTo("swear09"));
            Assert.That(ShoutLine.IsThunder(ShoutLine.SwearLines[8]), Is.True);
        }

        [Test]
        public void UnrelentingForceIsMostlyTheGodOfThunder()
        {
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, Lines, 0.0, 0.5), Is.EqualTo("swear05"));
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, Lines, 0.74, 0.99), Is.EqualTo("swear05"));
        }

        [Test]
        public void SometimesAnotherPerkele()
        {
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, Lines, 0.8, 0.0), Is.EqualTo("swear07"));
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, Lines, 0.8, 0.99), Is.EqualTo("Human_Perkele"));
        }

        [Test]
        public void OtherShoutsSwearSomethingElse()
        {
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, Lines, 0.0, 0.0), Is.EqualTo("Human_Vittu"));
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, Lines, 0.0, 1.0), Is.EqualTo("Human_Saatana"));
        }

        [Test]
        public void FallsBackToWhateverPerkeleThereIs()
        {
            var onlyPerkele = new List<KeyValuePair<string, string>> { L("Human_Perkele", null), L("Human_Royh", null) };
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, onlyPerkele, 0.1, 0.5), Is.EqualTo("Human_Perkele"));
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, onlyPerkele, 0.1, 0.5), Is.EqualTo("Human_Perkele"));
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, new List<KeyValuePair<string, string>> { L("Human_Vittu", null) }, 0.1, 0.5), Is.Null);
        }
    }
}
