#pragma once

// A check, off unless PerkeleRim.ini asks for it: PerkeleRim's attacks on Skyrim's actors with nobody
// at the keys (scripts/hit-test.ps1). With [Debug] HitTest=all (or a list such as punch,bottle; the
// scenarios are protocol/cpp/hit_test.h's) it waits for the save Autoload loads, hands the player to
// MSC as F10 would and, scenario by scenario, sets MSC's drunk level and what is in Kumpo's hand over
// the link (DebugCommand, MSC's spike commands), levels MSC's camera with mouse moves, places a target
// (HitTestTarget, Skyrim.esm's LvlGiant by default) just ahead with its AI off, and presses MSC's keys
// for it (H to punch, the right button to throw). Each attack's damage, as Combat cast it and as the
// target's health took it, goes to a CSV (HitTestCsv, else Documents/My Games/PerkeleRim/hittest.csv)
// with a pass or fail by damage.h. Nothing is saved. Main thread only.
namespace PerkeleRim::HitTest
{
    void OnDataLoaded();
    void OnGameLoaded();  // kPostLoadGame

    // Every player update, after PlayerSync's.
    void Update(RE::PlayerCharacter* a_player, float a_delta);
}
