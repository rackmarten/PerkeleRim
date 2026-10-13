#pragma once

#include "link.h"

// Fighting while MSC has the player. Skyrim's actors near the player go to MSC as capsules
// (ActorList) so MSC's player bumps into them, and MSC's punches that land on one (PunchEvent) hit
// it as the player: PerkeleRim.esp's punch spells, a share of its max health (protocol/cpp/damage.h)
// and a stagger. Being the player's hostile spells, the target fights back and an attack on someone
// peaceful is assault. Also logs who attacks and hits the player, to see why enemies don't.
// Main thread only.
namespace PerkeleRim::Combat
{
    void OnDataLoaded();

    // Every frame MSC has the player: writes the ActorList for the current anchor.
    void Update(RE::PlayerCharacter* a_player, std::uint32_t a_generation, float a_delta);

    // A PunchEvent message. a_drunk: MSC's drunk level (0..8), drunk punches hit harder.
    void OnPunch(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size, float a_drunk);

    // Skyrim has the player back: MSC's capsules go.
    void Stop();
}
