using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // What MSC yells for a shout the player made in Skyrim (PlayerShout). MSC's player swears with
    // swear01..swear16, subtitled by a 16-line table in MSC's level data (SwearLines, in that order).
    // Unrelenting Force is Kumpo's thu'um: the clip subtitled "God of thunder!" (what perkele means,
    // swear09), a syllable per word of power shouted: PER, PER-KE, PER-KE-LE! (Cut). Other common
    // shouts each have a word of their own (Words), any other shout one of the other swears.
    // Unity-free.
    public static class ShoutLine
    {
        public const string Perkele = "Human_Perkele";
        public const string Prefix = "Human_";
        // MSC's Pieno-soundbank of Human_ clips also snores, burps, sneezes and farts, hence the list.
        public static readonly string[] Swears = { "Human_Saatana", "Human_Vittu", "Human_Kyrpa" };
        // The English subtitles of swear01..swear16 (MSC's level2, lines 1 to 16).
        public static readonly string[] SwearLines =
        {
            "Oh pussy!", "Oh satan!", "One spring of pussies!", "Satan...", "What the hell!?", "God help me.",
            "Oh pussy again...", "Satan?", "God of thunder!", "It is the spring of pussies for sure!", "Satan again.",
            "Oh hell, for real!", "Not the satan.", "God help me already!", "Pussy.", "The pussy of all pussies!",
        };

        public struct Word
        {
            public uint Shout;  // Skyrim.esm's form id
            public string Name;
            public string Clip;
            public string Says;
        }

        // The common shouts' words: one clip each, whatever the word count.
        public static readonly Word[] Words =
        {
            new Word { Shout = 0x02F7BA, Name = "Whirlwind Sprint", Clip = "Human_Vittu", Says = "Vittu!" },
            new Word { Shout = 0x03F9EA, Name = "Fire Breath", Clip = "swear05", Says = "Mitä helvettiä!?" },
            new Word { Shout = 0x05D16B, Name = "Frost Breath", Clip = "swear01", Says = "Voi vittu!" },
            new Word { Shout = 0x032920, Name = "Become Ethereal", Clip = "swear06", Says = "Jumalauta." },
            new Word { Shout = 0x044250, Name = "Dragonrend", Clip = "Human_Saatana", Says = "Saatana!" },
            new Word { Shout = 0x048AC9, Name = "Slow Time", Clip = "swear04", Says = "Saatana..." },
            new Word { Shout = 0x07097C, Name = "Marked for Death", Clip = "Human_Kyrpa", Says = "Kyrpä!" },
            new Word { Shout = 0x03CD34, Name = "Clear Skies", Clip = "swear02", Says = "Voi saatana!" },
            new Word { Shout = 0x07097D, Name = "Storm Call", Clip = "swear14", Says = "Jumalauta jo!" },
        };

        // Where the Perkele clip (swear09) is cut for one and two words, in seconds: after PER (in
        // the silence of the K's closure) and after KE (where the L starts). Measured once on MSC's
        // clip, 0.994 s long; a clip of another length isn't cut (MSC changed it: the whole word).
        public static readonly float[] PerkeleCuts = { 0.37f, 0.585f };
        public const float PerkeleLength = 0.994f;
        public const float LengthSlack = 0.02f;
        // A cut's last this many seconds fade out, so it doesn't click.
        public const float FadeSeconds = 0.025f;

        public static string SwearClip(int index) => "swear" + (index + 1).ToString("00");

        public static bool IsThunder(string line) => Has(line, "thunder");

        public static bool IsPerkele(string clip, string line) => clip == Perkele || Has(line, "perkele");

        static bool Has(string text, string word) => text != null && text.ToLowerInvariant().Contains(word);

        // The word a shout (its form id) yells, null when it has none of its own.
        public static Word? WordOf(uint shout)
        {
            foreach (var word in Words)
                if (word.Shout == shout) return word;
            return null;
        }

        // The clip name to play. kind and shout: PlayerShout's; lines: MSC's clip names with their
        // subtitle line (null or empty when none); variant: a random number in [0, 1). Null when
        // there's nothing to yell.
        public static string Pick(uint kind, uint shout, IList<KeyValuePair<string, string>> lines, double variant)
        {
            var thunder = new List<string>();
            var perkele = new List<string>();
            var swears = new List<string>();
            var names = new List<string>();
            foreach (var line in lines)
            {
                if (line.Key == null) continue;
                Add(names, line.Key);
                if (IsThunder(line.Value)) Add(thunder, line.Key);
                else if (IsPerkele(line.Key, line.Value)) Add(perkele, line.Key);
                else if (System.Array.IndexOf(Swears, line.Key) >= 0) Add(swears, line.Key);
            }
            if (kind != Wire.ShoutUnrelentingForce)
            {
                var word = WordOf(shout);
                if (word.HasValue && names.Contains(word.Value.Clip)) return word.Value.Clip;
                if (swears.Count > 0) return One(swears, variant);
            }
            if (thunder.Count > 0) return thunder[0];
            return perkele.Count > 0 ? One(perkele, variant) : null;
        }

        // How much of the Perkele clip (length seconds long) to play for a shout of words words:
        // the cut after that many syllables, or 0 for all of it (three words, an unknown count, or a
        // clip of another length).
        public static float Cut(uint words, float length)
        {
            if (System.Math.Abs(length - PerkeleLength) > LengthSlack) return 0f;
            return words >= 1 && words <= PerkeleCuts.Length ? PerkeleCuts[words - 1] : 0f;
        }

        // The syllables a shout of words words yells, for the log.
        public static string Syllables(uint words) => words == 1 ? "PER" : words == 2 ? "PER-KE" : "PER-KE-LE!";

        // Fades the last FadeSeconds of samples (frequency samples a second, channels interleaved)
        // out to silence, in place: the end of a cut.
        public static void FadeOut(float[] samples, int channels, int frequency)
        {
            int frames = samples.Length / channels;
            int fade = System.Math.Min(frames, (int)(FadeSeconds * frequency));
            for (int i = 0; i < fade; i++)
            {
                float gain = (float)(fade - 1 - i) / fade;
                int frame = frames - fade + i;
                for (int c = 0; c < channels; c++) samples[frame * channels + c] *= gain;
            }
        }

        static void Add(List<string> list, string name)
        {
            if (!list.Contains(name)) list.Add(name);
        }

        static string One(List<string> list, double random)
        {
            int i = (int)(random * list.Count);
            return list[i < 0 ? 0 : i >= list.Count ? list.Count - 1 : i];
        }
    }
}
