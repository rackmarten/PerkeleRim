#pragma once

#include <array>
#include <string>

// Our own layer in Skyrim's HUD movie while MSC owns the player: a small dot at the screen center
// (Skyrim hides its crosshair then), MSC's look-at prompt just below it (its hand icon for things
// to use, and its text on a dark box so it reads in daylight), MSC's needs bars bottom left and its
// subtitle line above Skyrim's own subtitles. Main thread; the Scaleform work runs as an SKSE UI
// task.
namespace PerkeleRim::Hud
{
    struct View
    {
        bool                 show = false;      // false hides everything else too
        std::string          label;             // look-at text, empty = none
        bool                 hand = false;      // look-at hand icon
        std::string          subtitle;          // empty = none
        bool                 hasNeeds = false;  // needs below are MSC's
        std::array<float, 7> needs{};           // PlayerState.needs, kNeed* order
    };

    // Only queues work when something visible changed, or with a_refresh (rebuilds the layer if
    // the HUD lost it).
    void Set(const View& a_view, bool a_refresh = false);
}
