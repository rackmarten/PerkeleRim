#pragma once

#include <array>
#include <string>

// Our own layer in Skyrim's HUD movie while MSC owns the player: MSC's first-person hands under
// everything else in the movie, a small dot at the screen center
// (Skyrim hides its crosshair then), MSC's look-at prompt just below it (its hand icon for things
// to use, and its text on a dark box so it reads in daylight), MSC's needs bars bottom left (above Skyrim's own health meter) with the
// pocket's slots next to them, and its subtitle line above Skyrim's own subtitles. Main thread; the Scaleform work runs as an SKSE UI
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
        std::string          pocket;            // the pocket's slots (Pocket::Hint), right of the needs bars; empty = none
        std::string          handsImage;        // MSC's first-person layer (HandView), full screen under the rest; empty = none
        std::uint32_t        handsWidth = 0, handsHeight = 0;  // its size in pixels
        std::array<float, 3> handsLight{ 1.0f, 1.0f, 1.0f };   // color multiplier for it (HandView::Light)
    };

    // Only queues work when something visible changed, or with a_refresh (rebuilds the layer if
    // the HUD lost it).
    void Set(const View& a_view, bool a_refresh = false);
}
