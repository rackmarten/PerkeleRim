using System;

namespace PerkeleRim
{
    // MSC's side of what the needs do to Kumpo while Skyrim has handed him over; the rest is
    // protocol/cpp/needs.h. A full bladder slows his walk and run (MotorBoost) from MSC's red line at
    // 100 to UrineSlowMax at 200, its death line. Unity-free.
    public static class Needs
    {
        public const float Mark = 100f;
        public const float Full = 200f;
        public const float UrineSlowMax = 0.3f;

        // MotorBoost's speeds are multiplied by this.
        public static float UrineSpeed(float urine)
        {
            var share = Math.Max(0f, Math.Min(1f, (urine - Mark) / (Full - Mark)));
            return 1f - UrineSlowMax * share;
        }
    }
}
