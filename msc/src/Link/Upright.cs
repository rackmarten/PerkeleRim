using System;

namespace PerkeleRim
{
    // Putting a rolled vehicle back on its wheels (MSC's push key J next to it; MSC's own push does
    // nothing to a vehicle on its side or roof): when it counts as rolled and still enough, and which
    // way it faces afterwards. Unity-free, so the tests cover it.
    public static class Upright
    {
        // Its up axis more than 60 degrees from world up (the y of its up vector below this): on its
        // side or roof. A van on a steep road is well within this.
        public const float MaxTiltCos = 0.5f;
        public const float MaxSpeed = 1.5f;   // meters per second
        public const float MaxSpin = 1f;      // radians per second
        public const float Reach = 4f;        // meters from the vehicle's middle to the player

        public static bool Rolled(float upY) => upY < MaxTiltCos;

        // Whether J next to the vehicle rights it: rolled, nearly at rest, the player close by.
        public static bool CanRight(float upY, float speed, float spin, float distance) =>
            Rolled(upY) && speed < MaxSpeed && spin < MaxSpin && distance <= Reach;

        // The yaw (Unity degrees: 0 = +Z, 90 = +X) it faces once righted: its forward flattened onto
        // the ground. Standing on its nose the forward is nearly vertical; then it faces where its
        // roof points (tipped forward over its nose, the roof points the way it was going).
        public static float Heading(float forwardX, float forwardZ, float upX, float upZ)
        {
            if (forwardX * forwardX + forwardZ * forwardZ > 0.01f)
                return (float)(Math.Atan2(forwardX, forwardZ) * 180.0 / Math.PI);
            return (float)(Math.Atan2(upX, upZ) * 180.0 / Math.PI);
        }
    }
}
