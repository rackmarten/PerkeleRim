#include <doctest/doctest.h>

#include "../../cpp/hurt.h"

using namespace PerkeleRim;

TEST_CASE("health dropping low counts once until healed")
{
    Hurt::LowHealth low;
    CHECK_FALSE(low.Step(1.0f));
    CHECK_FALSE(low.Step(0.6f));
    CHECK(low.Step(0.2f));         // dropped low
    CHECK_FALSE(low.Step(0.1f));   // still low
    CHECK_FALSE(low.Step(0.4f));   // a little better is not enough
    CHECK_FALSE(low.Step(0.2f));
    CHECK_FALSE(low.Step(0.7f));   // healed
    CHECK(low.Step(0.24f));        // low again
}

TEST_CASE("already low at the handover is not a drop")
{
    Hurt::LowHealth low;
    CHECK_FALSE(low.Step(0.8f));
    low.Reset();
    CHECK_FALSE(low.Step(0.1f));
    CHECK_FALSE(low.Step(0.05f));
    CHECK_FALSE(low.Step(0.6f));
    CHECK(low.Step(0.2f));
}
