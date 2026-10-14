#include <doctest/doctest.h>

#include "../../cpp/damage.h"

using namespace PerkeleRim;

TEST_CASE("an MSC death costs half of Skyrim's max health")
{
    CHECK(Damage::Crash(200.0f, 1.0f) == doctest::Approx(100.0f));
    CHECK(Damage::Crash(200.0f, 0.5f) == doctest::Approx(50.0f));
    CHECK(Damage::Crash(200.0f, -1.0f) == 0.0f);
}

TEST_CASE("a punch costs a quarter of the target's max health, dragons less")
{
    CHECK(Damage::Punch(100.0f, false) == doctest::Approx(25.0f));
    CHECK(Damage::Punch(100.0f, false) * 4.0f >= 100.0f);  // four punches drop anyone
    CHECK(Damage::Punch(3000.0f, true) == doctest::Approx(187.5f));
    CHECK(Damage::Punch(-5.0f, false) == 0.0f);
}

TEST_CASE("drunk punches hit harder, up to twice as hard")
{
    CHECK(Damage::Punch(100.0f, false, 0.0f) == doctest::Approx(25.0f));
    CHECK(Damage::Punch(100.0f, false, 2.0f) == doctest::Approx(37.5f));
    CHECK(Damage::Punch(100.0f, false, 4.0f) * 2.0f >= 100.0f);  // two drunk punches drop anyone
    CHECK(Damage::Punch(100.0f, false, 8.0f) == doctest::Approx(50.0f));
    CHECK(Damage::Punch(100.0f, false, -1.0f) == doctest::Approx(25.0f));
}

TEST_CASE("a thrown item hits by its speed, up to a sober punch")
{
    CHECK(Damage::Thrown(100.0f, false, 1.0f) == 0.0f);
    CHECK(Damage::Thrown(100.0f, false, 2.0f) == 0.0f);
    CHECK(Damage::Thrown(100.0f, false, 8.5f) == doctest::Approx(12.5f));
    CHECK(Damage::Thrown(100.0f, false, 15.0f) == doctest::Approx(25.0f));
    CHECK(Damage::Thrown(100.0f, false, 40.0f) == doctest::Approx(25.0f));
    CHECK(Damage::Thrown(3000.0f, true, 15.0f) == doctest::Approx(187.5f));
    CHECK(Damage::Thrown(-5.0f, false, 15.0f) == 0.0f);
}

TEST_CASE("piss poisons for a share of max health per second, dragons less")
{
    CHECK(Damage::PissPerSecond(100.0f, false) == doctest::Approx(2.0f));
    CHECK(Damage::PissPerSecond(100.0f, false) * Damage::kPissSeconds == doctest::Approx(10.0f));
    CHECK(Damage::PissPerSecond(3000.0f, true) == doctest::Approx(15.0f));
    CHECK(Damage::PissPerSecond(-5.0f, false) == 0.0f);
}

TEST_CASE("drinks, cigarettes and food heal a share of max health, never past it")
{
    using namespace PerkeleRim::Protocol;
    CHECK(Damage::Heal(kConsumedBeer, 200.0f, 50.0f) == doctest::Approx(20.0f));
    CHECK(Damage::Heal(kConsumedCigarette, 200.0f, 50.0f) == doctest::Approx(10.0f));
    CHECK(Damage::Heal(kConsumedFood, 200.0f, 50.0f) == doctest::Approx(50.0f));
    CHECK(Damage::Heal(kConsumedSpirit, 200.0f, 50.0f) == doctest::Approx(50.0f));
    CHECK(Damage::Heal(kConsumedFood, 200.0f, 190.0f) == doctest::Approx(10.0f));  // capped at max
    CHECK(Damage::Heal(kConsumedBeer, 200.0f, 200.0f) == 0.0f);
    CHECK(Damage::Heal(kConsumedBeer, 200.0f, 250.0f) == 0.0f);  // fortified above max: no harm
    CHECK(Damage::Heal(0, 200.0f, 50.0f) == 0.0f);
    CHECK(Damage::Heal(99, 200.0f, 50.0f) == 0.0f);
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
