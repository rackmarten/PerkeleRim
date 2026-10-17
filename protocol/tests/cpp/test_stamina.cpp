#include <doctest/doctest.h>

#include "../../cpp/input.h"
#include "../../cpp/stamina.h"

using namespace PerkeleRim;

TEST_CASE("running drains stamina, walking and standing do not")
{
    Stamina::Sprint sprint;
    CHECK(sprint.Step(0.5f, true, 9.6f, 100.0f, 100.0f, true).drain == doctest::Approx(3.5f));
    CHECK(sprint.Step(0.5f, true, 5.5f, 100.0f, 100.0f, true).drain == 0.0f);   // walking
    CHECK(sprint.Step(0.5f, false, 9.6f, 100.0f, 100.0f, true).drain == 0.0f);  // coasting without the key
    CHECK(sprint.Step(0.5f, true, 9.6f, 100.0f, 100.0f, false).drain == 0.0f);  // driving
}

TEST_CASE("an empty bar stops the run until a fifth of the stamina is back")
{
    Stamina::Sprint sprint;
    CHECK_FALSE(sprint.Step(0.05f, true, 9.6f, 10.0f, 100.0f, true).blockRun);
    const auto empty = sprint.Step(0.05f, true, 9.6f, 0.0f, 100.0f, true);
    CHECK(empty.blockRun);
    CHECK(empty.drain == 0.0f);
    CHECK(sprint.Step(0.05f, true, 2.0f, 15.0f, 100.0f, true).blockRun);   // still held back
    CHECK(sprint.Step(0.05f, false, 0.0f, 19.0f, 100.0f, true).blockRun);  // releasing the key doesn't reset it
    CHECK_FALSE(sprint.Step(0.05f, true, 5.0f, 20.0f, 100.0f, true).blockRun);
    CHECK_FALSE(sprint.Exhausted());
}

TEST_CASE("a bigger bar (drunk) raises the recovery point, and an empty bar without the key is fine")
{
    Stamina::Sprint sprint;
    CHECK_FALSE(sprint.Step(0.05f, false, 0.0f, 0.0f, 200.0f, true).blockRun);
    CHECK(sprint.Step(0.05f, true, 0.0f, 0.0f, 200.0f, true).blockRun);
    CHECK(sprint.Step(0.05f, true, 0.0f, 39.0f, 200.0f, true).blockRun);
    CHECK_FALSE(sprint.Step(0.05f, true, 0.0f, 40.0f, 200.0f, true).blockRun);
}

TEST_CASE("the run key can be held back from MSC without forgetting it is held")
{
    Input::Builder input;
    input.SetKey(Input::KeyFromScanCode(Input::kDikLeftShift), true);
    input.SetKey(Input::KeyCode::W, true);
    input.Block(304, true);
    auto s = input.Build(1, true, true);
    CHECK((s.keys[304 >> 6] & (1ull << (304 & 63))) == 0);
    CHECK((s.keys[Input::KeyCode::W >> 6] & (1ull << (Input::KeyCode::W & 63))) != 0);
    CHECK(input.IsKeyDown(304));
    input.Block(304, false);
    s = input.Build(2, true, true);
    CHECK((s.keys[304 >> 6] & (1ull << (304 & 63))) != 0);
}
