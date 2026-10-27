#pragma once

#include "link.h"
#include "vehicle_hitbox.h"

// Vehicles are solid in Skyrim's own world (protocol/cpp/vehicle_hitbox.h has the shape and the rules): a few
// keyframed Havok capsules filling the box around MSC's model of each vehicle follow its VehicleState
// every frame. They are on L_ANIMSTATIC, which stops arrows, spells, flames, line of sight, actors
// walking into it and ragdolls; and in the player's own collision group, so the MSC-placed player walks
// through them. Main thread only.
namespace PerkeleRim::Hitbox
{
    // Vehicle a_vehicle (kVehicle* id, a_name for the log) is drawn at a_state this frame with its
    // model a_model: built once per model, in the player's cell's world while the vehicle stands or
    // crawls.
    void Show(std::uint32_t a_vehicle, std::string_view a_name, std::uint32_t a_model, const Protocol::VehicleState& a_state);
    // Out of the world: the vehicle isn't drawn.
    void Hide(std::uint32_t a_vehicle);

    // Where vehicle a_vehicle's hitbox is (its pose and box) while it is in Skyrim's world; false while out.
    bool Where(std::uint32_t a_vehicle, Protocol::VehicleState& a_state, Box& a_box);
    // Projectiles that have hit vehicle a_vehicle's hitbox since the game started (each logged, the first few in full),
    // counted only with HitboxTest=1 in PerkeleRim.ini.
    std::uint32_t Hits(std::uint32_t a_vehicle);
    // Whether a collision object is one of the vehicles' capsules (a projectile's impact names it).
    bool Owns(const RE::bhkCollisionObject* a_object);
    // ...or a Havok collidable is (a ray cast's hit names it).
    bool Owns(const RE::hkpCollidable* a_collidable);
}
