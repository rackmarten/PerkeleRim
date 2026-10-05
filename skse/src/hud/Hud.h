#pragma once

#include <string>

// Our own layer in Skyrim's HUD movie: a small dot at the screen center (Skyrim hides its crosshair
// while MSC owns the player) and MSC's look-at prompt just below it (its hand icon for things to
// use, and its text on a dark box so it reads in daylight). Main thread; the Scaleform work runs as
// an SKSE UI task.
namespace PerkeleRim::Hud
{
    // Shows the dot, a_label (empty = no label) and, with a_hand, the hand icon while a_show; hides
    // everything otherwise. Only queues work when something changed, or with a_refresh (rebuilds
    // the layer if the HUD lost it).
    void Set(bool a_show, const std::string& a_label, bool a_hand, bool a_refresh = false);
}
