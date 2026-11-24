#pragma once

#include <filesystem>
#include <string_view>

// F11 while MSC has the player: a screenshot of both games with one name, <name>_skyrim.png (this
// game's next frame, BackBuffer) and <name>_msc.png (MSC's view, through a ScreenshotEvent), in
// Documents/My Games/PerkeleRim/Screenshots, or the folder a ScreenshotDir=<path> line names in
// Data/SKSE/Plugins/PerkeleRim.ini (a relative one is under the Skyrim folder).
namespace PerkeleRim::Shot
{
    void Take();  // main thread

    // Only MSC's view, as <folder>/<a_name>_msc.png (the folder F11 saves to): the test kit's pr.mscshot. The path it
    // asked for (an older file of that name removed first), empty when MSC isn't linked or the folder can't be made.
    std::filesystem::path AskMsc(std::string_view a_name);
}
