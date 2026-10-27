#pragma once

#include <array>
#include <string>

// Our own layer in Skyrim's HUD movie while MSC owns the player: MSC's first-person hands under
// everything else in the movie, a small dot at the screen center
// (Skyrim hides its crosshair then) with a throw's charge as a bar above it, MSC's look-at prompt just below it (its hand icon for things
// to use, and its text on a dark box so it reads in daylight), MSC's needs bars top left (with room above for Skyrim's own HUD messages), its subtitle line above Skyrim's own subtitles, the gear bottom right while driving and the fuel line above it. Main thread; the Scaleform work runs as an SKSE UI
// task.
namespace PerkeleRim::Hud
{
    struct View
    {
        bool                 show = false;      // false hides everything else too
        bool                 dot = true;        // the dot at the screen center (off for the intro's captions)
        bool                 hideStealth = false;  // Skyrim's sneak eye hidden (every frame while true; shown again once it is false): it stayed up after standing
        std::string          label;             // look-at text, empty = none
        bool                 hand = false;      // look-at hand icon
        std::string          subtitle;          // empty = none
        std::string          gear;              // MSC's gear indicator while driving ("N", "3 HIGH"), empty = none
        std::string          fuel;              // MSC's fuel line above the gear (FuelText), empty = none
        std::int64_t         money = -1;        // Kumpo's money (Skyrim's gold, MSC's PlayerMoney) under the needs, -1 = none
        bool                 tools = false;     // MSC's tools are out: its wrench and screwdriver icon, top right
        float                charge = -1.0f;    // a throw being charged, 0..1 of a full charge; < 0 none
        bool                 hasNeeds = false;  // needs below are MSC's
        std::array<float, 7> needs{};           // PlayerState.needs, kNeed* order
        std::string          handsImage;        // MSC's first-person layer (HandView), full screen under the rest; empty = none
        std::uint32_t        handsWidth = 0, handsHeight = 0;  // its size in pixels
        std::array<float, 3> handsLight{ 1.0f, 1.0f, 1.0f };   // color multiplier for it (HandView::Light)
    };

    // Only queues work when something visible changed, or with a_refresh (rebuilds the layer if
    // the HUD lost it).
    void Set(const View& a_view, bool a_refresh = false);

    // Kumpo's shake (Needs::Shake, 0..1) and the hands' offset now (Needs::ShakeOffset for a screen 1
    // tall, so a share of the screen height). The layer grows a little while it shakes so its edges
    // stay off screen. Queues nothing while the hands are steady.
    void ShakeHands(float a_shake, float a_x, float a_y);
}
