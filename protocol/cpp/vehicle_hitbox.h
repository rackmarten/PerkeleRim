// A vehicle's hitbox in Skyrim's world (skse/src/vehicle/Hitbox.cpp): the box around MSC's own model
// of it, filled with capsules lying along its longest side (Skyrim builds capsules for us, boxes it
// doesn't). It is solid while the vehicle stands or crawls; from the run-over speed on it leaves
// Skyrim's world, or it would shove people out of the way before MSC's box could run them over
// (msc/src/Link/RunOver.cs). Header-only and game-free; the protocol tests cover it.

#pragma once

#include <algorithm>
#include <cmath>
#include <cstddef>
#include <vector>

#include "coords.h"
#include "damage.h"
#include "model.h"

namespace PerkeleRim::Hitbox
{
    inline constexpr float       kRadius = 42.0f;     // units (0.6 m): capsules at most this thick where the box allows
    inline constexpr std::size_t kCapsuleMax = 12;    // capsules per vehicle at most
    inline constexpr float       kSizeMax = 1400.0f;  // units (20 m): a model bigger than this along any side isn't a vehicle we know

    inline constexpr float kSolidBelow = Damage::kRunOverSpeedMin * 0.75f;  // m/s: solid again once it has slowed under this

    // Whether the hitbox is in Skyrim's world at a_speed (m/s), given whether it was last frame.
    inline bool Solid(float a_speed, bool a_was)
    {
        return a_was ? a_speed < Damage::kRunOverSpeedMin : a_speed < kSolidBelow;
    }

    struct Box
    {
        float lo[3];
        float hi[3];
    };

    struct Capsule
    {
        float a[3];  // the two ends of its core segment
        float b[3];
        float radius;
    };

    // How far inside a_box a world point is (Skyrim units), the box in the frame of a vehicle at
    // a_position turned by a_rotation (x, y, z, w); 0 when outside.
    inline float Depth(const float (&a_position)[3], const float (&a_rotation)[4], const Box& a_box, const float (&a_point)[3])
    {
        float m[3][3];
        Coords::ToMatrix({ a_rotation[0], a_rotation[1], a_rotation[2], a_rotation[3] }, m);
        const float d[3] = { a_point[0] - a_position[0], a_point[1] - a_position[1], a_point[2] - a_position[2] };
        float       depth = 1e9f;
        for (int k = 0; k < 3; k++) {
            const float local = m[0][k] * d[0] + m[1][k] * d[1] + m[2][k] * d[2];  // transpose: world to vehicle
            depth = (std::min)(depth, (std::min)(local - a_box.lo[k], a_box.hi[k] - local));
        }
        return (std::max)(depth, 0.0f);
    }

    // Where a hit coming from a_from lands on the box (the same frame as Depth): the point of its
    // surface closest to a_from, or from inside, of the nearest face; a_normal is that face's outward
    // normal. A hit on the player in a safe seat sparks there (skse/src/safe/MetalHit.cpp).
    inline void Surface(const float (&a_position)[3], const float (&a_rotation)[4], const Box& a_box, const float (&a_from)[3], float (&a_point)[3], float (&a_normal)[3])
    {
        float m[3][3];
        Coords::ToMatrix({ a_rotation[0], a_rotation[1], a_rotation[2], a_rotation[3] }, m);
        const float d[3] = { a_from[0] - a_position[0], a_from[1] - a_position[1], a_from[2] - a_position[2] };
        float       local[3];
        int         axis = 0;      // the face's axis
        float       side = 1.0f;   // and which end of it
        float       out = 0.0f;    // how far outside past that face
        float       in = 1e9f;     // inside: how far from the nearest face
        for (int k = 0; k < 3; k++) {
            local[k] = m[0][k] * d[0] + m[1][k] * d[1] + m[2][k] * d[2];  // transpose: world to vehicle
            const float below = a_box.lo[k] - local[k];
            const float above = local[k] - a_box.hi[k];
            if ((std::max)(below, above) > out) {
                out = (std::max)(below, above);
                axis = k;
                side = above > below ? 1.0f : -1.0f;
            }
            if (out <= 0.0f && (std::min)(-below, -above) < in) {
                in = (std::min)(-below, -above);
                axis = k;
                side = -above < -below ? 1.0f : -1.0f;
            }
        }
        float p[3];
        for (int k = 0; k < 3; k++) {
            p[k] = (std::clamp)(local[k], a_box.lo[k], a_box.hi[k]);
        }
        p[axis] = side > 0.0f ? a_box.hi[axis] : a_box.lo[axis];
        for (int j = 0; j < 3; j++) {
            a_point[j] = a_position[j] + m[j][0] * p[0] + m[j][1] * p[1] + m[j][2] * p[2];
            a_normal[j] = m[j][axis] * side;
        }
    }

