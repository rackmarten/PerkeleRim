#pragma once

#include <string>

// The pocket on Skyrim's side (docs/PROTOCOL.md "The pocket"): Skyrim's inventory holds MSC's
// beer, empty bottles, food and cigarettes as PerkeleRim.esp misc items (tools/esp-gen), so they
// travel through load doors and live in Skyrim's saves.
//  - MSC's PocketChange adds or removes them (Y pockets, keys 3 to 8 drink, eat or take out);
//  - the counts go back to MSC as PocketCounts when they change, whoever changed them;
//  - mead and ale the player picks up (PerkeleRimBeerDrinks) become MSC beer at once.
// Without the ESP loaded all of it stays off. Main thread only.
namespace PerkeleRim::Pocket
{
    void OnDataLoaded();  // finds the ESP's records, watches the player's inventory; call on kDataLoaded

    // Every frame, owned or not: sends the counts to MSC when they changed or the link came up.
    void Update(RE::PlayerCharacter* a_player, float a_delta);

    void OnChange(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);  // PocketChange

    // The HUD's slot hint: one line per kind the pocket has ("3  Beer  x3"), empty for none.
    std::string Hint();
}
