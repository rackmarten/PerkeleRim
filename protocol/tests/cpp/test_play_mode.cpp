#include <doctest/doctest.h>

#include "../../cpp/play_mode.h"

using namespace PerkeleRim;
using PlayMode::Phase;
using PlayMode::Say;

namespace
{
    // A frame where everything is ready: Skyrim settled-looking, MSC linked and in its game.
    PlayMode::Frame Ready(float a_delta = 0.1f)
    {
        PlayMode::Frame f;
        f.linkUp = true;
        f.mscInGame = true;
        f.player3D = true;
        f.cellAttached = true;
        f.controls = true;
        f.delta = a_delta;
        return f;
    }

    // Ticks a_seconds of a_frame in a_frame.delta steps; the first Say other than None, or None.
    Say Run(PlayMode::Machine& a_machine, const PlayMode::Frame& a_frame, float a_seconds)
    {
        for (float t = 0.0f; t < a_seconds; t += a_frame.delta) {
            if (const auto say = a_machine.Tick(a_frame); say != Say::None) {
                return say;
            }
        }
        return Say::None;
    }
}

TEST_CASE("nothing hands over before a load")
{
    PlayMode::Machine m;
    CHECK(m.Current() == Phase::Off);
    CHECK(Run(m, Ready(), 10.0f) == Say::None);
}

TEST_CASE("a load hands over once Skyrim has settled for kSettleSeconds, then never again")
{
    PlayMode::Machine m;
    m.Load();
    CHECK(m.Waiting());
    CHECK(Run(m, Ready(), PlayMode::kSettleSeconds - 0.25f) == Say::None);
    CHECK(Run(m, Ready(), 0.5f) == Say::HandOver);
    CHECK(m.Current() == Phase::Off);
    CHECK(Run(m, Ready(), 10.0f) == Say::None);
}

TEST_CASE("anything off while settling starts the wait over")
{
    PlayMode::Machine m;
    m.Load();
    CHECK(Run(m, Ready(), 1.5f) == Say::None);
    auto loading = Ready();
    loading.loading = true;
    CHECK(m.Tick(loading) == Say::None);
    CHECK(Run(m, Ready(), 1.5f) == Say::None);  // 1.5 s again, not 3 s in total
    CHECK(Run(m, Ready(), 1.0f) == Say::HandOver);

    const auto never = [&](void (*a_spoil)(PlayMode::Frame&)) {
        m.Load();
        auto f = Ready();
        a_spoil(f);
        CHECK(Run(m, f, 10.0f) == Say::None);
        CHECK(m.Current() == Phase::Settling);
    };
    never([](PlayMode::Frame& f) { f.paused = true; });
    never([](PlayMode::Frame& f) { f.loading = true; });
    never([](PlayMode::Frame& f) { f.player3D = false; });
    never([](PlayMode::Frame& f) { f.cellAttached = false; });
    never([](PlayMode::Frame& f) { f.controls = false; });
}

TEST_CASE("a scene that took the controls keeps the player until it lets go")
{
    PlayMode::Machine m;
    m.Load();
    auto scene = Ready();
    scene.controls = false;
    CHECK(Run(m, scene, 30.0f) == Say::None);
    CHECK(Run(m, Ready(), 1.0f) == Say::None);
    CHECK(Run(m, Ready(), 1.5f) == Say::HandOver);
}

TEST_CASE("the Helgen intro: nothing hands over until it's over, then it settles again")
{
    PlayMode::Machine m;
    m.Load();
    auto cart = Ready();
    cart.intro = true;
    cart.controls = false;  // the cart ride
    CHECK(Run(m, cart, 60.0f) == Say::None);
    CHECK(m.Current() == Phase::Intro);
    auto escape = Ready();
    escape.intro = true;  // running through the keep with the controls back
    CHECK(Run(m, escape, 300.0f) == Say::None);
    CHECK(m.Current() == Phase::Intro);
    CHECK(Run(m, Ready(), PlayMode::kSettleSeconds - 0.25f) == Say::None);  // out of the cave
    CHECK(m.Current() == Phase::Settling);
    CHECK(Run(m, Ready(), 0.5f) == Say::HandOver);
}

