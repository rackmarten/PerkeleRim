#pragma once

// Hands the Skyrim player to MSC and back (F10). While MSC owns the player:
//  - keyboard and mouse go to MSC as InputState, and Skyrim's own movement, look, sneak, jump,
//    attack and POV controls are off;
//  - every frame, the PlayerCharacter::Update hook places the player at the pose MSC reports.
// A save's first handover moves the player to the garage (Garage) first and parks the van there.
// Taking over sends a WorldspaceChange with the player's spot, which MSC uses as the anchor of its
// collision island, and starts CollisionSampler. A load door, fast travel or other jump sends a
// new anchor; poses for older anchors are ignored. Ownership ends on F10 again or when the link
// goes down.
namespace PerkeleRim::PlayerSync
{
    void Install();       // hook; call at SKSE load
    void OnDataLoaded();  // input and menu sinks; call on kDataLoaded
}
