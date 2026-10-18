using System;

namespace PerkeleRim
{
    // Small bumps on Skyrim's rough roads against real crashes, for the ridden moped. MSC treats any
    // touch of the body as a crash: its SoundController plays the crash sound for any collision of a
    // non-wheel collider, and the engine stalls when its RPM falls under 50, which a hop and its
    // landing do. A bump is an impact under Stalls (m/s, CrashMeter's measure); those play no sound
    // and never stall the engine, harder ones do both. Unity-free, so the tests cover it.
    public static class Bumps
    {
        // Relative speed (m/s) under which a collision plays no sound (MSC's volume: speed * 0.1).
        public const float QuietBelow = 3f;
        // An impact (m/s) from here up is a crash that stalls the engine. Landing a jump on the
        // wheels is 5 to 8 and hurts a little; MSC's death is at 12 (CrashMeter.Fatal).
        public const float Stalls = 9f;
        // Seconds after the last bump or impact in which the engine can't stall.
        public const float Hold = 1.5f;

        // Sideways speed (m/s) from which the moped counts as sliding: its engine can't stall then.
        public const float SlideSpeed = 1f;

        public static bool Sliding(float sidewaysSpeed) => Math.Abs(sidewaysSpeed) >= SlideSpeed;

        public static bool QuietCollision(float relativeSpeed) => relativeSpeed < QuietBelow;

        public static bool Crash(float impact) => impact >= Stalls;
    }

    // Decides when the moped's engine may stall: not during an impact or for Hold seconds after one
    // that wasn't a crash. A crash lifts the guard and asks for the stall itself, since the guard
    // may have held the engine up through it.
    public class StallGuard
    {
        float until = float.NegativeInfinity;

        // One physics step: inImpact is CrashMeter.InImpact, impact the size of an impact that ended
        // on this step (else 0), now in seconds. Returns true when the engine is to stall now (a
        // crash ended).
        public bool Step(bool inImpact, float impact, float now)
        {
            if (impact > 0f && Bumps.Crash(impact))
            {
                until = float.NegativeInfinity;
                return true;
            }
            if (inImpact || impact > 0f) until = now + Bumps.Hold;
            return false;
        }

        // Whether the engine is kept from stalling at time now.
        public bool Protects(float now) => now < until;

        public void Reset() => until = float.NegativeInfinity;
    }
}
