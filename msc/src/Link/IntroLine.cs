using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // What MSC plays for Kumpo's Helgen intro (IntroCue, protocol/cpp/intro_cues.h): steps of a clip
    // some seconds after the cue. Blackout: Kumpo drinks, burps, nods off and snores while Skyrim's
    // new game fades up from black. Wake: he wakes up hungover, with MSC's own hangover line
    // hangover05 ("Saaatanaa... oy oy ooooy", picked by ear), after Ralof's "You're finally awake" in
    // the cart and when he wakes up in the shack on a save's first handover. Each step names the clips it may use, best first: an exact MSC clip name, else the
    // first name (in ordinal order) that has it as a part, ignoring case (the Pieno-soundbank's
    // Human_ snores and burps, whose names aren't mapped). A step with no clip is skipped. Unity-free.
    public static class IntroLine
    {
        public struct Step
        {
            public float At;  // seconds after the cue
            public string[] Clips;
        }

        public static readonly Step[] Blackout =
        {
            new Step { At = 0f, Clips = new[] { "drinking" } },
            new Step { At = 2.5f, Clips = new[] { "burp" } },
            new Step { At = 4.5f, Clips = new[] { "tosleep", "yawn01" } },
            new Step { At = 6.5f, Clips = new[] { "snore" } },
        };

        public static readonly Step[] Wake =
        {
            new Step { At = 0f, Clips = new[] { "hangover05" } },
        };

        public static Step[] For(uint cue) =>
            cue == Wire.IntroBlackout ? Blackout : cue == Wire.IntroWake ? Wake : new Step[0];

        // The clip name to play for a step out of MSC's clip names, or null.
        public static string Pick(string[] wanted, ICollection<string> names)
        {
            foreach (var w in wanted)
                if (names.Contains(w)) return w;
            foreach (var w in wanted)
            {
                string best = null;
                var part = w.ToLowerInvariant();
                foreach (var name in names)
                    if (name.ToLowerInvariant().Contains(part) && (best == null || string.CompareOrdinal(name, best) < 0))
                        best = name;
                if (best != null) return best;
            }
            return null;
        }
    }
}
