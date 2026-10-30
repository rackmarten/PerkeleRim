#include <doctest/doctest.h>

#include "../../cpp/held_drink.h"

using namespace PerkeleRim::Held;

namespace
{
    void CheckNear(Vec a, Vec b)
    {
        CHECK(a.x == doctest::Approx(b.x).epsilon(1e-4));
        CHECK(a.y == doctest::Approx(b.y).epsilon(1e-4));
        CHECK(a.z == doctest::Approx(b.z).epsilon(1e-4));
    }

    // A model point as the placed pose has it.
    Vec Placed(const Pose& a_pose, Vec a_model) { return Add(a_pose.translate, Apply(a_pose.rotate, Scale(a_model, a_pose.scale))); }

    // Skyrim's Flin, roughly: 0.5..20.5 units tall, base not quite at the origin, off center in y.
    constexpr Vec kMin{ -3.0f, -2.0f, 0.5f };
    constexpr Vec kMax{ 3.0f, 4.0f, 20.5f };
    constexpr Vec kNeck{ 0.0f, 1.0f, 16.5f };  // 20 % of its 20 units under its top
}

TEST_CASE("held drink is held by its neck in the fist")
{
    CheckNear(NeckOf(kMin, kMax), kNeck);
    Pose pose;
    // MSC's bottle upright in front of the camera, the fist on it at (5, 30, 2).
    REQUIRE(Place({ 5, 30, -6 }, { 5, 30, 6 }, { 1, 0, 0 }, { 5, 30, 2 }, kMin, kMax, pose));
    CHECK(pose.scale == doctest::Approx(1.0f));  // its own size, not MSC's bottle's 12 units
    const float up = kLiftUp;
    CheckNear(Placed(pose, kNeck), { 5, 30, 2 + up });                // in the fist, a little up
    CheckNear(Placed(pose, { 0, 1, 20.5f }), { 5, 30, 6 + up });     // its top out of the fist, the way MSC's points
    CheckNear(Placed(pose, { 0, 1, 0.5f }), { 5, 30, -14 + up });    // the body hangs below
    CheckNear(Placed(pose, { 3, 1, 16.5f }), { 8, 30, 2 + up });     // +X along side
}

TEST_CASE("held drink keeps its own size unless it is taller than a bottle")
{
    CHECK(SizeOf(kMin, kMax) == doctest::Approx(1.0f));
    CHECK(SizeOf({ -10, -10, 0 }, { 10, 10, 42 }) == doctest::Approx(0.5f));  // the jug of milk, 60 cm, to 30
    Pose pose;
    REQUIRE(Place({ 0, 0, 0 }, { 0, 0, 10 }, { 0, 1, 0 }, { 0, 0, 5 }, { -10, -10, 0 }, { 10, 10, 42 }, pose));
    CHECK(pose.scale == doctest::Approx(0.5f));
    CheckNear(Placed(pose, NeckOf({ -10, -10, 0 }, { 10, 10, 42 })), { 0, 0, 5 + kLiftUp });
}

TEST_CASE("held drink follows a tilted bottle and its turn")
{
    // Tipped toward the mouth: the neck points back at the camera (-Y) and a little up.
    const Vec bottom{ 4, 20, -8 };
    const Vec top{ 4, 4, 4 };
    const Vec grip{ 4, 12, -2 };  // halfway
    Pose      pose;
    REQUIRE(Place(bottom, top, { 0, 0, 1 }, grip, kMin, kMax, pose));  // side not quite across: only its part across counts
    CheckNear(Placed(pose, kNeck), { grip.x, grip.y, grip.z + kLiftUp });  // up is the camera's, whatever the tilt
    CheckNear(Placed(pose, { 0, 1, 20.5f }), { 4, 12 - 0.8f * 4, -2 + 0.6f * 4 + kLiftUp });  // 4 units on toward MSC's top
    // A rotation: lengths kept, right-handed.
    const Vec x = Apply(pose.rotate, { 1, 0, 0 }), y = Apply(pose.rotate, { 0, 1, 0 }), z = Apply(pose.rotate, { 0, 0, 1 });
    CHECK(Length(x) == doctest::Approx(1.0f));
    CHECK(Dot(x, z) == doctest::Approx(0.0f));
    CheckNear(Cross(x, y), z);
    CHECK(Dot(x, { 0, 0.6f, 0.8f }) == doctest::Approx(1.0f));  // side, made square to the bottle
}

TEST_CASE("held drink without a length or a side isn't placed")
{
    Pose pose;
    CHECK_FALSE(Place({ 1, 2, 3 }, { 1, 2, 3 }, { 1, 0, 0 }, { 1, 2, 3 }, kMin, kMax, pose));
    CHECK_FALSE(Place({ 0, 0, 0 }, { 0, 0, 10 }, { 0, 0, 5 }, { 0, 0, 5 }, kMin, kMax, pose));
    CHECK_FALSE(Place({ 0, 0, 0 }, { 0, 0, 10 }, { 1, 0, 0 }, { 0, 0, 5 }, { 0, 0, 2 }, { 1, 1, 2 }, pose));
}

TEST_CASE("held drink goes from the camera's space to its parent's")
{
    Pose local;
    REQUIRE(Place({ 0, 10, -8 }, { 0, 10, 8 }, { 1, 0, 0 }, { 0, 10, 0 }, kMin, kMax, local));
    // A camera at (100, 200, 300) looking east: its forward (+Y) is world +X, its right (+X) world -Y.
    const Matrix camera = { { 0, 1, 0 }, { -1, 0, 0 }, { 0, 0, 1 } };
    const Pose   world = ToParent(camera, { 100, 200, 300 }, local);
    CheckNear(Placed(world, kNeck), { 110, 200, 300 + kLiftUp });  // the neck ten units ahead of the camera
    CheckNear(Placed(world, { 0, 1, 20.5f }), { 110, 200, 304 + kLiftUp });
    CheckNear(Apply(world.rotate, { 1, 0, 0 }), { 0, -1, 0 });
}
