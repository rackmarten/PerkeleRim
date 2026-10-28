#pragma once

#include "link.h"

// The Skyrim drink Kumpo holds while MSC shows its drinking (HeldDrink, docs/PROTOCOL.md "Food and
// drink", placement in protocol/cpp/held_drink.h): the item's own model (ItemView::FormModel) on
// MSC's bottle, posed from Skyrim's camera every frame. It hangs in the first-person scene, which
// Skyrim draws after the world with its own near plane, so the bottle at Kumpo's mouth isn't cut
// off and never pokes into a wall. MSC's hand layer goes over it, with a hole where MSC's bottle is.
// Main thread only.
namespace PerkeleRim::HeldDrinkView
{
    void OnMessage(const std::uint8_t* a_data, std::uint32_t a_size);  // HeldDrink

    // Every frame while MSC has the player, after the camera was posed. a_shown: the camera sits at
    // MSC's first-person one and no Skyrim menu is up.
    void Update(RE::PlayerCharacter* a_player, bool a_shown, float a_delta);

    // Out of the hand: the handback, the link down, a load.
    void Clear();
}
