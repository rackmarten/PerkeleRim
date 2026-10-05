// Skyrim <-> Unity coordinate conversion. Mirrored in msc/src/Link/Coords.cs; both are checked
// against the same vectors in protocol/tests/coords_vectors.csv.
//
// Skyrim: X east, Y north, Z up (right-handed), game units. Heading is the Z angle in radians,
//         0 = north, increasing clockwise seen from above. Pitch positive looks down.
// Unity:  X east, Y up, Z north (left-handed), meters. Yaw is eulerAngles.y in degrees, 0 = +Z,
//         increasing clockwise seen from above. Pitch (eulerAngles.x) positive looks down.
//
// Swapping Y and Z flips the handedness, so positions map axis by axis and angles keep their sign.

#pragma once

#include <cmath>

namespace PerkeleRim::Coords
{
    // Havok world scale Skyrim uses: 1 unit = 0.0142875 m (about 70 units per meter).
    inline constexpr double kMetersPerUnit = 0.0142875;
    inline constexpr double kUnitsPerMeter = 1.0 / kMetersPerUnit;

    struct Vec3
    {
        float x;
        float y;
        float z;
    };

    inline Vec3 SkyrimToUnity(const Vec3& a_skyrim)
    {
        return {
            static_cast<float>(a_skyrim.x * kMetersPerUnit),
            static_cast<float>(a_skyrim.z * kMetersPerUnit),
            static_cast<float>(a_skyrim.y * kMetersPerUnit)
        };
    }

    inline Vec3 UnityToSkyrim(const Vec3& a_unity)
    {
        return {
            static_cast<float>(a_unity.x * kUnitsPerMeter),
            static_cast<float>(a_unity.z * kUnitsPerMeter),
            static_cast<float>(a_unity.y * kUnitsPerMeter)
        };
    }

    // Radians (any range) -> degrees in [0, 360).
    inline float SkyrimAngleToUnity(float a_radians)
    {
        double deg = std::fmod(a_radians * (180.0 / 3.14159265358979323846), 360.0);
        if (deg < 0.0) {
            deg += 360.0;
        }
        const float result = static_cast<float>(deg);
        return result >= 360.0f ? 0.0f : result;  // a tiny negative angle rounds up to 360
    }

    // Degrees (any range) -> radians in [0, 2*pi).
    inline float UnityAngleToSkyrim(float a_degrees)
    {
        double deg = std::fmod(static_cast<double>(a_degrees), 360.0);
        if (deg < 0.0) {
            deg += 360.0;
        }
        const float result = static_cast<float>(deg * (3.14159265358979323846 / 180.0));
        return result >= static_cast<float>(2.0 * 3.14159265358979323846) ? 0.0f : result;
    }
}
