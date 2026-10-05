using System;

namespace PerkeleRim
{
    // Skyrim <-> Unity coordinate conversion, mirror of protocol/cpp/coords.h (conventions are
    // explained there). Both are checked against protocol/tests/coords_vectors.csv.
    // Unity-free so it builds and tests without the game; convert Vec3 to Vector3 at the call site.
    public struct Vec3
    {
        public float x, y, z;

        public Vec3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }
    }

    public static class Coords
    {
        public const double MetersPerUnit = 0.0142875;
        public const double UnitsPerMeter = 1.0 / MetersPerUnit;

        public static Vec3 SkyrimToUnity(Vec3 skyrim) => new Vec3(
            (float)(skyrim.x * MetersPerUnit),
            (float)(skyrim.z * MetersPerUnit),
            (float)(skyrim.y * MetersPerUnit));

        public static Vec3 UnityToSkyrim(Vec3 unity) => new Vec3(
            (float)(unity.x * UnitsPerMeter),
            (float)(unity.z * UnitsPerMeter),
            (float)(unity.y * UnitsPerMeter));

        // Radians (any range) -> degrees in [0, 360).
        public static float SkyrimAngleToUnity(float radians)
        {
            double deg = (radians * (180.0 / Math.PI)) % 360.0;
            if (deg < 0.0) deg += 360.0;
            float result = (float)deg;
            return result >= 360f ? 0f : result;  // a tiny negative angle rounds up to 360
        }

        // Degrees (any range) -> radians in [0, 2*pi).
        public static float UnityAngleToSkyrim(float degrees)
        {
            double deg = degrees % 360.0;
            if (deg < 0.0) deg += 360.0;
            float result = (float)(deg * (Math.PI / 180.0));
            return result >= (float)(2.0 * Math.PI) ? 0f : result;
        }
    }
}
