#include <doctest/doctest.h>

#include "../../cpp/wading.h"

using namespace PerkeleRim;

TEST_CASE("walking in up to the waist washes once, ankle-deep does nothing")
{
    Wading::Edge edge;
    CHECK_FALSE(edge.Step(0.0f, true));
    CHECK_FALSE(edge.Step(0.15f, true));  // ankles
    CHECK_FALSE(edge.Step(0.3f, true));   // knees
    CHECK_FALSE(edge.Step(0.49f, true));
    CHECK(edge.Step(0.5f, true));         // waist
    CHECK_FALSE(edge.Step(0.6f, true));   // stays in
    CHECK_FALSE(edge.Step(1.0f, true));
}

TEST_CASE("leaving the water re-arms it")
{
    Wading::Edge edge;
    CHECK(edge.Step(0.7f, true));
    CHECK_FALSE(edge.Step(0.7f, true));
    CHECK_FALSE(edge.Step(0.0f, true));  // out on the bank
    CHECK(edge.Step(0.55f, true));       // in again
}

TEST_CASE("waves at the edge don't wash again before he is out")
{
    Wading::Edge edge;
    CHECK(edge.Step(0.52f, true));
    CHECK_FALSE(edge.Step(0.45f, true));  // between kRearm and kWash
    CHECK_FALSE(edge.Step(0.51f, true));
    CHECK_FALSE(edge.Step(0.45f, true));
    CHECK_FALSE(edge.Step(0.39f, true));  // out
    CHECK(edge.Step(0.5f, true));
}

TEST_CASE("not on foot counts as out of the water; a reset re-arms")
{
    Wading::Edge edge;
    CHECK_FALSE(edge.Step(0.9f, false));  // seated in a vehicle in the river
    CHECK(edge.Step(0.9f, true));         // steps out into the water
    CHECK_FALSE(edge.Step(0.9f, false));  // sits down
    CHECK(edge.Step(0.9f, true));
    CHECK_FALSE(edge.Step(0.9f, true));
    edge.Reset();
    CHECK(edge.Step(0.9f, true));
}
