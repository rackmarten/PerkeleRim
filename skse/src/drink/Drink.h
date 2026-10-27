#pragma once

// MSC's drunk level drives a blur with double vision (PerkeleRimDrunkIMAD in PerkeleRim.esp,
// tools/esp-gen); off without the ESP.
// Main thread only.
namespace PerkeleRim::Drink
{
    void OnDataLoaded();  // finds the ESP's blur; call on kDataLoaded

    // MSC's drunk level (PlayerState.needs[kNeedDrunk], 0..8) this frame, 0 when there is none.
    void SetDrunk(float a_level, float a_delta);
}
