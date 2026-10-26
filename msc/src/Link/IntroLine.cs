using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // What MSC plays for Kumpo's Helgen intro (IntroCue, protocol/cpp/intro_cues.h): steps of a clip
    // some seconds after the cue. Blackout: Kumpo snores, then groans MSC's hangover line hangover05 ("Ei hittoa, toi on kyllä viimeinen kerta, kun mä otan mitä. Ai saatana, mikä
    // olo.", picked by ear, at intro_cues.h's kHangoverAt) while Skyrim's new game fades up from
    // black. Wake: Perkele (swear09, "God of thunder!"; MSC's Human_Perkele sounds distorted) after
    // Ralof's "You're finally awake". Hangover: the hangover line alone, waking up in the shack on a
    // save's first handover. Each step names the clips it may use, best first: an exact MSC clip name, else the
    // first name (in ordinal order) that has it as a part, ignoring case (the Pieno-soundbank's
    // Human_ snores and burps, whose names aren't mapped). A step with no clip is skipped. Unity-free.
    public static class IntroLine
    {
        public struct Step
        {
            public float At;  // seconds after the cue
            public string[] Clips;
            public float Max;  // seconds it plays at most, 0 = the whole clip (MSC's drinking and snore clips are 20 s loops)
        }

        // How long a step plays a clip of that length.
        public static float Seconds(Step step, float clipLength) => step.Max > 0f && step.Max < clipLength ? step.Max : clipLength;

        public const string HangoverLine = "hangover05";
        public const float HangoverAt = 14f;  // intro_cues.h kHangoverAt, which the captions follow

        // One after the other on the black opening: quiet for 4 s, snoring 4-13.5 s (MSC's snore clip
        // is a 24 s loop), the hangover line from 14 s (9.7 s long).
        public static readonly Step[] Blackout =
        {
            new Step { At = 4f, Clips = new[] { "snore" }, Max = 9.5f },
            new Step { At = HangoverAt, Clips = new[] { HangoverLine } },
        };

        public static readonly Step[] Wake =
        {
            new Step { At = 0f, Clips = new[] { "swear09" } },
        };

        public static readonly Step[] Hangover =
        {
            new Step { At = 0f, Clips = new[] { HangoverLine } },
        };

        public static Step[] For(uint cue) =>
            cue == Wire.IntroBlackout ? Blackout : cue == Wire.IntroWake ? Wake : cue == Wire.IntroHangover ? Hangover : new Step[0];

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
