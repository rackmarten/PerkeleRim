using System.Collections.Generic;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class ShoutLineTests
    {
        const uint UnrelentingForce = 0x013E07;
        const uint WhirlwindSprint = 0x02F7BA;
        const uint FireBreath = 0x03F9EA;
        const uint Disarm = 0x070981;

        static KeyValuePair<string, string> L(string clip, string line) => new KeyValuePair<string, string>(clip, line);

        static readonly List<KeyValuePair<string, string>> Lines = new List<KeyValuePair<string, string>>
        {
            L("swear01", "Oh pussy!"),
            L("swear05", "What the hell!?"),
            L("swear09", "God of thunder!"),
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
        public void UnrelentingForceIsTheGodOfThunder()
        {
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, UnrelentingForce, Lines, 0.0), Is.EqualTo("swear09"));
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, UnrelentingForce, Lines, 0.99), Is.EqualTo("swear09"));
        }

        [Test]
        public void CommonShoutsYellTheirWord()
        {
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, WhirlwindSprint, Lines, 0.99), Is.EqualTo("Human_Vittu"));
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, FireBreath, Lines, 0.0), Is.EqualTo("swear05"));
        }

        [Test]
        public void TheWordTable()
        {
            var shouts = new HashSet<uint>();
            foreach (var word in ShoutLine.Words)
            {
                Assert.That(shouts.Add(word.Shout), Is.True, $"{word.Name} twice");
                Assert.That(word.Shout, Is.Not.EqualTo(UnrelentingForce), "Unrelenting Force is the Perkele");
                Assert.That(word.Shout >> 24, Is.EqualTo(0u), $"{word.Name} isn't Skyrim.esm's");
                bool known = System.Array.IndexOf(ShoutLine.Swears, word.Clip) >= 0;
                for (int i = 0; i < ShoutLine.SwearLines.Length; i++)
                {
                    if (ShoutLine.SwearClip(i) != word.Clip) continue;
                    known = true;
                    Assert.That(ShoutLine.IsThunder(ShoutLine.SwearLines[i]), Is.False, $"{word.Name} takes the Perkele");
                }
                Assert.That(known, Is.True, $"{word.Name}: {word.Clip} isn't one of Kumpo's swears");
                Assert.That(word.Says, Is.Not.Empty);
            }
            Assert.That(ShoutLine.WordOf(WhirlwindSprint).Value.Says, Is.EqualTo("Vittu!"));
            Assert.That(ShoutLine.WordOf(Disarm).HasValue, Is.False);
        }

        [Test]
        public void OtherShoutsSwearSomethingElse()
        {
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, Disarm, Lines, 0.0), Is.EqualTo("Human_Vittu"));
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, Disarm, Lines, 1.0), Is.EqualTo("Human_Saatana"));
            // A word whose clip MSC doesn't have: another swear.
            var noFire = new List<KeyValuePair<string, string>> { L("Human_Kyrpa", null), L("swear09", "God of thunder!") };
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, FireBreath, noFire, 0.5), Is.EqualTo("Human_Kyrpa"));
        }

        [Test]
        public void FallsBackToWhateverPerkeleThereIs()
        {
            var onlyPerkele = new List<KeyValuePair<string, string>> { L("Human_Perkele", null), L("Human_Royh", null) };
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, UnrelentingForce, onlyPerkele, 0.5), Is.EqualTo("Human_Perkele"));
            Assert.That(ShoutLine.Pick(Wire.ShoutOther, Disarm, onlyPerkele, 0.5), Is.EqualTo("Human_Perkele"));
            Assert.That(ShoutLine.Pick(Wire.ShoutUnrelentingForce, UnrelentingForce, new List<KeyValuePair<string, string>> { L("Human_Vittu", null) }, 0.5), Is.Null);
        }

        [Test]
        public void ASyllablePerWord()
        {
            Assert.That(ShoutLine.Cut(1, 0.9942f), Is.EqualTo(0.37f));
            Assert.That(ShoutLine.Cut(2, 0.9942f), Is.EqualTo(0.585f));
            Assert.That(ShoutLine.Cut(3, 0.9942f), Is.EqualTo(0f));  // the whole PER-KE-LE!
            Assert.That(ShoutLine.Cut(0, 0.9942f), Is.EqualTo(0f));  // words unknown
            Assert.That(ShoutLine.Cut(1, 1.2f), Is.EqualTo(0f));     // not the clip it was measured on
            Assert.That(ShoutLine.Syllables(1), Is.EqualTo("PER"));
            Assert.That(ShoutLine.Syllables(2), Is.EqualTo("PER-KE"));
            Assert.That(ShoutLine.Syllables(3), Is.EqualTo("PER-KE-LE!"));
        }

        [Test]
        public void ACutFadesOut()
        {
            const int frequency = 1000;  // the fade is 25 samples
            var mono = new float[100];
            for (int i = 0; i < mono.Length; i++) mono[i] = 1f;
            ShoutLine.FadeOut(mono, 1, frequency);
            Assert.That(mono[74], Is.EqualTo(1f));
            Assert.That(mono[75], Is.EqualTo(24f / 25f).Within(1e-6));
            Assert.That(mono[99], Is.EqualTo(0f));
            var stereo = new float[20];
            for (int i = 0; i < stereo.Length; i++) stereo[i] = 1f;
            ShoutLine.FadeOut(stereo, 2, frequency);  // shorter than the fade: all of it
            Assert.That(stereo[0], Is.EqualTo(0.9f).Within(1e-6));
            Assert.That(stereo[1], Is.EqualTo(0.9f).Within(1e-6));
            Assert.That(stereo[18], Is.EqualTo(0f));
            Assert.That(stereo[19], Is.EqualTo(0f));
        }
    }
}
