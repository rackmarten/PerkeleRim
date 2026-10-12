#pragma once

// The garage near Whiterun from PerkeleRim.esp (tools/esp-gen): a spawn marker in front of it and
// a marker where the van parks inside. The first handover of every save starts there; the SKSE
// cosave remembers that it happened. Without the ESP loaded everything here reports "no garage".
// Main thread only.
namespace PerkeleRim::Garage
{
    struct Spot
    {
        RE::NiPoint3 position;  // on the ground
        float        heading;   // radians, clockwise from north
    };

    // Cosave record (Cosave): whether this save has been to the garage yet.
    void Save(SKSE::SerializationInterface* a_intfc);
    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version);  // false: not ours
    void Revert();

    void OnDataLoaded();  // finds the ESP's markers; call on kDataLoaded

    RE::TESObjectREFR* FirstVisit();  // the spawn marker if this save hasn't been to the garage yet, else null
    void               Visited();     // the player arrived; later handovers start where it stands
    std::optional<Spot> Van();         // where the van parks

    // True for the garage's own references. Its roof would be a solid block in MSC's top-down
    // collision heightfield, so the sampler looks through it.
    bool Owns(const RE::TESObjectREFR* a_ref);
}
