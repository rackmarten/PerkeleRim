using System;

namespace PerkeleRim
{
    // Crashes MSC survives (jumps, flips, rocks): how hard the driven vehicle hit something, from its
    // velocity every physics step, and what that costs in Skyrim. MSC itself only reports the crashes
    // it kills on (DeathGuard, PlayerDamage severity 1); everything below that is measured here.
    // Unity-free, so the tests cover it.
    public class CrashMeter
    {
        // A physics step whose velocity change (m/s, gravity taken out) is above this is part of an
        // impact. Hard braking or accelerating changes it ~0.2 m/s a step.
        public const float StepNoise = 0.5f;
        // Impacts from here up are logged, to tune the thresholds below from a test drive.
        public const float LogFloor = 2f;
        // Impact (m/s) below which a hit costs nothing: Skyrim's bumpy roads.
        public const float Harmless = 4f;
        // Impact around which MSC kills (DeathGuard takes over from there).
        public const float Fatal = 12f;
        // Severity of a hit just short of fatal; MSC's own death is 1.
        public const float MaxSeverity = 0.8f;
        // A wreck tumbling down a slope hits many times; one hit per this many seconds counts.
        public const float Cooldown = 1f;
        // A vehicle on its wheels (its up axis' y from here up) taking an impact along its own up
        // axis (this share of it or more) is landing a bounce, not hitting a wall: the springs
        // took it, a tractor at full speed down a hill bounces for 6 to 8 m/s all the time. Such an
        // impact counts for LandingShare of its size.
        public const float LandingUpY = 0.7f;
        public const float LandingAlongUp = 0.75f;
        public const float LandingShare = 0.5f;

        // The size an impact counts for given how much of it was along the vehicle's up axis
        // (0..1, AlongUp) and that axis' y.
        public static float Soften(float impact, float alongUp, float upY)
        {
            return upY >= LandingUpY && alongUp >= LandingAlongUp ? impact * LandingShare : impact;
        }

        // PlayerDamage severity for an impact: nothing up to Harmless, then rising slowly at first
        // (a rough landing stings, a rock at speed hurts) to MaxSeverity at Fatal.
        public static float Severity(float impact)
        {
            if (!(impact > Harmless)) return 0f;
            float t = Math.Min(1f, (impact - Harmless) / (Fatal - Harmless));
            return MaxSeverity * (float)Math.Pow(t, 1.5);
        }

        bool known;
        float lastX, lastY, lastZ;
        bool inImpact;
        float sumX, sumY, sumZ;
        float endX, endY, endZ;  // the impact that ended last
        float lastHit = float.NegativeInfinity;

        // Whether the steps so far are part of an impact that has not ended yet.
        public bool InImpact => inImpact;

        // How much of the impact that ended last was along the unit axis (x, y, z): 0..1.
        public float AlongUp(float x, float y, float z)
        {
            float length = Length(endX, endY, endZ);
            return length > 1e-6f ? Math.Abs(endX * x + endY * y + endZ * z) / length : 0f;
        }

        // Forget the last velocity: the vehicle was moved or held, so the next change isn't a hit.
        public void Reset()
        {
            known = false;
            inImpact = false;
            sumX = sumY = sumZ = 0f;
        }

        // One physics step: the vehicle's velocity (m/s, y up), the step length and gravity (y).
        // Returns the size of an impact (m/s) on the step it ends, else 0. Consecutive noisy steps
        // are one impact; their changes add up as vectors.
        public float Step(float x, float y, float z, float dt, float gravityY)
        {
            if (!known)
            {
                known = true;
                lastX = x; lastY = y; lastZ = z;
                return 0f;
            }
            float dx = x - lastX, dy = y - lastY - gravityY * dt, dz = z - lastZ;
            lastX = x; lastY = y; lastZ = z;
            if (Length(dx, dy, dz) > StepNoise)
            {
                inImpact = true;
                sumX += dx; sumY += dy; sumZ += dz;
                return 0f;
            }
            if (!inImpact) return 0f;
            float impact = Length(sumX, sumY, sumZ);
            endX = sumX; endY = sumY; endZ = sumZ;
            inImpact = false;
            sumX = sumY = sumZ = 0f;
            return impact;
        }

        // The severity of an impact at time now (seconds), 0 inside the cooldown of the last hit that cost something.
        public float Hit(float impact, float now)
        {
            float severity = Severity(impact);
            if (severity <= 0f || now - lastHit < Cooldown) return 0f;
            lastHit = now;
            return severity;
        }

        static float Length(float x, float y, float z) => (float)Math.Sqrt(x * x + y * y + z * z);
    }
}
