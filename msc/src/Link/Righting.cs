using System;

namespace PerkeleRim
{
    // How much a ridden moped balances itself like a rider (AirAssist.Balance): only near the lean it
    // should have, with its wheels on the ground. Past Fall degrees off that lean it is falling and
    // left alone until it is nearly upright again, or it slid along the ground trying to stand up and
    // wiggled against walls. Unity-free, so the tests cover it.
    public class Righting
    {
        // Degrees off the wanted lean up to which the help is full, and past which it is gone.
        public const float Full = 12f;
        public const float Fall = 30f;
        // Degrees off the wanted lean under which a fallen moped counts as up again.
        public const float GetUp = 10f;
        // Wheels that must touch the ground for any help.
        public const int MinWheels = 2;

        // The most it leans into a turn (rad, 23 degrees), and the speeds (m/s) between which the lean
        // fades in: a crawl leans none, since a lean it can't carry slid its tires out from under it
        // in a sharp slow turn (the engine stalled in the slide).
        public const float MaxLean = 0.4f;
        public const float LeanFrom = 2f;
        public const float LeanFull = 6f;

        bool fallen;

        public bool Fallen => fallen;

        // The share (0..1) of the balancing torque for a moped error degrees off its wanted lean with
        // wheelsDown wheels on the ground.
        public float Update(float error, int wheelsDown)
        {
            if (!(error >= 0f)) error = 180f;  // NaN: no help
            if (error > Fall) fallen = true;
            else if (fallen && error < GetUp && wheelsDown >= MinWheels) fallen = false;
            if (fallen || wheelsDown < MinWheels) return 0f;
            return Gain(error);
        }

        // Full up to Full degrees, easing to nothing at Fall.
        public static float Gain(float error)
        {
            if (error <= Full) return 1f;
            if (error >= Fall) return 0f;
            float t = (error - Full) / (Fall - Full);
            return 1f - t * t * (3f - 2f * t);
        }

        // The lean (rad, positive toward the turn) it balances at: atan(speed * yaw rate / g) like a
        // rider, held to MaxLean and faded in with the speed.
        public static float WantedLean(float speed, float yawRate)
        {
            float lean = (float)Math.Atan(speed * yawRate / 9.81f);
            lean = Math.Max(-MaxLean, Math.Min(MaxLean, lean));
            float t = Math.Max(0f, Math.Min(1f, (Math.Abs(speed) - LeanFrom) / (LeanFull - LeanFrom)));
            return lean * t * t * (3f - 2f * t);
        }

        // Whether the air assist levels a moped whose wheels are all off the ground: not one lying on
        // its side or roof, or barely moving (up axis y at most 60 degrees off, 1.5 m/s).
        public const float AirMinUpY = 0.5f;
        public const float AirMinSpeed = 1.5f;

        public static bool AirHelps(float upY, float speed) => upY >= AirMinUpY && speed >= AirMinSpeed;

        public void Reset() => fallen = false;
    }
}
