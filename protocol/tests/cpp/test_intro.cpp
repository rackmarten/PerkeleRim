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

TEST_CASE("Perkele comes kWakeDelay after Ralof's line ends, once until a game loads")
{
    Intro::Cues cues;
    cues.Loaded();  // a save from inside the cart: no blackout to wait for
    cues.OnTopicEnd(0x12345);  // another line
    CHECK(cues.Tick(0.5f, true) == 0u);
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(0.2f, true) == 0u);
    CHECK(cues.Tick(0.2f, true) == kIntroWake);
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(1.0f, true) == 0u);

    cues.Loaded();  // a save from before the line hears it again
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(1.0f, true) == kIntroWake);
}

TEST_CASE("Perkele waits until the blackout's snore and hangover line are over")
{
    Intro::Cues cues;
    cues.NewGame();
    CHECK(cues.Tick(0.1f, true) == kIntroBlackout);
    CHECK(cues.Tick(5.0f, true) == 0u);
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);  // Ralof is quick: the hangover line still runs
    float         waited = 5.0f;
    std::uint32_t cue = 0;
    while (cue == 0 && waited < 30.0f) {
        cue = cues.Tick(0.1f, true);
        waited += 0.1f;
    }
    CHECK(cue == kIntroWake);
    CHECK(waited >= Intro::kBlackoutSeconds);
    CHECK(waited < Intro::kBlackoutSeconds + 0.3f);
}

TEST_CASE("Perkele after a slow Ralof comes right after his line")
{
    Intro::Cues cues;
    cues.NewGame();
    CHECK(cues.Tick(0.1f, true) == kIntroBlackout);
    CHECK(cues.Tick(Intro::kBlackoutSeconds + 5.0f, true) == 0u);
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(0.2f, true) == 0u);
    CHECK(cues.Tick(0.2f, true) == kIntroWake);
}

TEST_CASE("the answer is dropped when MSC isn't there, not kept for later")
{
    Intro::Cues cues;
    cues.Loaded();
    cues.OnTopicEnd(Intro::kRalofFinallyAwake);
    CHECK(cues.Tick(1.0f, false) == 0u);
    CHECK(cues.Tick(1.0f, true) == 0u);
}

TEST_CASE("captions follow the cue's sounds")
{
    CHECK(Intro::CaptionAt(kIntroBlackout, 1.0f) == Intro::kBlackoutLines[0].text);
    CHECK(Intro::CaptionAt(kIntroBlackout, Intro::kHangoverAt + 0.1f) == Intro::kBlackoutLines[1].text);
    CHECK(Intro::CaptionAt(kIntroBlackout, Intro::kHangoverAt + 6.0f) == Intro::kBlackoutLines[2].text);
    CHECK(Intro::CaptionAt(kIntroBlackout, Intro::kBlackoutSeconds + 0.1f).empty());
    CHECK(Intro::CaptionsSeconds(kIntroBlackout) == doctest::Approx(Intro::kBlackoutSeconds));

    // The shack's wake-up shows the same two lines as the cart's hangover part.
    CHECK(Intro::CaptionAt(kIntroHangover, 0.1f) == Intro::kBlackoutLines[1].text);
    CHECK(Intro::CaptionAt(kIntroHangover, 6.0f) == Intro::kBlackoutLines[2].text);
    CHECK(Intro::CaptionsSeconds(kIntroHangover) == doctest::Approx(Intro::kHangoverSeconds));

    CHECK(Intro::CaptionAt(kIntroWake, 1.0f) == "Kumpo: Perkele!");
    CHECK(Intro::CaptionAt(0, 1.0f).empty());
    CHECK(Intro::CaptionsSeconds(0) == 0.0f);
}
