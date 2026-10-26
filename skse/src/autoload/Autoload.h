#pragma once

// Test runs: with PerkeleRim_autoload.txt in Skyrim's folder, the main menu loads the newest save
// (or the one AutoloadSave=<name> in PerkeleRim.ini names) by itself, once per start, and answers the "mods disable achievements" box that load brings up
// (scripts/restart-games.ps1 then needs no clicks). Main thread only.
namespace PerkeleRim::Autoload
{
    void OnDataLoaded();  // watches for the main menu and message boxes; call on kDataLoaded
    void OnGameLoaded();  // the load finished; call on kPostLoadGame
}
