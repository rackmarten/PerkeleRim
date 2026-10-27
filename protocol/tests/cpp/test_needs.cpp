#include <doctest/doctest.h>

#include <cmath>

#include "../../cpp/needs.h"

using namespace PerkeleRim;

TEST_CASE("needs do nothing up to the mark and are full at 200")
{
    CHECK(Needs::Share(0.0f) == 0.0f);
    CHECK(Needs::Share(100.0f) == 0.0f);
    CHECK(Needs::Share(150.0f) == doctest::Approx(0.5f));
    CHECK(Needs::Share(200.0f) == 1.0f);
    CHECK(Needs::Share(201.0f) == 1.0f);  // MSC's loop clamps to about 201
}

TEST_CASE("starving or dehydrated drains health from 150, enough to beat Skyrim's regen near 200")
{
    CHECK(Needs::StarveDrain(149.0f, 149.0f, 200.0f) == 0.0f);
    CHECK(Needs::StarveDrain(175.0f, 0.0f, 200.0f) == doctest::Approx(2.0f));  // 1% a second
    CHECK(Needs::StarveDrain(0.0f, 200.0f, 200.0f) == doctest::Approx(4.0f));   // thirst alone
    CHECK(Needs::StarveDrain(200.0f, 200.0f, 200.0f) == doctest::Approx(4.0f)); // the worse one, not both
    const float regen = 0.007f;                                                 // Skyrim's HealRate, share a second
    CHECK(Needs::StarveDrain(200.0f, 0.0f, 1.0f) - regen > 0.01f);              // dead in under 100 s
    CHECK(Needs::StarveDrain(160.0f, 0.0f, 1.0f) < regen);                      // a warning, not yet a threat
}

TEST_CASE("fatigue cuts stamina regen, stress punches harder, dirt makes traders dearer")
{
    CHECK(Needs::FatigueRegenCut(80.0f) == 0.0f);
    CHECK(Needs::FatigueRegenCut(150.0f) == doctest::Approx(37.5f));
    CHECK(Needs::FatigueRegenCut(200.0f) == doctest::Approx(75.0f));
    CHECK(Needs::StressPunch(50.0f) == 1.0f);
    CHECK(Needs::StressPunch(200.0f) == doctest::Approx(1.2f));
    CHECK(Needs::StressPunch(400.0f) == doctest::Approx(1.2f));  // stress can go past 200 for a moment
    CHECK(Needs::DirtPrice(0.0f) == 1.0f);
    CHECK(Needs::DirtPrice(150.0f) == doctest::Approx(1.15f));
    CHECK(Needs::DirtPrice(200.0f) == doctest::Approx(1.3f));
}

TEST_CASE("the shake grows with hours dry, a beer or two steadies it")
{
    CHECK(Needs::Shake(0.5f, 0.0f) == 0.0f);   // just sobered up
    CHECK(Needs::Shake(1.0f, 0.0f) == 0.0f);
    CHECK(Needs::Shake(12.5f, 0.0f) == doctest::Approx(0.5f));
    CHECK(Needs::Shake(24.0f, 0.0f) == 1.0f);
    CHECK(Needs::Shake(100.0f, 0.0f) == 1.0f);
    CHECK(Needs::Shake(24.0f, 0.025f) == doctest::Approx(0.5f));  // half a beer in him
    CHECK(Needs::Shake(24.0f, 0.08f) == 0.0f);                    // one beer
}

TEST_CASE("the dry clock starts dry, a drink restarts it")
{
    Needs::DryClock clock;
    CHECK(clock.DryHours(10.0f) == Needs::kShakeFull);  // new game: he wakes up shaky
    clock.Step(10.0f, 0.01f);                            // a sip isn't a beer
    CHECK(clock.DryHours(10.0f) == Needs::kShakeFull);
    clock.Step(10.0f, 0.1f);
    CHECK(clock.DryHours(10.0f) == 0.0f);
    clock.Step(13.0f, 0.0f);
    CHECK(clock.DryHours(13.0f) == doctest::Approx(3.0f));
    CHECK(Needs::Shake(clock.DryHours(13.0f), 0.0f) > 0.0f);
    CHECK(clock.DryHours(5.0f) == 0.0f);  // never negative
    clock.Reset();
    CHECK(clock.LastDrunk() == Needs::kNever);
}

TEST_CASE("the shake offset stays within its share of the screen and moves")
{
    float most = 0.0f;
    bool  moved = false;
    for (int i = 0; i < 600; i++) {
        const auto o = Needs::ShakeOffset(i / 60.0f, 1.0f, 1080.0f);
        most = (std::max)(most, (std::max)(std::abs(o.x), std::abs(o.y)));
        const auto p = Needs::ShakeOffset((i + 1) / 60.0f, 1.0f, 1080.0f);
        moved = moved || std::abs(p.x - o.x) > 0.5f;
    }
    CHECK(most <= Needs::kShakeMax * 1080.0f + 0.001f);
    CHECK(most > Needs::kShakeMax * 1080.0f * 0.7f);
    CHECK(moved);
    const auto still = Needs::ShakeOffset(3.0f, 0.0f, 1080.0f);
    CHECK(still.x == 0.0f);
    CHECK(still.y == 0.0f);
}
