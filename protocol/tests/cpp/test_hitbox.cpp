#include <doctest/doctest.h>

#include <algorithm>
#include <array>
#include <cmath>
#include <cstring>
#include <vector>

#include "../../cpp/vehicle_hitbox.h"

using namespace PerkeleRim;

namespace
{
    Protocol::ModelVertex Vertex(float a_x, float a_y, float a_z) { return { { a_x, a_y, a_z }, { 0, 0, 1 }, { 0, 0 } }; }

    // Is p inside any of the capsules (or on one's surface)?
    bool Covered(const std::vector<Hitbox::Capsule>& a_capsules, const float (&a_p)[3])
    {
        for (const auto& c : a_capsules) {
            float d[3], ab[3], dot = 0.0f, len = 0.0f;
            for (int k = 0; k < 3; k++) {
                ab[k] = c.b[k] - c.a[k];
                dot += (a_p[k] - c.a[k]) * ab[k];
                len += ab[k] * ab[k];
            }
            const float t = len > 0.0f ? std::clamp(dot / len, 0.0f, 1.0f) : 0.0f;
            float       dist = 0.0f;
            for (int k = 0; k < 3; k++) {
                d[k] = a_p[k] - (c.a[k] + ab[k] * t);
                dist += d[k] * d[k];
            }
            if (std::sqrt(dist) <= c.radius + 0.01f) {
                return true;
            }
        }
        return false;
    }
}

TEST_CASE("a model's bounds take its nodes' rest poses")
{
    Model::Model model;
    // A wheel node 50 forward, turned a quarter about Z, with a child node 10 up; a mesh on the root.
    model.nodes.push_back({ "Wheel", -1, { 0, 50, 0 }, { 0, 0, std::sqrt(0.5f), std::sqrt(0.5f) } });
    model.nodes.push_back({ "Hub", 0, { 0, 0, 10 }, { 0, 0, 0, 1 } });
    const std::vector<Protocol::ModelVertex> body{ Vertex(-20, -60, 0), Vertex(20, 40, 30) };
    const std::vector<Protocol::ModelVertex> hub{ Vertex(5, 0, 0) };  // the quarter turn puts +X along +Y
    model.meshes.push_back({ "body", -1, -1, -1, body.data(), 2, nullptr, 0 });
    model.meshes.push_back({ "hub", -1, -1, 1, hub.data(), 1, nullptr, 0 });

    Hitbox::Box box{};
    REQUIRE(Hitbox::BoundsOf(model, box));
    CHECK(box.lo[0] == doctest::Approx(-20));
    CHECK(box.lo[1] == doctest::Approx(-60));
    CHECK(box.lo[2] == doctest::Approx(0));
    CHECK(box.hi[0] == doctest::Approx(20));
    CHECK(box.hi[1] == doctest::Approx(55));  // 50 + the hub's 5 turned onto Y
    CHECK(box.hi[2] == doctest::Approx(30));

    Model::Model empty;
    CHECK_FALSE(Hitbox::BoundsOf(empty, box));
}

TEST_CASE("the van's box is four capsules along its length with no gap between them")
{
    // About the Hayosiko: 1.7 m wide, 4.3 m long, 2 m tall, its origin between the axles.
    const Hitbox::Box box{ { -60, -150, -40 }, { 60, 150, 100 } };
    const auto        capsules = Hitbox::Capsules(box);
    REQUIRE(capsules.size() == 4);
    const float r = capsules[0].radius;
    CHECK(r == doctest::Approx(140.0f / (2.0f + std::sqrt(2.0f))));
    for (const auto& c : capsules) {
        CHECK(c.radius == doctest::Approx(r));
        CHECK(c.a[1] == doctest::Approx(-150 + r));
        CHECK(c.b[1] == doctest::Approx(150 - r));
        CHECK(c.a[0] == c.b[0]);
        CHECK(c.a[2] == c.b[2]);
    }
    // Every point of the box but its rounded edges and ends: an arrow can't slip between capsules.
    int checked = 0;
    for (float x = -60 + 0.3f * r; x <= 60 - 0.3f * r; x += 2.0f) {
        for (float z = -40 + 0.3f * r; z <= 100 - 0.3f * r; z += 2.0f) {
            for (float y : { -150 + r, 0.0f, 150 - r }) {
                const float p[3] = { x, y, z };
                CHECK_MESSAGE(Covered(capsules, p), x, ",", y, ",", z);
                checked++;
            }
        }
    }
    CHECK(checked > 1000);
    const float top[3] = { 19, 0, 100 }, outside[3] = { 0, 0, 101 }, front[3] = { 19, 150, 59 };  // on a capsule's crest
    CHECK(Covered(capsules, top));
    CHECK(Covered(capsules, front));
    CHECK_FALSE(Covered(capsules, outside));
}

TEST_CASE("a narrow vehicle is one row of capsules, a sideways one lies along X")
{
    // The moped: 0.6 m wide, 1.8 m long, 1.1 m tall.
    const auto moped = Hitbox::Capsules({ { -21, -63, 0 }, { 21, 63, 77 } });
    REQUIRE(moped.size() == 3);
    CHECK(moped[0].radius == doctest::Approx(21));
    for (const auto& c : moped) {
        CHECK(c.a[0] == doctest::Approx(0));
    }
    CHECK(moped[0].a[2] == doctest::Approx(21));
    CHECK(moped[2].a[2] == doctest::Approx(56));

    const auto sideways = Hitbox::Capsules({ { -150, -60, -40 }, { 150, 60, 100 } });
    REQUIRE(sideways.size() == 4);
    const float r = sideways[0].radius;
    CHECK(sideways[0].a[0] == doctest::Approx(-150 + r));
    CHECK(sideways[0].b[0] == doctest::Approx(150 - r));
    CHECK(sideways[0].a[1] == sideways[0].b[1]);
}

TEST_CASE("no capsules for an empty, flat, huge or broken box")
{
    CHECK(Hitbox::Capsules({ { 0, 0, 0 }, { 0, 0, 0 } }).empty());
    CHECK(Hitbox::Capsules({ { 0, 0, 0 }, { 100, 100, 0.5f } }).empty());
    CHECK(Hitbox::Capsules({ { 0, 0, 0 }, { 100, 5000, 100 } }).empty());
    CHECK(Hitbox::Capsules({ { 0, 0, 0 }, { 100, NAN, 100 } }).empty());
    CHECK(Hitbox::Capsules({ { 0, 0, 0 }, { 1000, 1000, 2 } }).empty());  // a sheet
}

TEST_CASE("a ball-sized box is one capsule with its ends apart")
{
    const auto ball = Hitbox::Capsules({ { -10, -10, -10 }, { 10, 10, 10 } });
    REQUIRE(ball.size() == 1);
    CHECK(ball[0].radius == doctest::Approx(10));
    CHECK(ball[0].b[1] - ball[0].a[1] == doctest::Approx(1));
}

TEST_CASE("the hitbox leaves the world at the run-over speed and comes back once slower")
{
    CHECK(Hitbox::Solid(0.0f, false));
    CHECK(Hitbox::Solid(1.9f, true));
    CHECK_FALSE(Hitbox::Solid(Damage::kRunOverSpeedMin, true));
    CHECK_FALSE(Hitbox::Solid(1.9f, false));  // still too fast to come back
    CHECK(Hitbox::Solid(1.4f, false));
}
