#pragma once

// Drinking across the link, from PerkeleRim.esp (tools/esp-gen):
//  - mead and ale the player picks up (PerkeleRimBeerDrinks) leave Skyrim's inventory and go to MSC
//    as beer (GiveBeer), while the link is up;
//  - MSC's drunk level drives a blur with double vision (PerkeleRimDrunkIMAD).
// Without the ESP loaded both stay off. Main thread only.
namespace PerkeleRim::Drink
{
    void OnDataLoaded();  // finds the ESP's records, watches the player's inventory; call on kDataLoaded

    // MSC's drunk level (PlayerState.needs[kNeedDrunk], 0..8) this frame, 0 when there is none.
    void SetDrunk(float a_level, float a_delta);
}
