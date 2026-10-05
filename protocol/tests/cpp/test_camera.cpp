#include <doctest/doctest.h>

#include <cmath>

#include "../../cpp/camera.h"

using namespace PerkeleRim::Camera;

namespace
{
    constexpr float kPi = 3.14159265f;

    struct V
    {
        float x, y, z;
    };

    V Apply(const Matrix& m, V v)
    {
        return { m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z, m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z,
            m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z };
    }

    void CheckNear(V a, V b)
    {
        CHECK(a.x == doctest::Approx(b.x).epsilon(1e-5));
        CHECK(a.y == doctest::Approx(b.y).epsilon(1e-5));
        CHECK(a.z == doctest::Approx(b.z).epsilon(1e-5));
    }
}

TEST_CASE("camera basis looks along heading and pitch")
{
    Matrix m;
    Basis(0.0f, 0.0f, 0.0f, m);
    CheckNear(Apply(m, { 0, 1, 0 }), { 0, 1, 0 });  // north

    Basis(kPi / 2, 0.0f, 0.0f, m);
    CheckNear(Apply(m, { 0, 1, 0 }), { 1, 0, 0 });   // heading is clockwise: east
    CheckNear(Apply(m, { 1, 0, 0 }), { 0, -1, 0 });  // right of east is south

    const float h = 0.7f, p = 0.4f;
    Basis(h, p, 0.0f, m);
    CheckNear(Apply(m, { 0, 1, 0 }), { std::sin(h) * std::cos(p), std::cos(h) * std::cos(p), -std::sin(p) });  // pitch down
}

TEST_CASE("positive roll tilts the view right")
{
    Matrix m;
    Basis(0.0f, 0.0f, 0.5f, m);
    CheckNear(Apply(m, { 0, 1, 0 }), { 0, 1, 0 });  // still looking north
    const V up = Apply(m, { 0, 0, 1 });
    CHECK(up.x > 0.0f);  // top of the view leans east, to the right
    CHECK(up.z == doctest::Approx(std::cos(0.5f)));
}

TEST_CASE("retargeting keeps the camera node's own axes")
{
    // Skyrim's node with some fixed local axis swap on top of heading 0.3 / pitch 0.1.
    Matrix skyrim, base;
    const Matrix swap = { { 0, 1, 0 }, { -1, 0, 0 }, { 0, 0, 1 } };
    Basis(0.3f, 0.1f, 0.0f, base);
    Multiply(base, swap, skyrim);

    Matrix out, expected, target;
    Retarget(skyrim, 0.3f, 0.1f, 1.2f, -0.2f, 0.25f, out);
    Basis(1.2f, -0.2f, 0.25f, target);
    Multiply(target, swap, expected);
    for (int r = 0; r < 3; r++) {
        for (int c = 0; c < 3; c++) {
            CHECK(out[r][c] == doctest::Approx(expected[r][c]).epsilon(1e-5));
        }
    }
}

TEST_CASE("MSC's vertical fov becomes Skyrim's 4:3 horizontal fov")
{
    CHECK(SkyrimFov(60.0f) == doctest::Approx(75.18f).epsilon(1e-3));
    CHECK(SkyrimFov(30.0f) < SkyrimFov(60.0f));  // zooming in narrows it
}