    // How far to lower Skyrim's player while it sits in a vehicle, so its a_height tall body (feet at
    // a_feet) stays kSeatHeadroom under the box's top: MSC seats it on the seat, where a standing
    // Skyrim body pokes its head out of the roof for every enemy to hit. 0 when it fits already or
    // the vehicle lies on its side, at most kSeatDropMax. A vertical drop, in Skyrim units.
    inline constexpr float kSeatHeadroom = 10.0f;
    inline constexpr float kSeatDropMax = 120.0f;

    inline float SeatDrop(const float (&a_position)[3], const float (&a_rotation)[4], const Box& a_box, const float (&a_feet)[3], float a_height)
    {
        float m[3][3];
        Coords::ToMatrix({ a_rotation[0], a_rotation[1], a_rotation[2], a_rotation[3] }, m);
        const float up = m[2][2];  // world up along the box's own up
        if (!(up > 0.5f) || !(a_height > 0.0f)) {
            return 0.0f;
        }
        const float d[3] = { a_feet[0] - a_position[0], a_feet[1] - a_position[1], a_feet[2] - a_position[2] };
        const float head = m[0][2] * d[0] + m[1][2] * d[1] + m[2][2] * d[2] + a_height * up;
        const float drop = (head - (a_box.hi[2] - kSeatHeadroom)) / up;
        return std::isfinite(drop) ? (std::clamp)(drop, 0.0f, kSeatDropMax) : 0.0f;
    }

    // Where a player who has climbed into a vehicle without driving it (MSC lowers the camera and
    // lets him move between the seats before Enter) has its feet: the van's cabin, its box above
    // the ground clearance (the wheel arches, the bumpers and the ground beside it stay out), under
    // the roof by more than a step (standing on it stays out) and off the bumpers' ends. Only the
    // van has one; the Kekmet is an open frame whose box reaches out to its rear wheels.
    inline constexpr float kCabinFloor = 40.0f;  // units above the box's bottom (the van's: 28 below its origin, its floor about 60 above that)
    inline constexpr float kCabinRoof = 50.0f;   // units under the box's top
    inline constexpr float kCabinEnds = 15.0f;   // units in from its front and back

    inline bool Cabin(std::uint32_t a_vehicle, const Box& a_box, Box& a_out)
    {
        if (a_vehicle != Protocol::kVehicleHayosiko) {
            return false;
        }
        a_out = a_box;
        a_out.lo[1] += kCabinEnds;
        a_out.hi[1] -= kCabinEnds;
        a_out.lo[2] += kCabinFloor;
        a_out.hi[2] -= kCabinRoof;
        return a_out.lo[1] < a_out.hi[1] && a_out.lo[2] < a_out.hi[2];
    }

    // Whether feet at a_feet (world) are in vehicle a_vehicle's cabin, the vehicle at a_position
    // turned by a_rotation with box a_box.
    inline bool InCabin(std::uint32_t a_vehicle, const float (&a_position)[3], const float (&a_rotation)[4], const Box& a_box, const float (&a_feet)[3])
    {
        Box cabin{};
        return Cabin(a_vehicle, a_box, cabin) && Depth(a_position, a_rotation, cabin, a_feet) > 0.0f;
    }

