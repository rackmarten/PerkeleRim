// Skyrim's favorites hotkeys as Kumpo's hotbar while MSC has the player (docs/PROTOCOL.md "The
// hotbar"). Number keys 1 to 8 do what the player bound to them in Skyrim's Favorites menu:
//  - PerkeleRim.esp's three powers become MSC's own keys: Empty Hands 1 (MSC's hand), Tools 2 (its
//    spanner), Throw Junk the pocket's 3 (an empty bottle into the hand, then junk, junk.h);
//  - MSC Beer and MSC's food become the pocket's 4 to 8: MSC's own drinking and eating;
//  - other food and drink (and potions) are used in Skyrim, which tells MSC what they did (food.h);
//  - an unbound number, or one bound to anything else (a sword, a spell), does nothing: no hidden
//    keys. Instead the first time PerkeleRim meets a save its defaults (DefaultOf) are bound in
//    Skyrim's own favorites, on the numbers still free, so the Favorites menu shows them.
// Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <cstdint>

namespace PerkeleRim::Hotbar
{
    inline constexpr int kSlots = 8;  // Skyrim's hotkeys 1 to 8
    inline constexpr std::uint16_t kAlpha1 = 49;  // Unity KeyCode.Alpha1

    // What a hotkey holds.
    enum class Bound : std::uint8_t
    {
        None,        // nothing bound
        Other,       // something PerkeleRim leaves alone
        Hands,       // the Empty Hands power
        Tools,       // the Tools power
        Bottle,      // the Throw Junk power
        Beer,        // MSC Beer
        Sausages,    // MSC's food, in the pocket's order
        Pizza,
        Chips,
        Macaron,
        Consumable,  // any other food, drink or potion
    };

    // What a number is bound to the first time PerkeleRim meets a save: 1 Empty Hands, 2 Tools, 3 Throw
    // Junk, 4 MSC Beer, 5..8 MSC's food.
    constexpr Bound DefaultOf(int a_slot)
    {
        constexpr Bound kDefaults[kSlots] = { Bound::Hands, Bound::Tools, Bound::Bottle, Bound::Beer, Bound::Sausages, Bound::Pizza, Bound::Chips, Bound::Macaron };
        return a_slot >= 0 && a_slot < kSlots ? kDefaults[a_slot] : Bound::None;
    }

    struct Action
    {
        std::uint16_t mscKey = 0;  // the Unity KeyCode MSC gets held while the number is, 0 for none
        bool          use = false;  // use the bound item in Skyrim (eat, drink)
    };

    // Number key a_slot (0 for 1 .. 7 for 8) with a_bound on it.
    constexpr Action Resolve(int a_slot, Bound a_bound)
    {
        if (a_slot < 0 || a_slot >= kSlots) {
            return {};
        }
        switch (a_bound) {
        case Bound::Hands: return { kAlpha1 };
        case Bound::Tools: return { kAlpha1 + 1 };
        case Bound::Bottle: return { kAlpha1 + 2 };
        case Bound::Beer: return { kAlpha1 + 3 };
        case Bound::Sausages: return { kAlpha1 + 4 };
        case Bound::Pizza: return { kAlpha1 + 5 };
        case Bound::Chips: return { kAlpha1 + 6 };
        case Bound::Macaron: return { kAlpha1 + 7 };
        case Bound::Consumable: return { 0, true };
        default: return {};
        }
    }
}
