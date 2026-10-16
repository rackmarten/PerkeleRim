#include <doctest/doctest.h>

#include "../../cpp/damage.h"

using namespace PerkeleRim;

TEST_CASE("an MSC death costs half of Skyrim's max health")
{
    CHECK(Damage::Crash(200.0f, 1.0f) == doctest::Approx(100.0f));
    CHECK(Damage::Crash(200.0f, 0.5f) == doctest::Approx(50.0f));
    CHECK(Damage::Crash(200.0f, -1.0f) == 0.0f);
}

TEST_CASE("a punch is Skyrim's unarmed damage, scaled")
{
    CHECK(Damage::Punch(4.0f) == doctest::Approx(8.0f));  // a Nord, sober: an iron sword's hit
    CHECK(Damage::Punch(22.0f) == doctest::Approx(44.0f));  // a Khajiit
    CHECK(Damage::Punch(-5.0f) == 0.0f);
    // Level 1 and sober, against vanilla health: a wolf (22) in three punches, a soldier (50) in
    // six or seven.
    CHECK(Damage::Punch(4.0f) * 2.0f < 22.0f);
    CHECK(Damage::Punch(4.0f) * 3.0f >= 22.0f);
    CHECK(Damage::Punch(4.0f) * 6.0f < 50.0f);
    CHECK(Damage::Punch(4.0f) * 7.0f >= 50.0f);
}

TEST_CASE("drunk punches hit harder, up to twice as hard at MSC's drunk 3")
{
    CHECK(Damage::Punch(4.0f, 0.0f) == doctest::Approx(8.0f));
    CHECK(Damage::Punch(4.0f, 0.4f) == doctest::Approx(8.0f * (1.0f + 0.4f / 3.0f)));  // five beers: a little
    CHECK(Damage::Punch(4.0f, 1.5f) == doctest::Approx(12.0f));
    CHECK(Damage::Punch(4.0f, 3.0f) == doctest::Approx(16.0f));
    CHECK(Damage::Punch(4.0f, 8.0f) == doctest::Approx(16.0f));
    CHECK(Damage::Punch(4.0f, -1.0f) == doctest::Approx(8.0f));
}

TEST_CASE("drunk makes the player tougher, up to MSC's drunk 3")
{
    const auto sober = Damage::Drunk(0.0f);
    CHECK(sober.armor == 0.0f);
    CHECK(sober.stamina == 0.0f);
    CHECK(sober.staminaRate == 0.0f);
    const auto half = Damage::Drunk(1.5f);
    CHECK(half.armor == doctest::Approx(100.0f));
    CHECK(half.stamina == doctest::Approx(50.0f));
    CHECK(half.staminaRate == doctest::Approx(50.0f));
    const auto wasted = Damage::Drunk(8.0f);
    CHECK(wasted.armor == doctest::Approx(200.0f));
    CHECK(wasted.stamina == doctest::Approx(100.0f));
    CHECK(wasted.staminaRate == doctest::Approx(100.0f));
    CHECK(Damage::Drunk(-1.0f).armor == 0.0f);
}

TEST_CASE("a thrown item hits by its speed, up to a sober punch")
{
    const float punch = Damage::Punch(4.0f);
    CHECK(Damage::Thrown(punch, 1.0f) == 0.0f);
    CHECK(Damage::Thrown(punch, 2.0f) == 0.0f);
    CHECK(Damage::Thrown(punch, 8.5f) == doctest::Approx(punch * 0.5f));
    CHECK(Damage::Thrown(punch, 15.0f) == doctest::Approx(punch));
    CHECK(Damage::Thrown(punch, 40.0f) == doctest::Approx(punch));
    CHECK(Damage::Thrown(-5.0f, 15.0f) == 0.0f);
}

TEST_CASE("running someone over hits harder than a punch, a nudge softer")
{
    CHECK(Damage::RunOver(60.0f, false, 35.0f / 3.6f) > Damage::Punch(4.0f, 4.0f));  // a level 1 bandit at 35 km/h
    CHECK(Damage::RunOver(60.0f, false, 2.5f) < Damage::Punch(4.0f));
}

