#include <doctest/doctest.h>

#include "../../cpp/shout.h"

using namespace PerkeleRim;

TEST_CASE("a shout is a jump in the recovery time")
{
    Shout::Watch watch;
    CHECK_FALSE(watch.Step(0.0f));
    CHECK_FALSE(watch.Step(0.0f));
    CHECK(watch.Step(15.0f));         // Fus released
    CHECK_FALSE(watch.Step(14.98f));  // counting down
    CHECK_FALSE(watch.Step(3.0f));
    CHECK(watch.Step(20.0f));         // shouted again before it ran out (Amulet of Talos, a blessing)
    CHECK_FALSE(watch.Step(20.2f));   // too small to be a shout
}

TEST_CASE("a cooldown running at the handover is not a shout")
{
    Shout::Watch watch;
    CHECK_FALSE(watch.Step(0.0f));
    watch.Reset();
    CHECK_FALSE(watch.Step(30.0f));
    CHECK_FALSE(watch.Step(29.9f));
}

TEST_CASE("Unrelenting Force yells Perkele, any other shout swears")
{
    CHECK(Shout::KindOf(0x13E07) == Protocol::kShoutUnrelentingForce);
    CHECK(Shout::KindOf(0x13E08) == Protocol::kShoutOther);
    CHECK(Shout::KindOf(0) == Protocol::kShoutOther);
}

TEST_CASE("the words are Skyrim's while charging, else by the cooldown")
{
    const float force[3] = { 20.0f, 25.0f, 45.0f };  // Unrelenting Force's recovery times
    CHECK(Shout::Words(0, force, 1.0f, 20.0f) == 1);
    CHECK(Shout::Words(2, force, 1.0f, 20.0f) == 3);  // the variation wins
    CHECK(Shout::Words(-1, force, 1.0f, 20.0f) == 1);
    CHECK(Shout::Words(-1, force, 1.0f, 26.0f) == 2);
    CHECK(Shout::Words(-1, force, 1.0f, 44.0f) == 3);
    CHECK(Shout::Words(-1, force, 1.0f / 3.0f, 15.0f) == 3);  // fully drunk: a third of 45 s
    CHECK(Shout::Words(-1, force, 0.8f, 16.0f) == 1);         // the Amulet of Talos
    const float none[3] = { 0.0f, 0.0f, 0.0f };
    CHECK(Shout::Words(-1, none, 1.0f, 10.0f) == 0);
}
