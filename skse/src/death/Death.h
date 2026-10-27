#pragma once

#include "link.h"

// The player's health while MSC has it. Skyrim's health is the only one: what MSC would have died
// of arrives as PlayerDamage and costs a share of it (protocol/cpp/damage.h), and falls MSC's
// player takes cost Skyrim's own fall damage, which doesn't run while MSC places the player. What
// MSC's player drinks, smokes and eats (Consumed) heals a share of it back. When
// the player dies, PlayerSync takes it back from MSC and Show puts MSC's blood over the view (its
// own texture, sent as the "PlayerBlood" model) while Skyrim's normal death and reload run.
// Main thread only.
namespace PerkeleRim::Death
{
    // A PlayerDamage message.
    void OnDamage(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);

    // A Consumed message.
    void OnConsumed(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);

    // Starving or dehydrated (NeedEffects): a_amount health off now, quietly; enough of it kills.
    void Starve(RE::PlayerCharacter* a_player, float a_amount);

    // Someone hit the player while MSC has it (Skyrim's hit event; the damage is Skyrim's own).
    void OnHit(RE::PlayerCharacter* a_player, bool a_blocked);

    // MSC just got the player.
    void OnHandover();

    // Every frame MSC has the player: its pose when there is a valid one for the current anchor
    // (else null) and whether it sits in a vehicle. Lands falls, notices health dropping low.
    void OnPose(RE::PlayerCharacter* a_player, const Protocol::PlayerState* a_pose, bool a_seated);

    // A hit that would kill came in: PlayerSync takes the player back, then calls Kill.
    bool Lethal();
    void Kill(RE::PlayerCharacter* a_player);

    // MSC's blood over the view, until the next handover or a game load.
    void Show();
    void Hide();

    // Every frame from the player's Update: keeps the blood in front of the camera.
    void Tick();

}
