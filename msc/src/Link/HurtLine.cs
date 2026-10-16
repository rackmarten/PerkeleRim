using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    public enum HurtSound { None, Grunt, Swear }

    // What MSC's player says when Skyrim hurts it (PlayerHurt; Skyrim's own grunts are muted while
    // MSC has the player). In a fight mostly grunts: a hit grunts, now and then swears; a hit the
    // guard took swears more often than not. Out of one, falls and crashes mostly swear, a bad one
    // always. Health first dropping low always swears. After a sound the
    // player stays quiet for Cooldown, so a flurry of hits doesn't spam; the low health swear isn't
    // dropped for that but waits (up to LowWait) and comes out once the cooldown is over (Due).
    // Unity-free; times in seconds.
    public class HurtLine
    {
        public const double Cooldown = 1.5;
        public const double LowWait = 3.0;
        // A fall or crash costing at least this share of max health always swears.
        public const float BadDamage = 0.2f;
        // How often each swears instead of grunting.
        public const double HitSwears = 0.2;
        public const double BlockedSwears = 0.6;
        public const double AccidentSwears = 0.6;  // falls and crashes

        double quietUntil = double.NegativeInfinity;
        double lowUntil = double.NegativeInfinity;  // a low health swear waits until then
        bool lowPending;

        // random in [0, 1): picks grunt or swear.
        public HurtSound Decide(uint cause, float damage, double now, double random)
        {
            if (cause == Wire.HurtLow)
            {
                if (now >= quietUntil) return Said(HurtSound.Swear, now);
                lowPending = true;
                lowUntil = now + LowWait;
                return HurtSound.None;
            }
            if (now < quietUntil) return HurtSound.None;
            double swears = HitSwears;
            if (cause == Wire.HurtBlocked) swears = BlockedSwears;
            else if (cause == Wire.HurtFall || cause == Wire.HurtCrash) swears = damage >= BadDamage ? 1.0 : AccidentSwears;
            return Said(random < swears ? HurtSound.Swear : HurtSound.Grunt, now);
        }

        // Every frame: the low health swear that waited for the cooldown, once it's over.
        public HurtSound Due(double now)
        {
            if (!lowPending || now < quietUntil) return HurtSound.None;
            lowPending = false;
            return now <= lowUntil ? Said(HurtSound.Swear, now) : HurtSound.None;
        }

        HurtSound Said(HurtSound sound, double now)
        {
            quietUntil = now + Cooldown;
            lowPending = lowPending && sound != HurtSound.Swear;
            return sound;
        }

        // MSC's pain grunts: the player's ouch clips (MasterAudio group PlayerMisc, ouch1..ouch10).
        public static bool IsGrunt(string clip) => clip != null && clip.StartsWith("ouch");

        // A swear for being hurt, from the clips PlayerVoice knows (clip name and subtitle line):
        // the player's swear01..swear16 and the Human_ swears, not the "God of thunder!" Perkele,
        // which stays the shout's. random in [0, 1). Null when there are none.
        public static string PickSwear(IList<KeyValuePair<string, string>> lines, double random)
        {
            var swears = new List<string>();
            foreach (var line in lines)
            {
                if (line.Key == null || ShoutLine.IsThunder(line.Value) || ShoutLine.IsPerkele(line.Key, line.Value)) continue;
                bool own = !string.IsNullOrEmpty(line.Value) && line.Key.StartsWith("swear");
                if ((own || System.Array.IndexOf(ShoutLine.Swears, line.Key) >= 0) && !swears.Contains(line.Key)) swears.Add(line.Key);
            }
            return One(swears, random);
        }

        public static string One(IList<string> names, double random)
        {
            if (names.Count == 0) return null;
            int i = (int)(random * names.Count);
            return names[i < 0 ? 0 : i >= names.Count ? names.Count - 1 : i];
        }
    }
}
