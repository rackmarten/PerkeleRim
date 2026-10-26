#pragma once

// Hands the Skyrim player to MSC and back (F10). While MSC owns the player:
//  - keyboard and mouse go to MSC as InputState, and Skyrim's own movement, look, sneak, jump,
//    attack and POV controls are off;
//  - every frame, the PlayerCharacter::Update hook places the player at the pose MSC reports.
// A save's first handover wakes the player up in the shack next to the garage (Garage) and parks
// the van on the garage's pit; the first handover near the garage has MSC stock its shelves.
// Taking over sends a WorldspaceChange with the player's spot, which MSC uses as the anchor of its
// collision island, and starts CollisionSampler. A load door, fast travel or other jump sends a
// new anchor; poses for older anchors are ignored. Ownership ends on F10 again, when the link
// goes down or when a game loads; a save taken while MSC had the player hands over again after it loads.
namespace PerkeleRim::PlayerSync
{
    void Install();       // hook; call at SKSE load
    void OnDataLoaded();  // input and menu sinks; call on kDataLoaded
    void OnGameLoad();    // Skyrim takes the player back; call on kPreLoadGame and kNewGame

    bool Owned();            // MSC has the player
    bool Seated();           // ...and it sits in a vehicle
    void RestoreControls();  // the loaded save was taken while MSC had the player and kept its controls off
    void ResumeHandover();   // and hands the player to MSC again once it's in its cell (F10 cancels)

    // The hit test (debug/HitTest.h) at the keys: F10 unless MSC has the player or is about to (a save
    // taken while it had it resumes the handover by itself), a Unity KeyCode held for MSC on top of what
    // the devices hold (until let go or the release), and mouse counts as a mouse move brings them.
    void DebugHandOver();
    void DebugHold(std::uint16_t a_keyCode, bool a_held);
    void DebugMouse(std::int32_t a_dx, std::int32_t a_dy);
}
