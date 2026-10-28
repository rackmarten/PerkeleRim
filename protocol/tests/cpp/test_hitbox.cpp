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

TEST_CASE("a point's depth inside a turned vehicle's box")
{
    const Hitbox::Box box{ { -80.0f, -200.0f, 0.0f }, { 80.0f, 200.0f, 150.0f } };
    const float       at[3] = { 1000.0f, 2000.0f, 50.0f };
    const float       level[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    const float       s = std::sqrt(0.5f);
    const float       quarter[4] = { 0.0f, 0.0f, s, s };  // a quarter turn about Z: its +Y lies along world -X
    const float       center[3] = { 1000.0f, 2000.0f, 125.0f };
    CHECK(Hitbox::Depth(at, level, box, center) == doctest::Approx(75.0f));
    const float ahead[3] = { 1000.0f, 2150.0f, 125.0f };  // 150 along its length: inside level, outside turned
    CHECK(Hitbox::Depth(at, level, box, ahead) == doctest::Approx(50.0f));
    CHECK(Hitbox::Depth(at, quarter, box, ahead) == 0.0f);
    const float aside[3] = { 850.0f, 2000.0f, 125.0f };  // 150 along world -X: along the turned one's length
    CHECK(Hitbox::Depth(at, quarter, box, aside) == doctest::Approx(50.0f));
    const float below[3] = { 1000.0f, 2000.0f, 40.0f };
    CHECK(Hitbox::Depth(at, level, box, below) == 0.0f);
}

TEST_CASE("climbed in counts in the van's cabin only, not beside, under or on top of it")
{
    // The boxes PerkeleRim logs for MSC's models, the van turned a quarter about Z (its +Y along world -X).
    const Hitbox::Box van{ { -65.0f, -155.0f, -28.0f }, { 65.0f, 144.0f, 111.0f } };
    const Hitbox::Box kekmet{ { -69.0f, -134.0f, -49.0f }, { 69.0f, 244.0f, 150.0f } };
    const float       at[3] = { 1000.0f, 2000.0f, 0.0f };
    const float       level[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    const float       s = std::sqrt(0.5f);
    const float       quarter[4] = { 0.0f, 0.0f, s, s };
    const auto        in = [&](std::uint32_t a_vehicle, const float (&a_rotation)[4], const Hitbox::Box& a_box, float a_x, float a_y, float a_z) {
        const float feet[3] = { at[0] + a_x, at[1] + a_y, at[2] + a_z };
        return Hitbox::InCabin(a_vehicle, at, a_rotation, a_box, feet);
    };
    CHECK(in(Protocol::kVehicleHayosiko, level, van, -40.0f, 80.0f, 36.0f));    // in the driver's seat
    CHECK(in(Protocol::kVehicleHayosiko, level, van, 0.0f, -100.0f, 20.0f));    // in the back
    CHECK(in(Protocol::kVehicleHayosiko, quarter, van, -80.0f, -40.0f, 36.0f));  // the seat of the turned van
    CHECK_FALSE(in(Protocol::kVehicleHayosiko, level, van, 0.0f, 0.0f, 110.0f));  // on the roof, a unit under its top
    CHECK_FALSE(in(Protocol::kVehicleHayosiko, level, van, 60.0f, 140.0f, -20.0f));  // on the ground at the front bumper's corner
    CHECK_FALSE(in(Protocol::kVehicleHayosiko, level, van, 60.0f, 140.0f, 30.0f));   // on the bumper's end
    CHECK_FALSE(in(Protocol::kVehicleHayosiko, level, van, 60.0f, -100.0f, -10.0f)); // in a wheel arch, beside the van
    CHECK_FALSE(in(Protocol::kVehicleKekmet, level, kekmet, 60.0f, -100.0f, 20.0f)); // beside the Kekmet's engine
    CHECK_FALSE(in(Protocol::kVehicleKekmet, level, kekmet, 0.0f, 150.0f, 60.0f));   // even in its seat: it has no cabin
    CHECK_FALSE(in(Protocol::kVehicleJonnez, level, van, -40.0f, 80.0f, 36.0f));
}

TEST_CASE("climbed in counts in the Gifu's cab only, not on its tank, its tires or its roof")
{
    // The box of MSC's Gifu model (tires to beacons, mirror to mirror, the tank's end to the wipers);
    // its cab's floor is about 83 above its origin, its roof 182.
    const Hitbox::Box gifu{ { -102.0f, -339.0f, -5.0f }, { 102.0f, 262.0f, 222.0f } };
    const float       at[3] = { 1000.0f, 2000.0f, 0.0f };
    const float       level[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    const auto        in = [&](float a_x, float a_y, float a_z) {
        const float feet[3] = { at[0] + a_x, at[1] + a_y, at[2] + a_z };
        return Hitbox::InCabin(Protocol::kVehicleGifu, at, level, gifu, feet);
    };
    CHECK(in(-49.0f, 190.0f, 85.0f));         // on the driver's side of the cab's floor
    CHECK(in(40.0f, 200.0f, 85.0f));          // the passenger's side
    CHECK_FALSE(in(-49.0f, 190.0f, -5.0f));   // on the ground under the driver's door
    CHECK_FALSE(in(-70.0f, 175.0f, 71.0f));   // on the front tire
    CHECK_FALSE(in(0.0f, 0.0f, 205.0f));      // on the septic tank
    CHECK_FALSE(in(0.0f, 100.0f, 120.0f));    // between the tank and the cab
    CHECK_FALSE(in(0.0f, 200.0f, 183.0f));    // on the cab's roof
    CHECK_FALSE(in(95.0f, 220.0f, 140.0f));   // out at the mirror
}

TEST_CASE("the Gifu's seated driver is lowered under its cab's roof, not the tank's top")
{
    const Hitbox::Box gifu{ { -102.0f, -339.0f, -5.0f }, { 102.0f, 262.0f, 222.0f } };
    const float       at[3] = { 0.0f, 0.0f, 0.0f };
    const float       level[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    const float       seat[3] = { -49.0f, 190.0f, 90.0f };
    CHECK(Hitbox::Roof(Protocol::kVehicleGifu, gifu) == doctest::Approx(180.0f));
    CHECK(Hitbox::Roof(Protocol::kVehicleHayosiko, gifu) == doctest::Approx(222.0f));
    Hitbox::Box cab = gifu;
    cab.hi[2] = Hitbox::Roof(Protocol::kVehicleGifu, gifu);
    CHECK(Hitbox::SeatDrop(at, level, cab, seat, 128.0f) == doctest::Approx(48.0f));  // 90 + 128 - (180 - 10)
}

TEST_CASE("a hit on a seated player lands on the box's surface nearest where it came from")
{
    const Hitbox::Box box{ { -65.0f, -155.0f, -28.0f }, { 65.0f, 144.0f, 111.0f } };
    const float       at[3] = { 1000.0f, 2000.0f, 0.0f };
    const float       level[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    const float       s = std::sqrt(0.5f);
    const float       quarter[4] = { 0.0f, 0.0f, s, s };  // its +X along world +Y
    float             point[3];
    float             normal[3];
    const auto        is = [](const float (&a_v)[3], float a_x, float a_y, float a_z) {
        return a_v[0] == doctest::Approx(a_x) && a_v[1] == doctest::Approx(a_y) && a_v[2] == doctest::Approx(a_z);
    };

    const float swing[3] = { 1100.0f, 2050.0f, 90.0f };  // a bandit's blade beside the driver's door
    Hitbox::Surface(at, level, box, swing, point, normal);
    CHECK(is(point, 1065.0f, 2050.0f, 90.0f));
    CHECK(is(normal, 1.0f, 0.0f, 0.0f));

    const float arrow[3] = { 960.0f, 2080.0f, 150.0f };  // an arrow at the head over the roof
    Hitbox::Surface(at, level, box, arrow, point, normal);
    CHECK(is(point, 960.0f, 2080.0f, 111.0f));
    CHECK(is(normal, 0.0f, 0.0f, 1.0f));

    const float corner[3] = { 1100.0f, 2300.0f, 200.0f };  // past the front's top corner, farthest past the front
    Hitbox::Surface(at, level, box, corner, point, normal);
    CHECK(is(point, 1065.0f, 2144.0f, 111.0f));
    CHECK(is(normal, 0.0f, 1.0f, 0.0f));

    const float inside[3] = { 1040.0f, 2000.0f, 50.0f };  // in the cabin: the nearest face, the right side
    Hitbox::Surface(at, level, box, inside, point, normal);
    CHECK(is(point, 1065.0f, 2000.0f, 50.0f));
    CHECK(is(normal, 1.0f, 0.0f, 0.0f));

    const float side[3] = { 1000.0f, 2100.0f, 50.0f };  // turned, world +Y is the van's +X side
    Hitbox::Surface(at, quarter, box, side, point, normal);
    CHECK(is(point, 1000.0f, 2065.0f, 50.0f));
    CHECK(is(normal, 0.0f, 1.0f, 0.0f));
}

TEST_CASE("a seated player is lowered until its head is under the roof")
{
    const Hitbox::Box van{ { -65.0f, -155.0f, -28.0f }, { 65.0f, 144.0f, 111.0f } };
    const float       at[3] = { 1000.0f, 2000.0f, 0.0f };
    const float       level[4] = { 0.0f, 0.0f, 0.0f, 1.0f };
    const float       seat[3] = { 960.0f, 2080.0f, 36.0f };
    // 36 + 128 = 164 at the top of its head, the roof's underside at 111 - 10.
    CHECK(Hitbox::SeatDrop(at, level, van, seat, 128.0f) == doctest::Approx(63.0f));
    CHECK(Hitbox::SeatDrop(at, level, van, seat, 60.0f) == 0.0f);  // a short one fits already
    const float roof[3] = { 960.0f, 2080.0f, 300.0f };
    CHECK(Hitbox::SeatDrop(at, level, van, roof, 128.0f) == Hitbox::kSeatDropMax);
    const float s = std::sqrt(0.5f);
    const float onSide[4] = { 0.0f, s, 0.0f, s };  // a quarter turn about Y: on its side
    CHECK(Hitbox::SeatDrop(at, onSide, van, seat, 128.0f) == 0.0f);
    CHECK(Hitbox::SeatDrop(at, level, van, seat, NAN) == 0.0f);
}
