#include <ostream>
#include <vector>

#include <doctest/doctest.h>

#include "../../cpp/blackout_wake.h"

using namespace PerkeleRim::Blackout;
namespace Protocol = PerkeleRim::Protocol;

namespace
{
    constexpr float kHere[3] = { 15170.0f, -14516.0f, -3000.0f };  // the garage

    Marker At(float a_x, float a_y, std::uint16_t a_type)
    {
        return Marker{ { a_x, a_y, 0.0f }, a_type };
    }

    Frame Tick(float a_delta, bool a_arrived = false, bool a_loading = false)
    {
        Frame frame;
        frame.delta = a_delta;
        frame.arrived = a_arrived;
        frame.loading = a_loading;
        return frame;
    }

    // Ticks a_seconds in a_frame.delta steps, returning the first Step that isn't None.
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

TEST_CASE("blackout: he wakes up only at peaceful open ground")
{
    for (const std::uint16_t type : { kCity, kTown, kSettlement, kFarm, kWoodMill, kWheatMill, kStable, kLighthouse, kDocks }) {
        CHECK(WakeType(type));
    }
    // Cave, camp, fort, Nordic ruin, dragon lair, giant camp, shrine, door, quest target.
    for (const std::uint16_t type : { std::uint16_t{ 0 }, std::uint16_t{ 4 }, std::uint16_t{ 5 }, std::uint16_t{ 6 }, std::uint16_t{ 7 }, std::uint16_t{ 12 }, std::uint16_t{ 29 }, std::uint16_t{ 34 }, std::uint16_t{ 61 }, std::uint16_t{ 62 } }) {
        CHECK_FALSE(WakeType(type));
    }
}

TEST_CASE("blackout: the marker is somewhere else")
{
    const std::vector<Marker> markers = {
        At(kHere[0] + 500.0f, kHere[1], kTown),             // 0: right here
        At(kHere[0] + 200000.0f, kHere[1], 4),              // 1: far, but a cave
        At(kHere[0], kHere[1] + 150000.0f, kFarm),          // 2: far
        At(kHere[0] - 300000.0f, kHere[1], kCity),          // 3: far
        At(kHere[0] + kMinDistance * 0.9f, kHere[1], kStable),  // 4: not far enough
    };
    CHECK(Pick(markers, kHere, 0.0f) == 2);
    CHECK(Pick(markers, kHere, 0.49f) == 2);
    CHECK(Pick(markers, kHere, 0.5f) == 3);
    CHECK(Pick(markers, kHere, 0.999f) == 3);
    CHECK(Pick(markers, kHere, 1.0f) == 3);   // a roll of 1 stays in range
    CHECK(Pick(markers, kHere, -1.0f) == 2);  // and so does a negative one
    // Every far marker comes up for some roll.
    for (int i = 0; i < 100; i++) {
        const auto picked = Pick(markers, kHere, i / 100.0f);
        CHECK((picked == 2 || picked == 3));
    }
}

TEST_CASE("blackout: with nothing far enough any wake marker will do, with none he stays")
{
    const std::vector<Marker> near = { At(kHere[0] + 500.0f, kHere[1], kTown), At(kHere[0], kHere[1] + 900.0f, 7), At(kHere[0], kHere[1], kDocks) };
    CHECK(Pick(near, kHere, 0.0f) == 0);
    CHECK(Pick(near, kHere, 0.99f) == 2);

    const std::vector<Marker> none = { At(kHere[0] + 500000.0f, kHere[1], 4), At(kHere[0], kHere[1], 12) };
    CHECK(Pick(none, kHere, 0.5f) == -1);
    CHECK(Pick(std::span<const Marker>{}, kHere, 0.5f) == -1);
}

TEST_CASE("blackout: a drunk sleeps it off, a knockout is short")
{
    CHECK(Hours(Protocol::kBlackoutDrunk, 0.0f) == doctest::Approx(kDrunkHoursMin));
    CHECK(Hours(Protocol::kBlackoutDrunk, 1.0f) == doctest::Approx(kDrunkHoursMax));
    CHECK(Hours(Protocol::kBlackoutDrunk, 0.5f) == doctest::Approx(8.0f));
    CHECK(Hours(Protocol::kBlackoutKnockout, 0.0f) == doctest::Approx(kKnockoutHoursMin));
    CHECK(Hours(Protocol::kBlackoutKnockout, 2.0f) == doctest::Approx(kKnockoutHoursMax));
    CHECK(Hours(99, -1.0f) == doctest::Approx(kDrunkHoursMin));  // an unknown kind counts as drunk
}

TEST_CASE("blackout: fade out, move, wake once he stands there")
{
    Machine m;
    CHECK(m.Current() == Phase::Off);
    CHECK(m.Tick(Tick(0.016f)) == Step::None);

    REQUIRE(m.Start(Protocol::kBlackoutDrunk));
    CHECK(m.Busy());
    CHECK(m.Kind() == Protocol::kBlackoutDrunk);
    CHECK(m.Tick(Tick(0.016f)) == Step::FadeOut);
    CHECK_FALSE(m.Start(Protocol::kBlackoutKnockout));  // one at a time

    CHECK(Run(m, Tick(0.1f), kFadeOutSeconds - 0.3f) == Step::None);
    CHECK(Run(m, Tick(0.1f), 0.5f) == Step::Move);
    CHECK(m.Current() == Phase::Moving);
    CHECK(m.NoFall());

    // Not there yet, then there but loading, then there.
    CHECK(Run(m, Tick(0.1f), 5.0f) == Step::None);
    CHECK(Run(m, Tick(0.1f, true, true), 5.0f) == Step::None);
    CHECK(Run(m, Tick(0.1f, true), kArrivedSeconds - 0.3f) == Step::None);
    CHECK(Run(m, Tick(0.1f, true), 0.5f) == Step::Wake);
    CHECK_FALSE(m.GaveUp());
    CHECK_FALSE(m.Busy());
    CHECK(m.NoFall());

    CHECK(Run(m, Tick(0.1f), kLandingSeconds + 0.5f) == Step::None);
    CHECK(m.Current() == Phase::Off);
    CHECK(m.Start(Protocol::kBlackoutKnockout));  // and the next one can come
}

TEST_CASE("blackout: standing there must last, and the screen never stays black")
{
    Machine m;
    REQUIRE(m.Start(Protocol::kBlackoutKnockout));
    CHECK(Run(m, Tick(0.1f), 10.0f) == Step::FadeOut);
    CHECK(Run(m, Tick(0.1f), 10.0f) == Step::Move);
    // Arrived for a moment, then gone again: the count starts over.
    CHECK(Run(m, Tick(0.1f, true), kArrivedSeconds - 0.5f) == Step::None);
    CHECK(Run(m, Tick(0.1f), 0.2f) == Step::None);
    CHECK(Run(m, Tick(0.1f, true), kArrivedSeconds - 0.5f) == Step::None);
    // Never there: woken up anyway at the timeout.
    CHECK(Run(m, Tick(0.1f), kArriveTimeout + 1.0f) == Step::Wake);
    CHECK(m.GaveUp());
}

TEST_CASE("blackout: a load ends it")
{
    Machine m;
    REQUIRE(m.Start(Protocol::kBlackoutDrunk));
    CHECK(m.Tick(Tick(0.1f)) == Step::FadeOut);
    m.Loaded();
    CHECK(m.Current() == Phase::Off);
    CHECK_FALSE(m.Busy());
    CHECK(Run(m, Tick(0.1f), 10.0f) == Step::None);
}
