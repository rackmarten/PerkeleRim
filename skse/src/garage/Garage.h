#pragma once

// The garage near Whiterun from PerkeleRim.esp (tools/esp-gen): a spawn marker in front of it, a
// marker where the van parks inside (on the pit), the shack next to it with a wake-up marker by the
// bed, and the shelf spots MSC stocks. The first handover of every save wakes the player up in the
// shack (or, with an older ESP, starts in front of the garage), except a new game, whose Helgen skip
// already woke him at the spawn marker (the handover starts there), and a save still in the Helgen
// cart; the SKSE cosave remembers that it happened, and that the shelves got their stock. Without
// the ESP loaded everything here reports "no garage". Main thread only.
namespace PerkeleRim::Garage
{
    struct Spot
    {
        RE::NiPoint3 position;  // on the ground (the van's: on the pit)
        float        heading;   // radians, clockwise from north
    };

    // Cosave record (Cosave): whether this save has been to the garage yet and had its shelves stocked.
    void Save(SKSE::SerializationInterface* a_intfc);
    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version);  // false: not ours
    void Revert();

    void OnDataLoaded();  // finds the ESP's markers; call on kDataLoaded

    RE::TESObjectREFR* Spawn();       // the spawn marker in front of the garage, or null
    RE::TESObjectREFR* FirstVisit();  // the wake-up (else spawn) marker if this save hasn't been to the garage yet, else null
    void               Visited();     // the player arrived; later handovers start where it stands
    void               CartStart();   // the save is in the Helgen cart: its first handover starts where the player stands too
    void               SkippedHelgen();  // the Helgen skip moves him to the spawn marker: the first handover starts there, with the van
    bool               WakeUp(RE::TESObjectREFR* a_marker);  // the player is at a_marker: fades up from black if it's the shack's (true then)
    std::optional<Spot> Van();         // where the van parks

    // The shelves are stocked once per save, at a handover near the garage (the items need the
    // island's ground there).
    bool                     StockDue(const RE::NiPoint3& a_player);
    const std::vector<Spot>& ShelfSpots();
    void                     Stocked();
}