TEST_CASE("without MSC it says so once, then hands over once MSC has been in its game for kMscSeconds")
{
    PlayMode::Machine m;
    m.Load();
    auto none = Ready();
    none.linkUp = false;
    none.mscInGame = false;
    CHECK(Run(m, none, 5.0f) == Say::NoMsc);
    CHECK(m.Current() == Phase::Msc);
    CHECK(Run(m, none, 60.0f) == Say::None);  // said once

    auto menu = none;
    menu.linkUp = true;  // MSC started, on its menu
    CHECK(Run(m, menu, 30.0f) == Say::None);
    CHECK(Run(m, Ready(), PlayMode::kMscSeconds - 0.25f) == Say::None);
    CHECK(Run(m, Ready(), 0.5f) == Say::HandOver);
}

TEST_CASE("MSC linked but still loading says MscLoading")
{
    PlayMode::Machine m;
    m.Load();
    auto loading = Ready();
    loading.mscInGame = false;
    CHECK(Run(m, loading, 5.0f) == Say::MscLoading);
    CHECK(Run(m, loading, 30.0f) == Say::None);
    CHECK(Run(m, Ready(), PlayMode::kMscSeconds + 0.25f) == Say::HandOver);
}

TEST_CASE("MSC dropping out again while it's waiting for it restarts its moment")
{
    PlayMode::Machine m;
    m.Load();
    auto none = Ready();
    none.mscInGame = false;
    CHECK(Run(m, none, 5.0f) == Say::MscLoading);
    CHECK(Run(m, Ready(), PlayMode::kMscSeconds - 1.0f) == Say::None);
    CHECK(m.Tick(none) == Say::None);
    CHECK(Run(m, Ready(), PlayMode::kMscSeconds - 1.0f) == Say::None);
    CHECK(Run(m, Ready(), 1.5f) == Say::HandOver);
}

TEST_CASE("F10 cancels a handover still waiting")
{
    PlayMode::Machine m;
    m.Load();
    CHECK(Run(m, Ready(), 1.0f) == Say::None);
    m.Cancel();
    CHECK_FALSE(m.Waiting());
    CHECK(Run(m, Ready(), 10.0f) == Say::None);
}

TEST_CASE("the link lost while MSC had the player hands over again once it's back, without NoMsc")
{
    PlayMode::Machine m;
    m.Lost();
    auto none = Ready();
    none.linkUp = false;
    none.mscInGame = false;
    CHECK(Run(m, none, 30.0f) == Say::None);
    CHECK(m.Current() == Phase::Msc);
    CHECK(Run(m, Ready(), PlayMode::kMscSeconds + 0.25f) == Say::HandOver);
}

TEST_CASE("quitting to the main menu and loading another save starts over")
{
    PlayMode::Machine m;
    m.Load();
    CHECK(Run(m, Ready(), 3.0f) == Say::HandOver);
    m.Load();  // the next save
    CHECK(m.Waiting());
    CHECK(Run(m, Ready(), 3.0f) == Say::HandOver);

    // A load while still waiting for MSC says it again for the new save.
    auto none = Ready();
    none.linkUp = false;
    none.mscInGame = false;
    m.Load();
    CHECK(Run(m, none, 3.0f) == Say::NoMsc);
    m.Load();
    CHECK(Run(m, none, 3.0f) == Say::NoMsc);
}

TEST_CASE("a broken frame time counts as not ready")
{
    PlayMode::Machine m;
    m.Load();
    auto f = Ready();
    f.delta = -1.0f;
    CHECK(Run(m, Ready(), 1.9f) == Say::None);
    CHECK(m.Tick(f) == Say::None);
    CHECK(Run(m, Ready(), 1.9f) == Say::None);
}
