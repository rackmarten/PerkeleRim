#pragma once

#include "link.h"

// Skyrim's people around Kumpo's vehicles (the rules are in protocol/cpp/bystanders.h and damage.h):
//  - nobody starts a dialogue with him while he sits in a vehicle or has climbed into one
//    (Bystanders::MayTalk): Actor::SetDialogueWithPlayer refuses, so neither E, someone walking up to
//    him nor a force-greet opens the dialogue menu that would take the controls mid-drive;
//  - civilians (Bystanders::Flees) in the driven vehicle's way (Damage::InVehicleWay, further ahead the
//    faster it goes) run from it: a flee package outside combat, no crime, no aggro. It's let go
//    (EvaluatePackage) a few seconds after the vehicle stops coming at them.
// Main thread, but the dialogue hook may run on Skyrim's AI threads.
namespace PerkeleRim::People
{
    void Install();  // the dialogue hook; call once, with the plugin's other vtable hooks

    // Every frame while MSC has the player: the vehicle id it sits in or has climbed into, 0 on foot.
    void SetInVehicle(std::uint32_t a_vehicle);

    // Every frame while MSC has the player: the vehicle it drives (nullptr on foot).
    void Update(RE::PlayerCharacter* a_player, const Protocol::VehicleState* a_driven);

    // a_actor runs from the player (in its vehicle) if it's a civilian; a_why for the log. True when
    // a flee started or started over.
    bool Flee(RE::PlayerCharacter* a_player, RE::Actor* a_actor, const char* a_why);

    // MSC lets go of the player: everyone fleeing is let go, anyone may talk again.
    void Stop();
}
