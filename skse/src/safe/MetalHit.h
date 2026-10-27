#pragma once

// Hits on the player in a safe seat (Safe::Seat) land on the vehicle instead: Skyrim's player stands
// at full height where MSC seats it, so its head pokes out of the roof and enemies keep swinging and
// shooting at it. A melee hit is dropped before Skyrim builds it (no grunt, no screen blood or shake,
// no hit reaction), and an arrow or a spell bolt that reaches the player is killed before it lands.
// Either plays the attack's own impact on metal (blade on metal, arrow on metal, the spell's) on the
// vehicle's hitbox, where the hit came from, and counts in Hitbox::Hits. Main thread.
namespace PerkeleRim::MetalHit
{
    void Install();  // the hooks; needs the trampoline SKSE::Init allocates in main.cpp
}
