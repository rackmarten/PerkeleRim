#pragma once

#include <optional>
#include <string>
#include <string_view>

#include "hit_test.h"

// PerkeleRim's attacks on Skyrim's actors with nobody at the keys: the hit test, run as the test kit's pr.hits scenario
// (skse/src/debug/TestKit.cpp, scenarios/hits.json, scripts/hit-test.ps1). Begin takes a list of protocol/cpp/hit_test.h's
// scenarios; it hands the player to MSC as F10 would (unless MSC has him), waits for MSC to finish sending its models
// and, scenario by scenario, sets MSC's drunk level and what is in Kumpo's hand over the link (DebugCommand, MSC's
// spike commands), levels MSC's camera with mouse moves, places a target (Skyrim.esm's Whiterun guard unless another
// base is asked for) just ahead with its AI off, and presses MSC's keys for it (H to punch, the right button to throw).
// Each attack is an AttackRow: its damage as Combat cast it and as the target's health took it, with a pass or fail by
// damage.h. Nothing is saved. Main thread only; inert until Begin.
namespace PerkeleRim::HitTest
{
    struct AttackRow
    {
        std::string name;    // the scenario's, or "drunk vs punch" for the check across them
        int         hit = 0;  // 1 on; 0 for the check across scenarios
        std::string target;
        Hit         result;
        float       expected = 0.0f;  // what damage.h says the attack does
        Verdict     verdict;
    };

    struct Result
    {
        bool        done = false;   // every scenario ran
        std::string stopped;        // why it ended early
        int         rows = 0;
        int         passed = 0;
    };

    // Empty when it starts, else why it can't (an unknown scenario, the player not in the world, a test running).
    std::string              Begin(std::string_view a_scenarios, std::uint32_t a_targetBase = 0);
    void                     Stop(const char* a_why);
    bool                     Running();
    std::optional<AttackRow> NextRow();  // the rows written since the last call, in order
    const Result&            Last();

    void OnGameLoaded();  // kPostLoadGame: a load in the middle stops it

    // Every player update, after PlayerSync's.
    void Update(RE::PlayerCharacter* a_player, float a_delta);
}