    // The box around every mesh of a model at its rest pose, in the model root's frame (Skyrim axes
    // and units). False for a model without vertices.
    inline bool BoundsOf(const Model::Model& a_model, Box& a_out)
    {
        struct Pose
        {
            float r[3][3];
            float t[3];
        };
        std::vector<Pose> poses(a_model.nodes.size());
        for (std::size_t i = 0; i < a_model.nodes.size(); i++) {
            const auto& node = a_model.nodes[i];
            Pose        local{};
            Coords::ToMatrix({ node.rotation[0], node.rotation[1], node.rotation[2], node.rotation[3] }, local.r);
            std::copy(node.translation, node.translation + 3, local.t);
            if (node.parent < 0) {
                poses[i] = local;
                continue;
            }
            const auto& parent = poses[static_cast<std::size_t>(node.parent)];
            Pose&       out = poses[i];
            for (int row = 0; row < 3; row++) {
                out.t[row] = parent.t[row];
                for (int k = 0; k < 3; k++) {
                    out.t[row] += parent.r[row][k] * local.t[k];
                }
                for (int col = 0; col < 3; col++) {
                    out.r[row][col] = 0.0f;
                    for (int k = 0; k < 3; k++) {
                        out.r[row][col] += parent.r[row][k] * local.r[k][col];
                    }
                }
            }
        }

        bool any = false;
        for (const auto& mesh : a_model.meshes) {
            const Pose* pose = mesh.node >= 0 ? &poses[static_cast<std::size_t>(mesh.node)] : nullptr;
            for (std::uint32_t i = 0; i < mesh.vertexCount; i++) {
                const auto v = mesh.Vertex(i);
                float      p[3] = { v.position[0], v.position[1], v.position[2] };
                if (pose) {
                    for (int row = 0; row < 3; row++) {
                        p[row] = pose->t[row] + pose->r[row][0] * v.position[0] + pose->r[row][1] * v.position[1] + pose->r[row][2] * v.position[2];
                    }
                }
                for (int k = 0; k < 3; k++) {
                    a_out.lo[k] = any ? (std::min)(a_out.lo[k], p[k]) : p[k];
                    a_out.hi[k] = any ? (std::max)(a_out.hi[k], p[k]) : p[k];
                }
                any = true;
            }
        }
        return any;
    }

    // Capsules filling a_box: each lies along its longest side, end to end with it; across, they
    // stand in a grid with the outer ones touching the box's sides and neighbours at most sqrt(2)
    // radii apart, so the cross-section has no gap between them. Only the edges come out rounded
    // (scalloped by up to 0.3 radii between capsules) and the ends round. Nothing for an empty or
    // absurd box.
    inline std::vector<Capsule> Capsules(const Box& a_box)
    {
        float size[3];
        for (int k = 0; k < 3; k++) {
            size[k] = a_box.hi[k] - a_box.lo[k];
            if (!std::isfinite(size[k]) || !(size[k] > 1.0f) || size[k] > kSizeMax) {
                return {};
            }
        }
        const int   along = size[1] >= size[0] && size[1] >= size[2] ? 1 : (size[0] >= size[2] ? 0 : 2);
        const int   u = along == 0 ? 1 : 0;
        const int   v = along == 2 ? 1 : 2;
        const float narrow = (std::min)(size[u], size[v]);
        const float kGap = std::sqrt(2.0f);
        // The radius n capsules across a side of this size need, and the fewest that do with a_radius.
        auto need = [&](int a_axis, int a_n) { return size[a_axis] / (2.0f + kGap * static_cast<float>(a_n - 1)); };
        auto count = [&](int a_axis, float a_radius) {
            int n = 1;
            while (need(a_axis, n) > a_radius * 1.0001f) {
                n++;
            }
            return n;
        };
        int   nu = 0, nv = 0;
        float radius = 0.0f;
        for (float target = (std::min)(kRadius, narrow * 0.5f);; target = (std::min)(target * 1.25f, narrow * 0.5f)) {
            nu = count(u, target);
            nv = count(v, target);
            radius = (std::max)(need(u, nu), need(v, nv));
            if (static_cast<std::size_t>(nu * nv) <= kCapsuleMax) {
                break;
            }
            if (target >= narrow * 0.5f) {
                return {};  // a sheet, not a vehicle
            }
        }
        auto place = [&](int a_axis, int a_n, int a_i) {
            return a_n == 1 ? (a_box.lo[a_axis] + a_box.hi[a_axis]) * 0.5f
                            : a_box.lo[a_axis] + radius + (size[a_axis] - 2.0f * radius) * static_cast<float>(a_i) / static_cast<float>(a_n - 1);
        };
        float from = a_box.lo[along] + radius, to = a_box.hi[along] - radius;
        if (to - from < 1.0f) {  // a ball: Havok wants the ends apart
            const float mid = (from + to) * 0.5f;
            from = mid - 0.5f;
            to = mid + 0.5f;
        }
        std::vector<Capsule> out;
        for (int i = 0; i < nu; i++) {
            for (int j = 0; j < nv; j++) {
                Capsule c{};
                c.a[u] = c.b[u] = place(u, nu, i);
                c.a[v] = c.b[v] = place(v, nv, j);
                c.a[along] = from;
                c.b[along] = to;
                c.radius = radius;
                out.push_back(c);
            }
        }
        return out;
    }
}
