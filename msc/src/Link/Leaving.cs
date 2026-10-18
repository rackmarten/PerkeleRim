namespace PerkeleRim
{
    // Getting off a vehicle. MSC lets the player out only when every velocity axis of the vehicle is
    // under its DriveTrigger FSM's MinimumRequiredVelocity (0.15 m/s). A vehicle lying down or sliding
    // never gets that slow quickly on Skyrim's slopes, so the limit is raised: any speed when it is
    // tipped, a walking pace otherwise. Unity-free, so the tests cover it.
    public static class Leaving
    {
        // The up axis' y (1 = upright) under which the moped counts as tipped: 45 degrees. The van and
        // the tractor are heavier and carry a steep road well, so they count as tipped past
        // Upright.MaxTiltCos (60 degrees, the same as being rolled for the push key).
        public const float TippedUpY = 0.7f;
        // Per axis (m/s) up to which Kumpo may step off a vehicle that stands, about 5 km/h.
        public const float WalkingSpeed = 1.5f;
        // MinimumRequiredVelocity of a tipped vehicle: anything.
        public const float Anything = 1000f;

        public static bool Tipped(float upY) => Tipped(upY, TippedUpY);
        public static bool Tipped(float upY, float tippedUpY) => upY < tippedUpY;

        // The limit to put in MinimumRequiredVelocity for a vehicle with this up axis y.
        public static float Limit(float upY) => Limit(upY, TippedUpY);
        public static float Limit(float upY, float tippedUpY) => Tipped(upY, tippedUpY) ? Anything : WalkingSpeed;
    }
}
