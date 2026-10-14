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

TEST_CASE("running someone over hurts by speed and kills from 50 km/h")
{
    CHECK(Damage::RunOver(100.0f, false, 1.9f) == 0.0f);  // a nudge while parking
    CHECK(Damage::RunOver(100.0f, false, 2.0f) == 0.0f);
    const float at20 = Damage::RunOver(100.0f, false, 20.0f / 3.6f);
    CHECK(at20 > 20.0f);
    CHECK(at20 < 50.0f);  // ragdolled, survives
    CHECK(Damage::RunOver(100.0f, false, 13.0f) < 100.0f);
    CHECK(Damage::RunOver(100.0f, false, 50.0f / 3.6f) >= 100.0f);
    CHECK(Damage::RunOver(100.0f, false, 60.0f / 3.6f) == doctest::Approx(200.0f));  // dead, fortified or not
    CHECK(Damage::RunOver(3000.0f, true, 60.0f / 3.6f) == doctest::Approx(1500.0f));  // a dragon gets up again
    CHECK(Damage::RunOver(-5.0f, false, 20.0f) == 0.0f);
    CHECK(Damage::KnockOver(0.0f) == 1.0f);
    CHECK(Damage::KnockOver(10.0f) == doctest::Approx(5.0f));
    CHECK(Damage::KnockOver(100.0f) == Damage::kKnockMax);
}

TEST_CASE("weight from bounds sorts actors into light, heavy and huge")
{
    CHECK(Damage::Mass(44.0f, 28.0f, 128.0f) == doctest::Approx(80.0f));  // a guard
    CHECK(Damage::Mass(-1.0f, 28.0f, 128.0f) == 0.0f);
    const float chicken = Damage::Mass(19.0f * 1.3f, 35.0f * 1.3f, 36.0f * 1.3f);
    const float cow = Damage::Mass(51.0f, 181.0f, 100.0f);
    CHECK(Damage::BulkOf(80.0f, false) == Damage::Bulk::kLight);
    CHECK(Damage::BulkOf(chicken, false) == Damage::Bulk::kLight);
    CHECK(Damage::BulkOf(cow, false) == Damage::Bulk::kHeavy);
    CHECK(Damage::BulkOf(Damage::kHugeMass, false) == Damage::Bulk::kHuge);
    CHECK(Damage::BulkOf(10.0f, true) == Damage::Bulk::kHuge);  // a dragon, whatever its bounds say
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
