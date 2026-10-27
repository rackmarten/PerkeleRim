#pragma once

#include "msc_menu.h"

// MSC's options menu used from Skyrim (F1 while MSC has the player; protocol/cpp/msc_menu.h has the
// open and close rules). A Skyrim menu of its own (Interface/PerkeleRim/MscMenu.swf, an empty movie)
// pauses Skyrim and shows its cursor, and draws MSC's menu full screen from the hand layer's
// texture, which MSC fills with its menu camera meanwhile (kHandLayerMenu). PlayerSync sends the
// cursor, the keys and the mouse buttons to MSC while it's up (Input::Builder::BuildMenu). F1 or
// Escape closes it, and so does MSC's Resume button. Main thread only.
namespace PerkeleRim::MscMenuView
{
    void OnDataLoaded();  // registers the menu

    // A key from PlayerSync's input sink (a_down: pressed this event, a_up: let go). True when it
    // was F1 or Escape for the menu, which then never reaches MSC.
    bool Key(std::uint32_t a_dik, bool a_down, bool a_up);

    // Skyrim's own menu keys (MenuOpenHandler) stay shut for this key: everything while the menu is
    // up, the Escape that closed it until it's let go, F1 while MSC has the player.
    bool HoldsKey(std::uint32_t a_dik);

    // Every frame (PlayerSync while unpaused, the menu itself while it's up).
    void Tick(float a_delta);

    bool Requested();  // InputState kInputMscMenu
    bool Shown();      // Skyrim's side is up: Skyrim's menus stay shut and the cursor goes to MSC

    // Skyrim's cursor as 0..1 of the screen, from the top left.
    void Cursor(float& a_x, float& a_y);
}
