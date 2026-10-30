#pragma once

// The hitbox test (scripts/hitbox-test.ps1): with HitboxTest=1 in PerkeleRim.ini, the first time MSC
// has the player with the Hayosiko van standing solid near him for a few seconds (the script calls it
// with ]), a melee bandit is placed behind the van (seen from Kumpo) and two archers with their AI
// off, the shooters of 12 arrows the test fires itself at Kumpo: 8 from just behind the van, through
// it (each also cast as a ray first, logging what it meets), and 4 from his side as a control. For a
// minute it logs every projectile impact near the van (one of its capsules, Kumpo or anything else,
// and the layer), how close the melee bandit comes to Kumpo and how deep he ever stands inside the
// van's box, and the navmesh obstacles of the loaded cells; it keeps Kumpo alive, then takes the
// bandits away and logs "hitbox test: done: ...". Main thread only.
namespace PerkeleRim::HitboxTest
{
    // Every frame MSC has the player, after the vehicles are drawn.
    void Update(RE::PlayerCharacter* a_player, float a_delta);
}
