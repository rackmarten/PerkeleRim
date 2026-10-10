#pragma once

#include "link.h"

// MSC's first-person layer (HandLayer: its hands, drinks and tools in hand) over Skyrim's world.
// Opens the texture MSC shares on the GPU, wraps it for Scaleform and registers it with the
// game's image loader; the HUD (Hud::View.handsImage) draws it full screen under its own layer.
// MSC redraws the texture in place, so nothing is copied here per frame. Main thread only.
namespace PerkeleRim::HandView
{
    // Every frame the player is handed over; null when the slot couldn't be read.
    void Update(const Protocol::HandLayer* a_layer, float a_delta);

    // The image for the HUD ("img://" + this), empty while there's nothing to draw.
    const std::string& Image();
    std::uint32_t      Width();
    std::uint32_t      Height();

    void Hide();
}
