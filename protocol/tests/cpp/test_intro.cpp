#include <ostream>

#include <doctest/doctest.h>

#include "../../cpp/intro_cues.h"

using namespace PerkeleRim;
using namespace PerkeleRim::Protocol;

TEST_CASE("a new game sends the blackout as soon as MSC is in its game, once")
{
    Intro::Cues cues;
    cues.NewGame();
    CHECK(cues.Tick(0.1f, false) == 0u);
    CHECK(cues.Tick(0.1f, false) == 0u);
    CHECK(cues.Tick(0.1f, true) == kIntroBlackout);
    CHECK(cues.Tick(0.1f, true) == 0u);
}

TEST_CASE("the blackout is dropped when MSC isn't there within the window")
{
    Intro::Cues cues;
    cues.NewGame();
    for (float t = 0.0f; t <= Intro::kBlackoutWindow + 0.5f; t += 0.5f) {
        CHECK(cues.Tick(0.5f, false) == 0u);
    }
    CHECK(cues.Tick(0.1f, true) == 0u);
}

TEST_CASE("a loaded save gets no blackout")
{
    Intro::Cues cues;
    cues.Loaded();
    CHECK(cues.Tick(0.1f, true) == 0u);
}

TEST_CASE("Kumpo answers Ralof's line kWakeDelay after it ends, once until a game loads")
{
    Intro::Cues cues;
    cues.NewGame();
    CHECK(cues.Tick(0.1f, true) == kIntroBlackout);
    cues.OnTopicEnd(0x12345);  // another line
    CHECK(cues.Tick(0.5f, true) == 0u);
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(0.2f, true) == 0u);
    CHECK(cues.Tick(0.2f, true) == kIntroWake);
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);  // said again (the save loaded before it is a new game load)
    CHECK(cues.Tick(1.0f, true) == 0u);

    cues.Loaded();  // a save from before the line hears it again
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(1.0f, true) == kIntroWake);
}

TEST_CASE("the answer is dropped when MSC isn't there, not kept for later")
{
    Intro::Cues cues;
    cues.Loaded();
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(1.0f, false) == 0u);
    CHECK(cues.Tick(1.0f, true) == 0u);
}

TEST_CASE("blackout and answer due on the same frame go one after the other")
{
    Intro::Cues cues;
    cues.NewGame();
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(1.0f, true) == kIntroBlackout);
    CHECK(cues.Tick(0.5f, true) == kIntroWake);
    CHECK(cues.Tick(0.5f, true) == 0u);
}

TEST_CASE("captions")
{
    CHECK(Intro::Caption(kIntroBlackout) == Intro::kBlackoutCaption);
    CHECK(Intro::Caption(kIntroWake) == Intro::kWakeCaption);
    CHECK(Intro::Caption(0).empty());
    CHECK(Intro::CaptionSeconds(kIntroBlackout) > 0.0f);
    CHECK(Intro::CaptionSeconds(0) == 0.0f);
}
