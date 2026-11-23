using System;

namespace PerkeleRim
{
    // Whirlwind Sprint for MSC's player (PlayerShout). Skyrim's own sprint moves Skyrim's player, which
    // PerkeleRim puts back where MSC's is every frame, so MSC dashes its player forward instead: about
    // the ~50, ~75 and ~100 ft UESP gives for one, two and three words, at Speed. Unity-free.
    public class ShoutDash
    {
        public const uint WhirlwindSprint = 0x02F7BA;  // Skyrim.esm's form id
        public const float Speed = 25f;                 // m/s
        static readonly float[] Distances = { 15f, 23f, 30f };  // m, by words shouted

        float left;  // m still to go

        public bool Active => left > 0f;

        public static float Distance(uint words) => words == 0 ? 0f : Distances[Math.Min(words, 3u) - 1];

        // True when the shout dashes.
        public bool Start(uint shout, uint words)
        {
            if (shout != WhirlwindSprint || words == 0) return false;
            left = Distance(words);
            return true;
        }

        public void Stop() => left = 0f;

        // Metres to move over a_delta seconds.
        public float Step(float a_delta)
        {
            float d = Math.Min(left, Speed * Math.Max(a_delta, 0f));
            left -= d;
            return d;
        }
    }
}
