using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // What MSC yells for a shout the player made in Skyrim (PlayerShout). MSC's player swears with
    // swear01..swear16, subtitled by a 16-line table in MSC's level data (SwearLines, in that order).
    // Unrelenting Force is "Perkele!": mostly the clip subtitled "God of thunder!" (what perkele
    // means, swear09), now and then another Perkele (MSC's Human_Perkele, which has no line). Any
    // other shout is one of the other swears. Unity-free.
    public static class ShoutLine
    {
        public const string Perkele = "Human_Perkele";
        public const string Prefix = "Human_";
        // How often Unrelenting Force picks a "God of thunder" clip when there are other Perkeles too.
        public const double ThunderShare = 0.75;
        // MSC's Pieno-soundbank of Human_ clips also snores, burps, sneezes and farts, hence the list.
        public static readonly string[] Swears = { "Human_Saatana", "Human_Vittu", "Human_Kyrpa" };
        // The English subtitles of swear01..swear16 (MSC's level2, lines 1 to 16).
        public static readonly string[] SwearLines =
        {
            "Oh pussy!", "Oh satan!", "One spring of pussies!", "Satan...", "What the hell!?", "God help me.",
            "Oh pussy again...", "Satan?", "God of thunder!", "It is the spring of pussies for sure!", "Satan again.",
            "Oh hell, for real!", "Not the satan.", "God help me already!", "Pussy.", "The pussy of all pussies!",
        };

        public static string SwearClip(int index) => "swear" + (index + 1).ToString("00");

        public static bool IsThunder(string line) => Has(line, "thunder");

        public static bool IsPerkele(string clip, string line) => clip == Perkele || Has(line, "perkele");

        static bool Has(string text, string word) => text != null && text.ToLowerInvariant().Contains(word);

        // The clip name to play. lines: MSC's clip names with their subtitle line (null or empty when
        // none); pick and variant: random numbers in [0, 1). Null when there's nothing to yell.
        public static string Pick(uint kind, IList<KeyValuePair<string, string>> lines, double pick, double variant)
        {
            var thunder = new List<string>();
            var perkele = new List<string>();
            var swears = new List<string>();
            foreach (var line in lines)
            {
                if (line.Key == null) continue;
                if (IsThunder(line.Value)) Add(thunder, line.Key);
                else if (IsPerkele(line.Key, line.Value)) Add(perkele, line.Key);
                else if (System.Array.IndexOf(Swears, line.Key) >= 0) Add(swears, line.Key);
            }
            if (kind != Wire.ShoutUnrelentingForce && swears.Count > 0) return One(swears, variant);
            if (thunder.Count > 0 && (perkele.Count == 0 || pick < ThunderShare)) return One(thunder, variant);
            return perkele.Count > 0 ? One(perkele, variant) : null;
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
