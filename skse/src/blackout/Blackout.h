#pragma once

// A blackout while MSC has the player (rules in protocol/cpp/blackout_wake.h): MSC's Blackout fades
// Skyrim to black, Kumpo is moved to a random map marker in Tamriel on open, peaceful ground (a
// teleport, so PlayerSync sends MSC a new anchor there), the clock moves on by the blackout's
// hours, and once MSC has put him down there Skyrim fades back in, sends TimePassed with
// kTimeBed | kTimeBlackout (needs, clock, sobering and hangover on MSC's side) and plays his
// hangover line (Intro::WakeUp). Main thread only.
namespace PerkeleRim::Blackout
{
    void OnDataLoaded();  // the fade's image space modifiers; call on kDataLoaded
    void OnGameLoaded();  // a load ends a blackout; call on kPostLoadGame and kNewGame

    // MSC's Blackout message (PlayerSync reads MSC's messages only while MSC has the player).
    void OnMessage(const std::uint8_t* a_data, std::uint32_t a_size);

    // Once a frame from the player's update.
    void Tick(RE::PlayerCharacter* a_player, float a_delta);
}
