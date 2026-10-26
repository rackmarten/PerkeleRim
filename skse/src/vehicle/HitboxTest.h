#pragma once

// The hitbox test (scripts/hitbox-test.ps1): with PerkeleRim_hitboxtest.txt in Skyrim's folder, the
// first time MSC has the player with the Hayosiko van solid near him, a bandit archer is placed behind
// the van (seen from Kumpo) and a melee bandit just behind it. For a minute it logs the arrows that hit
// the van (Hitbox::Hits), how close the melee bandit comes to Kumpo and how deep he ever stands inside
// the van's box, keeps Kumpo alive, then takes both bandits away and logs "hitbox test: done". Main
// thread only.
namespace PerkeleRim::HitboxTest
{
    // Every frame MSC has the player, after the vehicles are drawn.
    void Update(RE::PlayerCharacter* a_player, float a_delta);
}
