#pragma once

// Junk thrown from Skyrim's inventory (docs/PROTOCOL.md "Throwing junk", rules in protocol/cpp/junk.h).
// Once MSC's empty bottles are gone, key 3 takes a PocketChange of kPocketJunk: Skyrim picks the next
// piece (clutter, then fruit and vegetables, cheapest first) and sends MSC a JunkOut with its form,
// bounds and mass. The item stays in the inventory while it is out, set aside so it isn't picked or
// counted twice: a JunkBack that landed drops the real item where MSC's body came to rest, one that
// didn't (put away), the link going down, the handback or a load just make it available again.
// Main thread only.
namespace PerkeleRim::ThrowJunk
{
    void OnDataLoaded();  // VendorItemClutter, the produce, PerkeleRim.esp's pocket items to skip

    // Junk in the inventory that isn't out in MSC's hand (PocketCounts.counts[kPocketJunk]).
    std::uint32_t Count(RE::PlayerCharacter* a_player);

    // MSC took one out (PocketChange kPocketJunk, delta -1): the next piece goes to MSC as a JunkOut.
    void Take(RE::PlayerCharacter* a_player, std::uint32_t a_session, std::uint32_t a_seq);

    void OnBack(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);  // JunkBack

    // Everything out is the inventory's again: the link went down, MSC let go of the player, a load.
    void Release();
}
