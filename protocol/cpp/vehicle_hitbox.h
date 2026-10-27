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
