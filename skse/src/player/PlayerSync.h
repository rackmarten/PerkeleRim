#pragma once

// Hands the Skyrim player to MSC and back. Play mode (protocol/cpp/play_mode.h) does it by itself
// after every load, once the game has settled, the Helgen intro is over and MSC is in its game, and
// says so when MSC isn't there; F10 is a debug toggle (hand over, take back, cancel a handover still
// waiting). While MSC owns the player:
//  - keyboard and mouse go to MSC as InputState, and Skyrim's own movement, look, sneak, jump,
//    attack and POV controls are off;
//  - every frame, the PlayerCharacter::Update hook places the player at the pose MSC reports.
// A save's first handover wakes the player up in the shack next to the garage (Garage) and parks
// the van on the garage's pit. A new game skips Helgen (HelgenSkip) and fades Kumpo in at the
// garage's spawn marker, so its first handover starts there, with the van on the pit too; a save
// still running Unbound (Helgen skipped by hand or by another mod) hands over where Kumpo stands.
// The first handover near the garage has MSC stock its shelves.
// Taking over sends a WorldspaceChange with the player's spot, which MSC uses as the anchor of its
// collision island, and starts CollisionSampler. A load door, fast travel or other jump sends a
// new anchor; poses for older anchors are ignored. Ownership ends on F10 again, when the link
// goes down (play mode hands over again once it's back) or when a game loads.
namespace PerkeleRim::PlayerSync
{
    void Install();       // hook; call at SKSE load
    void OnDataLoaded();  // input and menu sinks; call on kDataLoaded
    void OnGameLoad();    // Skyrim takes the player back; call on kPreLoadGame and kNewGame
    void OnGameLoaded();  // play mode hands over once the game has settled; call on kPostLoadGame and kNewGame

    bool Owned();            // MSC has the player
    bool Seated();           // ...and it sits in a vehicle
    void RestoreControls();  // the loaded save was taken while MSC had the player and kept its controls off
    // The hit test (debug/HitTest.h) at the keys: F10 unless MSC has the player or is about to (play mode
    // hands over by itself after a load), a Unity KeyCode held for MSC on top of what
    // the devices hold (until let go or the release), and mouse counts as a mouse move brings them.
    void DebugHandOver();
    void DebugHold(std::uint16_t a_keyCode, bool a_held);
    void DebugMouse(std::int32_t a_dx, std::int32_t a_dy);
}
