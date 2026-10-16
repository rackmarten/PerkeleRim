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

    // A save loaded: Skyrim's saves keep globals, so one taken while MSC had the player would load with
    // the player's shout voice still muted. Shouts mutes it again if the handover resumes.
    void OnGameLoaded();

    // Every frame MSC has the player: writes the ActorList for the current anchor.
    void Update(RE::PlayerCharacter* a_player, std::uint32_t a_generation, float a_delta);

    // A PunchEvent message. a_drunk: MSC's drunk level (0..8), drunk punches hit harder.
    void OnPunch(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size, float a_drunk);

    // An ImpactEvent message: a thrown item (hits like a punch, by its speed), the piss stream
    // (PerkeleRim.esp's poison) or the driven vehicle (knocked over, damage by speed) reached an
    // actor.
    void OnImpact(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);

    // Every frame MSC has the player: Skyrim's shouting (Z) works as in vanilla, aimed along MSC's
    // camera, but PerkeleRim.esp's shout lines aren't the player's while its PerkeleRimMuteShouts
    // global is 1, so Skyrim's shout voice is muted; each shout released goes to MSC (PlayerShout),
    // which yells instead.
    void Shouts(RE::PlayerCharacter* a_player);

    // Every frame MSC has the player: keeps its hands drawn (Skyrim only blocks with them out; fists
    // unless something is equipped) and puts it in Skyrim's own block while a_block (MSC's guard,
    // kPlayerBlocking), so the Block skill, stagger and power attacks work as in vanilla. a_draw
    // false (a menu such as dialogue has the input) leaves sheathed hands sheathed. Skyrim's
    // first-person model is hidden meanwhile: MSC's hands are the ones on screen.
    void Guard(RE::PlayerCharacter* a_player, bool a_block, bool a_draw);

    // Skyrim has the player back: MSC's capsules go, the block ends, hands this drew are sheathed, the
    // first-person model shows again and Skyrim's shout voice is the player's again.
    void Stop();
}
