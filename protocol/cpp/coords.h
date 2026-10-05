// Skyrim <-> Unity coordinate conversion. Mirrored in msc/src/Link/Coords.cs; both are checked
// against the same vectors in protocol/tests/coords_vectors.csv.
//
// Skyrim: X east, Y north, Z up (right-handed), game units. Heading is the Z angle in radians,
//         0 = north, increasing clockwise seen from above. Pitch positive looks down.
// Unity:  X east, Y up, Z north (left-handed), meters. Yaw is eulerAngles.y in degrees, 0 = +Z,
//         increasing clockwise seen from above. Pitch (eulerAngles.x) positive looks down.
//
// Swapping Y and Z flips the handedness, so positions map axis by axis and angles keep their sign.
// Rotations as quaternions (x, y, z, w) swap the same axes and negate the vector part: the
// reflection reverses the turning direction, which the change of handedness then cancels out on
// screen. So a vehicle's local +Z forward in Unity becomes local +Y forward in Skyrim.

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

    struct Quat
    {
        float x;
        float y;
        float z;
        float w;
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

    // The mapping is its own inverse.
    inline Quat UnityToSkyrim(const Quat& a_unity) { return { -a_unity.x, -a_unity.z, -a_unity.y, a_unity.w }; }
    inline Quat SkyrimToUnity(const Quat& a_skyrim) { return UnityToSkyrim(a_skyrim); }

    // Rotation matrix of a unit quaternion, m[row][column], for column vectors (v' = m * v), which
    // is how NiMatrix3 is applied.
    inline void ToMatrix(const Quat& q, float (&m)[3][3])
    {
        const float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
        const float xy = q.x * q.y, xz = q.x * q.z, yz = q.y * q.z;
        const float wx = q.w * q.x, wy = q.w * q.y, wz = q.w * q.z;
        m[0][0] = 1.0f - 2.0f * (yy + zz);
        m[0][1] = 2.0f * (xy - wz);
        m[0][2] = 2.0f * (xz + wy);
        m[1][0] = 2.0f * (xy + wz);
        m[1][1] = 1.0f - 2.0f * (xx + zz);
        m[1][2] = 2.0f * (yz - wx);
        m[2][0] = 2.0f * (xz - wy);
        m[2][1] = 2.0f * (yz + wx);
        m[2][2] = 1.0f - 2.0f * (xx + yy);
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
