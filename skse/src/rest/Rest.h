#pragma once

// Waiting and sleeping in Skyrim, and loading, as MSC's player feels them. T opens Skyrim's Wait
// menu while MSC has the player; the hours the Wait or Sleep menu then passes are sent to MSC as
// TimePassed (its clock and needs move on, msc/src/Link/Rest.cs), flagged when the player slept in a
// bed. Any Skyrim load sends LoadReset first thing, so MSC stops what its player was doing (piss,
// smoke, drink, held items, push, finger, crouch, zoom) before control comes back.
// Main thread only.
namespace PerkeleRim::Rest
{
    void OnDataLoaded();                           // registers for the Wait and Sleep menu
    void OnFrame(RE::PlayerCharacter* a_player, float a_delta);  // every unpaused frame while MSC has the player
    void OnGameLoad();                             // kPreLoadGame, kNewGame
}
