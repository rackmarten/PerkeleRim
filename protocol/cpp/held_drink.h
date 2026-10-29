// Where Skyrim draws the drink Kumpo holds while MSC shows its drinking (HeldDrink, v50): the
// item's own model in MSC's fist. Header-only and free of Skyrim types, so the protocol tests
// cover it.
//
// MSC sends its bottle as the middle of its base and of its top (the way it points), a direction
// across it and the grip, where the fist closes on it, all in the camera's space. Most Skyrim
// bottles are too wide to hold by the body in one hand, so Kumpo holds them by the neck: a Skyrim
// bottle's model stands on its base along +Z (it's made to be put on a table), and its neck, a
// little under its top, goes into the grip, pointing the way MSC's bottle points, +X along side.
// The body hangs out of the fist beyond it. It keeps its own size, shrunk only when it's taller
// than a real bottle (a jug), so it isn't stretched to MSC's bottle.
// Matrices are m[row][col] and map a local vector to the parent's space (out = m * local), like
// NiMatrix3 in a NiTransform.

#pragma once

#include <algorithm>
#include <cmath>

namespace PerkeleRim::Held
{
    using Matrix = float[3][3];

    struct Vec
    {
        float x = 0.0f, y = 0.0f, z = 0.0f;
    };

    struct Pose
    {
        Matrix rotate{ { 1.0f, 0.0f, 0.0f }, { 0.0f, 1.0f, 0.0f }, { 0.0f, 0.0f, 1.0f } };
        Vec    translate;
        float  scale = 1.0f;
    };

    inline constexpr float kMaxHeight = 21.0f;  // units (30 cm): a model taller than this is shrunk to it
    inline constexpr float kNeckDown = 0.2f;    // the neck, this share of the model's height under its top

    inline Vec   Sub(Vec a, Vec b) { return { a.x - b.x, a.y - b.y, a.z - b.z }; }
    inline Vec   Add(Vec a, Vec b) { return { a.x + b.x, a.y + b.y, a.z + b.z }; }
    inline Vec   Scale(Vec a, float s) { return { a.x * s, a.y * s, a.z * s }; }
    inline float Dot(Vec a, Vec b) { return a.x * b.x + a.y * b.y + a.z * b.z; }
    inline Vec   Cross(Vec a, Vec b) { return { a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x }; }
    inline float Length(Vec a) { return std::sqrt(Dot(a, a)); }

    inline Vec Apply(const Matrix& m, Vec v)
    {
        return { m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z, m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z,
            m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z };
    }

    // The model's scale: its own size, shrunk to kMaxHeight when taller.
    inline float SizeOf(Vec a_min, Vec a_max) { return (std::min)(1.0f, kMaxHeight / (std::max)(a_max.z - a_min.z, 1e-4f)); }

    // Its neck in its own space: the middle of its bounds, kNeckDown of its height under its top.
    inline Vec NeckOf(Vec a_min, Vec a_max)
    {
        return { (a_min.x + a_max.x) * 0.5f, (a_min.y + a_max.y) * 0.5f, a_max.z - kNeckDown * (a_max.z - a_min.z) };
    }

    // The model (bounds a_min..a_max in its own space, standing along +Z) held by its neck at
    // a_grip, pointing from a_bottom to a_top, in the camera's space. False when MSC's bottle or
    // the model has no length, or side runs along it.
    inline bool Place(Vec a_bottom, Vec a_top, Vec a_side, Vec a_grip, Vec a_min, Vec a_max, Pose& a_out)
    {
        const Vec   along = Sub(a_top, a_bottom);
        const float length = Length(along);
        if (length < 1e-4f || a_max.z - a_min.z < 1e-4f) {
            return false;
        }
        const Vec z = Scale(along, 1.0f / length);
        Vec       x = Sub(a_side, Scale(z, Dot(a_side, z)));  // across the bottle only
        const float across = Length(x);
        if (across < 1e-4f) {
            return false;
        }
        x = Scale(x, 1.0f / across);
        const Vec y = Cross(z, x);  // right-handed, like Skyrim: x cross y = z
        const Vec columns[3] = { x, y, z };
        for (int c = 0; c < 3; c++) {
            a_out.rotate[0][c] = columns[c].x;
            a_out.rotate[1][c] = columns[c].y;
            a_out.rotate[2][c] = columns[c].z;
        }
        a_out.scale = SizeOf(a_min, a_max);
        a_out.translate = Sub(a_grip, Apply(a_out.rotate, Scale(NeckOf(a_min, a_max), a_out.scale)));
        return true;
    }

    // a_local (in the camera's space) in the camera's parent's space: the camera turned by
    // a_rotate and at a_translate, scale 1.
    inline Pose ToParent(const Matrix& a_rotate, Vec a_translate, const Pose& a_local)
    {
        Pose out;
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                out.rotate[r][c] = a_rotate[r][0] * a_local.rotate[0][c] + a_rotate[r][1] * a_local.rotate[1][c] + a_rotate[r][2] * a_local.rotate[2][c];
            }
        }
        out.translate = Add(a_translate, Apply(a_rotate, a_local.translate));
        out.scale = a_local.scale;
        return out;
    }
}
