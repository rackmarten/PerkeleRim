#include <doctest/doctest.h>

#include "../../cpp/finger.h"

using namespace PerkeleRim;

TEST_CASE("the finger is seen close by and in front")
{
    CHECK(Finger::InView(0.0f, 200.0f, 0.0f, 1.0f));     // 3 m straight ahead
    CHECK(Finger::InView(100.0f, 200.0f, 0.0f, 0.5f));   // a little to the side, any length of forward
    CHECK_FALSE(Finger::InView(0.0f, 400.0f, 0.0f, 1.0f));  // 6 m: too far
    CHECK_FALSE(Finger::InView(0.0f, -200.0f, 0.0f, 1.0f)); // behind
    CHECK_FALSE(Finger::InView(250.0f, 200.0f, 0.0f, 1.0f)); // more than 45 degrees off
    CHECK(Finger::InView(0.5f, 0.0f, 0.0f, 1.0f));       // right on top of the player
    CHECK_FALSE(Finger::InView(0.0f, 200.0f, 0.0f, 0.0f));  // looking straight up or down
}

TEST_CASE("each person minds the finger once a minute")
{
    Finger::Bounties bounties;
    CHECK(bounties.Take(0x1, 10.0));
    CHECK(bounties.Take(0x2, 10.0));       // someone else
    CHECK_FALSE(bounties.Take(0x1, 30.0));
    CHECK_FALSE(bounties.Take(0x1, 69.9));
    CHECK_FALSE(bounties.Take(0x2, 69.9));
    CHECK(bounties.Take(0x1, 70.0));
    CHECK(bounties.Take(0x2, 70.0));
    CHECK(bounties.Take(0x2, 5.0));        // the clock went back (a load): everyone forgets
}