TEST_CASE("a push staggers the actor away from where it comes from")
{
    constexpr float kPi = 3.14159265f;
    CHECK(Damage::StaggerDirection(0.0f, 0.0f, -1.0f) == doctest::Approx(0.0f));   // facing +Y, pushed from the front
    CHECK(Damage::StaggerDirection(0.0f, 0.0f, 1.0f) == doctest::Approx(0.5f));    // from behind
    CHECK(Damage::StaggerDirection(0.0f, -1.0f, 0.0f) == doctest::Approx(0.25f));  // from its right (+X)
    CHECK(Damage::StaggerDirection(0.0f, 1.0f, 0.0f) == doctest::Approx(0.75f));   // from its left
    CHECK(Damage::StaggerDirection(kPi * 0.5f, -1.0f, 0.0f) == doctest::Approx(0.0f));  // facing +X, from the front
    CHECK(Damage::StaggerDirection(-kPi * 0.5f, -1.0f, 0.0f) == doctest::Approx(0.5f));
    CHECK(Damage::StaggerDirection(0.0f, 0.0f, 0.0f) == 0.0f);
    const float any = Damage::StaggerDirection(12.0f, 0.3f, -0.7f);
    CHECK(any >= 0.0f);
    CHECK(any < 1.0f);
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
    CHECK(Damage::Heal(kConsumedBeer, 200.0f, 50.0f) == doctest::Approx(6.0f));
    CHECK(Damage::Heal(kConsumedCigarette, 200.0f, 50.0f) == doctest::Approx(4.0f));
    CHECK(Damage::Heal(kConsumedFood, 200.0f, 50.0f) == doctest::Approx(30.0f));
    CHECK(Damage::Heal(kConsumedSpirit, 200.0f, 50.0f) == doctest::Approx(16.0f));
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

TEST_CASE("the horn scatters whoever is in the vehicle's way")
{
    // Heading +Y.
    CHECK(Damage::InHornPath(0.0f, 600.0f, 0.0f, 1.0f));       // ahead on its line
    CHECK(Damage::InHornPath(450.0f, 600.0f, 0.0f, 1.0f));     // ahead, off to the side within the lane
    CHECK_FALSE(Damage::InHornPath(600.0f, 500.0f, 0.0f, 1.0f));  // ahead but well clear
    CHECK_FALSE(Damage::InHornPath(0.0f, -600.0f, 0.0f, 1.0f));   // behind
    CHECK(Damage::InHornPath(0.0f, -300.0f, 0.0f, 1.0f));       // right next to it, behind or not
    CHECK_FALSE(Damage::InHornPath(0.0f, 900.0f, 0.0f, 1.0f));    // out of earshot
    CHECK(Damage::InHornPath(500.0f, 500.0f, 0.0f, 0.0f));    // no heading: everyone in range
    CHECK(Damage::InHornPath(0.0f, 600.0f, 0.0f, 3.0f));       // the way needn't be a unit vector
}

TEST_CASE("the horn's dive heads out of the lane on the actor's own side")
{
    float x = 0.0f, y = 0.0f;
    Damage::HornDodge(100.0f, 1000.0f, 0.0f, 1.0f, x, y);  // heading +Y, a bit to the right
    CHECK(x == doctest::Approx(500.0f / 3.0f));            // a third of the way to 600 off the line
    CHECK(y == doctest::Approx(0.0f));
    Damage::HornDodge(-100.0f, 1000.0f, 0.0f, 2.0f, x, y);  // a bit to the left
    CHECK(x == doctest::Approx(-500.0f / 3.0f));
    Damage::HornDodge(0.0f, 1000.0f, 0.0f, 1.0f, x, y);     // dead ahead: some side, the whole way
    CHECK(std::abs(x) == doctest::Approx(200.0f));
    Damage::HornDodge(550.0f, 1000.0f, 0.0f, 1.0f, x, y);   // nearly clear: still a step
    CHECK(x == doctest::Approx(Damage::kHornDodgeMinUnits));
    Damage::HornDodge(0.0f, 200.0f, 0.0f, 0.0f, x, y);      // no way: away from the vehicle
    CHECK(x == doctest::Approx(0.0f));
    CHECK(y == doctest::Approx(400.0f / 3.0f));
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
