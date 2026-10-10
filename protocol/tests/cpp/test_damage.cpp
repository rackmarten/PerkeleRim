#include <doctest/doctest.h>

#include "../../cpp/damage.h"

using namespace PerkeleRim;

TEST_CASE("an MSC death costs half of Skyrim's max health")
{
    CHECK(Damage::Crash(200.0f, 1.0f) == doctest::Approx(100.0f));
    CHECK(Damage::Crash(200.0f, 0.5f) == doctest::Approx(50.0f));
    CHECK(Damage::Crash(200.0f, -1.0f) == 0.0f);
}

TEST_CASE("fall damage follows Skyrim's formula")
{
    CHECK(Damage::Fall(600.0f) == 0.0f);  // up to fJumpFallHeightMin is free
    CHECK(Damage::Fall(300.0f) == 0.0f);
    CHECK(Damage::Fall(1000.0f) == doctest::Approx(210.6f).epsilon(0.01));  // (400 * 0.1) ^ 1.45
    CHECK(Damage::Fall(1000.0f, { 200.0f, 0.1f, 1.0f }) == doctest::Approx(80.0f));
}

TEST_CASE("a fall is measured from the highest point while airborne to the landing")
{
    Damage::FallTracker fall;
    CHECK(fall.Update(1000.0f, true) == 0.0f);
    CHECK(fall.Update(1040.0f, false) == 0.0f);  // jumped up
    CHECK(fall.Update(900.0f, false) == 0.0f);
    CHECK(fall.Update(100.0f, true) == doctest::Approx(940.0f));
    CHECK(fall.Update(100.0f, true) == 0.0f);  // standing

    // Walking off a ledge: the fall starts where the ground was left.
    CHECK(fall.Update(80.0f, false) == 0.0f);
    CHECK(fall.Update(-500.0f, true) == doctest::Approx(600.0f));

    // After a reset the first pose only starts tracking.
    fall.Reset();
    CHECK(fall.Update(5000.0f, false) == 0.0f);
    fall.Reset();
    CHECK(fall.Update(0.0f, true) == 0.0f);
}
