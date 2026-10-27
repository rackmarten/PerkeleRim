#include <ostream>

#include <doctest/doctest.h>

#include "../../cpp/helgen_skip.h"

using namespace PerkeleRim::HelgenSkip;

namespace
{
    Frame Cart(float a_delta, std::uint32_t a_stage = 12)
    {
        Frame frame;
        frame.intro = true;
        frame.unboundStage = a_stage;
        frame.delta = a_delta;
        return frame;
    }

    Frame Garage(float a_delta, bool a_arrived, bool a_loading = false)
    {
        Frame frame;
        frame.intro = true;  // Unbound completes through Papyrus, a moment later
        frame.unboundStage = 12;
        frame.arrived = a_arrived;
        frame.loading = a_loading;
        frame.delta = a_delta;
        return frame;
    }

    // Ticks a_seconds in a_step steps, returning the first Step that isn't None.
    Step Run(Machine& a_machine, const Frame& a_frame, float a_seconds)
    {
        for (float t = 0.0f; t < a_seconds; t += a_frame.delta) {
            if (const auto step = a_machine.Tick(a_frame); step != Step::None) {
                return step;
            }
        }
        return Step::None;
    }
}

TEST_CASE("the fade starts a few seconds after the Perkele is over, then the release, then the move")
{
    Machine machine;
    machine.NewGame();
    CHECK(Run(machine, Cart(0.1f), 30.0f) == Step::None);  // the blackout and Ralof's line take a while
    machine.Woke();
    CHECK(Run(machine, Cart(0.1f), kPerkeleSeconds + kAfterPerkele - 0.25f) == Step::None);
    CHECK(Run(machine, Cart(0.1f), 0.5f) == Step::FadeOut);
    CHECK(machine.Skipping());
    CHECK(Run(machine, Cart(0.1f), kFadeOutSeconds - 0.25f) == Step::None);
    CHECK(Run(machine, Cart(0.1f), 0.5f) == Step::Release);
    CHECK(Run(machine, Cart(0.1f), kReleaseSeconds - 0.25f) == Step::None);
    CHECK(Run(machine, Cart(0.1f), 0.5f) == Step::Move);
}

TEST_CASE("it fades in once he has been at the garage for a moment, and is done")
{
    Machine machine;
    machine.NewGame();
    machine.Woke();
    REQUIRE(Run(machine, Cart(0.1f), 60.0f) == Step::FadeOut);
    REQUIRE(Run(machine, Cart(0.1f), 60.0f) == Step::Release);
    REQUIRE(Run(machine, Cart(0.1f), 60.0f) == Step::Move);
    CHECK(Run(machine, Garage(0.1f, false, true), 3.0f) == Step::None);  // the loading screen holds the move
    CHECK(Run(machine, Garage(0.1f, true, true), 3.0f) == Step::None);   // still loading
    CHECK(Run(machine, Garage(0.1f, true), kArrivedSeconds - 0.25f) == Step::None);
    CHECK(Run(machine, Garage(0.1f, true), 0.5f) == Step::FadeIn);
    CHECK_FALSE(machine.Skipping());
    CHECK_FALSE(machine.GaveUp());
    CHECK(machine.Current() == Phase::Off);
    CHECK(Run(machine, Garage(0.1f, true), 10.0f) == Step::None);
}

TEST_CASE("he's moved again while he isn't there, and it fades in anyway after the timeout")
{
    Machine machine;
    machine.NewGame();
    machine.Woke();
    REQUIRE(Run(machine, Cart(0.5f), 60.0f) == Step::FadeOut);
    REQUIRE(Run(machine, Cart(0.5f), 60.0f) == Step::Release);
    REQUIRE(Run(machine, Cart(0.5f), 60.0f) == Step::Move);
    CHECK(Run(machine, Garage(0.5f, false), kMoveRetrySeconds - 0.75f) == Step::None);
    CHECK(Run(machine, Garage(0.5f, false), 1.0f) == Step::Move);
    int moves = 0;
    Step step = Step::None;
    for (float t = 0.0f; t < kArriveTimeout && step != Step::FadeIn; t += 0.5f) {
        step = machine.Tick(Garage(0.5f, false));
        moves += step == Step::Move;
    }
    CHECK(step == Step::FadeIn);
    CHECK(moves > 5);
    CHECK(machine.GaveUp());
}

TEST_CASE("arriving resets when he's pulled away before the moment is over")
{
    Machine machine;
    machine.NewGame();
    machine.Woke();
    REQUIRE(Run(machine, Cart(0.1f), 60.0f) == Step::FadeOut);
    REQUIRE(Run(machine, Cart(0.1f), 60.0f) == Step::Release);
    REQUIRE(Run(machine, Cart(0.1f), 60.0f) == Step::Move);
    CHECK(Run(machine, Garage(0.1f, true), kArrivedSeconds - 0.25f) == Step::None);
    CHECK(machine.Tick(Garage(0.1f, false)) == Step::None);
    CHECK(Run(machine, Garage(0.1f, true), kArrivedSeconds - 0.25f) == Step::None);
    CHECK(Run(machine, Garage(0.1f, true), 0.5f) == Step::FadeIn);
}

TEST_CASE("without Ralof's line the cart reaching Helgen's gate starts the fade")
{
    Machine machine;
    machine.NewGame();
    CHECK(Run(machine, Cart(0.1f, kCartAtGate - 1), 120.0f) == Step::None);
    CHECK(machine.Tick(Cart(0.1f, kCartAtGate)) == Step::FadeOut);
}

TEST_CASE("a loaded save, or a new game without the cart, skips nothing")
{
    Machine machine;
    machine.Loaded();
    machine.Woke();
    CHECK(Run(machine, Cart(0.1f, kCartAtGate), 30.0f) == Step::None);

    machine.NewGame();
    machine.Woke();
    Frame noCart;
    noCart.delta = 0.1f;
    CHECK(Run(machine, noCart, 30.0f) == Step::None);
    CHECK(machine.Current() == Phase::Off);
}

TEST_CASE("only the first Perkele counts")
{
    Machine machine;
    machine.NewGame();
    machine.Woke();
    CHECK(Run(machine, Cart(0.1f), kPerkeleSeconds) == Step::None);
    machine.Woke();  // doesn't start the wait over
    CHECK(Run(machine, Cart(0.1f), kAfterPerkele + 0.25f) == Step::FadeOut);
    machine.Woke();
    CHECK(machine.Current() == Phase::FadingOut);
}

TEST_CASE("HelgenSide picks the friend, Imperial by default")
{
    CHECK(ParseSide("") == Side::Imperial);
    CHECK(ParseSide("Imperial") == Side::Imperial);
    CHECK(ParseSide("hadvar") == Side::Imperial);
    CHECK(ParseSide("nonsense") == Side::Imperial);
    CHECK(ParseSide("Stormcloak") == Side::Stormcloak);
    CHECK(ParseSide("STORMCLOAKS") == Side::Stormcloak);
    CHECK(ParseSide("Ralof") == Side::Stormcloak);
    CHECK(BeforeTheStormStage(Side::Imperial) == 1u);
    CHECK(BeforeTheStormStage(Side::Stormcloak) == 2u);
    CHECK(Friend(Side::Imperial) == "Hadvar");
}
