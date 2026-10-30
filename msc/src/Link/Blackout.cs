using System;

namespace PerkeleRim
{
    // MSC's own blackouts while Skyrim has handed the player over (Game/BlackoutLink): PassOutGuard
    // keeps Systems/PassOut and Systems/KnockOut from moving the player, so this watches for MSC's
    // pass-out line itself. PlayerDrunk above 3.8 (PassOut's Test alc) for PassOutDelay seconds
    // blacks him out; so does a knockout the guard ended. MSC sends Blackout and goes to sleep
    // (PlayerSleeps, PlayerStop, as PassOut's State 2 does: MSC's Hangover FSM takes its strength
    // from the drunk level then); Skyrim moves him and wakes him with TimePassed kTimeBlackout,
    // which sobers him by MSC's metabolism over the hours. Without an answer in WakeTimeout he
    // comes to where he is. Unity-free, so the tests cover it.
    public class Blackout
    {
        public const float PassOutDrunk = 3.8f;  // Systems/PassOut :: Activate, Test alc
        public const float PassOutDelay = 10f;   // seconds above it (PassOut's Check hunger waits too)
        public const float WakeTimeout = 120f;   // Skyrim never woke him: up again where he lies
        public const float Retry = 60f;          // after a blackout that never came, before the next

        // Drunk Mode takes Metabolism 0.0053 off PlayerDrunkAdjusted every 4 s or so, and an MSC hour
        // lasts 300 s (MAP/SUN/Pivot/SUN :: Clock), so a sleeping drunk loses about 0.4 an hour.
        public const float SoberPerHour = 0.0053f * 300f / 4f;
        // He wakes up below this whatever he drank, so he doesn't pass out again on the spot (MSC's
        // own blackout ends with Test alc again).
        public const float WakeDrunkMax = 2.5f;
        // PlayerDrunk = PlayerDrunkAdjusted / (PlayerWeight / WeightScale) (Drunk Mode).
        public const float WeightScale = 83f;

        float aboveSince = -1f;
        float outSince = -1f;
        float nextTry;

        public bool Out => outSince >= 0f;

        // Each frame. True when he blacks out now (send Blackout with kind). Only while MSC has
        // the player and isn't out already.
        public bool Update(bool owned, float drunk, bool knockedOut, float now, out uint kind)
        {
            kind = 0;
            if (!owned || Out || now < nextTry)
            {
                aboveSince = -1f;
                return false;
            }
            if (knockedOut)
            {
                kind = Protocol.Wire.BlackoutKnockout;
                return GoOut(now);
            }
            if (!(drunk > PassOutDrunk))
            {
                aboveSince = -1f;
                return false;
            }
            if (aboveSince < 0f) aboveSince = now;
            if (now - aboveSince < PassOutDelay) return false;
            kind = Protocol.Wire.BlackoutDrunk;
            return GoOut(now);
        }

        bool GoOut(float now)
        {
            outSince = now;
            aboveSince = -1f;
            return true;
        }

        // Out for WakeTimeout without Skyrim's TimePassed: he comes to where he is (Woke), and
        // the next blackout waits Retry.
        public bool TimedOut(float now) => Out && now - outSince >= WakeTimeout;

        public void Woke(float now, bool moved)
        {
            outSince = -1f;
            aboveSince = -1f;
            nextTry = moved ? now : now + Retry;
        }

        public void Reset()
        {
            outSince = aboveSince = -1f;
            nextTry = 0f;
        }

        // PlayerDrunkAdjusted after sleeping `hours` off, at `weight`.
        public static float Sober(float adjusted, float weight, float hours)
        {
            float after = Math.Max(0f, adjusted - SoberPerHour * Math.Max(0f, hours));
            return Math.Min(after, WakeDrunkMax * Math.Max(weight, 1f) / WeightScale);
        }
    }
}
