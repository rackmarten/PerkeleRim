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

    // Skyrim's ale bottle, roughly: 0..20 units tall, 3 wide, base not quite at the origin.
    constexpr Vec kMin{ -3.0f, -2.0f, 0.5f };
    constexpr Vec kMax{ 3.0f, 4.0f, 20.5f };
}

TEST_CASE("held drink stands on MSC's bottle when it is upright")
{
    Pose pose;
    REQUIRE(Place({ 5, 30, -10 }, { 5, 30, 10 }, { 1, 0, 0 }, kMin, kMax, pose));
    CHECK(pose.scale == doctest::Approx(1.0f));
    CheckNear(Placed(pose, { 0, 1, 0.5f }), { 5, 30, -10 });   // the middle of the base on MSC's base
    CheckNear(Placed(pose, { 0, 1, 20.5f }), { 5, 30, 10 });   // the top on MSC's top
    CheckNear(Placed(pose, { 3, 1, 0.5f }), { 8, 30, -10 });   // +X along side
}

TEST_CASE("held drink is scaled to MSC's bottle's length")
{
    Pose pose;
    REQUIRE(Place({ 0, 0, 0 }, { 0, 0, 10 }, { 0, 1, 0 }, kMin, kMax, pose));
    CHECK(pose.scale == doctest::Approx(0.5f));
    CheckNear(Placed(pose, { 0, 1, 20.5f }), { 0, 0, 10 });

    REQUIRE(Place({ 0, 0, 0 }, { 0, 0, 1000 }, { 0, 1, 0 }, kMin, kMax, pose));
    CHECK(pose.scale == doctest::Approx(kMaxScale));  // a broken model stays bottle-sized
}

TEST_CASE("held drink follows a tilted bottle and its turn")
{
    // Tipped toward the mouth: the neck points back at the camera (-Y) and a little up.
    const Vec bottom{ 4, 20, -8 };
    const Vec top{ 4, 4, 4 };  // 20 units from the base
    Pose      pose;
    REQUIRE(Place(bottom, top, { 0, 0, 1 }, kMin, kMax, pose));  // side not quite across: only its part across counts
    CHECK(pose.scale == doctest::Approx(1.0f));
    CheckNear(Placed(pose, { 0, 1, 0.5f }), bottom);
    CheckNear(Placed(pose, { 0, 1, 20.5f }), top);
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
    CHECK_FALSE(Place({ 1, 2, 3 }, { 1, 2, 3 }, { 1, 0, 0 }, kMin, kMax, pose));
    CHECK_FALSE(Place({ 0, 0, 0 }, { 0, 0, 10 }, { 0, 0, 5 }, kMin, kMax, pose));
    CHECK_FALSE(Place({ 0, 0, 0 }, { 0, 0, 10 }, { 1, 0, 0 }, { 0, 0, 2 }, { 1, 1, 2 }, pose));
}

TEST_CASE("held drink goes from the camera's space to its parent's")
{
    Pose local;
    REQUIRE(Place({ 0, 10, 0 }, { 0, 10, 20 }, { 1, 0, 0 }, kMin, kMax, local));
    // A camera at (100, 200, 300) looking east: its forward (+Y) is world +X, its right (+X) world -Y.
    const Matrix camera = { { 0, 1, 0 }, { -1, 0, 0 }, { 0, 0, 1 } };
    const Pose   world = ToParent(camera, { 100, 200, 300 }, local);
    CheckNear(Placed(world, { 0, 1, 0.5f }), { 110, 200, 300 });  // ten units ahead of the camera
    CheckNear(Placed(world, { 0, 1, 20.5f }), { 110, 200, 320 });
    CheckNear(Apply(world.rotate, { 1, 0, 0 }), { 0, -1, 0 });
}
