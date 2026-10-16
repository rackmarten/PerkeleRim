#pragma once

// F11 while MSC has the player: a screenshot of both games with one name, <name>_skyrim.png (this
// game's next frame, BackBuffer) and <name>_msc.png (MSC's view, through a ScreenshotEvent), in
// Documents/My Games/PerkeleRim/Screenshots, or the folder a ScreenshotDir=<path> line names in
// Data/SKSE/Plugins/PerkeleRim.ini (a relative one is under the Skyrim folder).
namespace PerkeleRim::Shot
{
    void Take();  // main thread
}
