// The parts attached to a vehicle built from parts (the Satsuma, PartSet): what Skyrim has to change
// of its drawing when a new PartSet comes in. Skyrim keeps one node per attached part, built from
// that part's own model; a part that went on is built, one that came off goes back, one whose
// look changed (another model) is built anew, and every other part stays as it is, so a part going
// on redraws only that part. A part with model 0 (not read yet) has nothing to draw.
// Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <cstdint>
#include <unordered_map>
#include <unordered_set>
#include <vector>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::PartSets
{
    // What Skyrim has built of a vehicle's parts: part id -> the model its node was built from.
    using Built = std::unordered_map<std::uint32_t, std::uint32_t>;

    struct Changes
    {
        std::vector<std::uint32_t> gone;   // built parts to take down: no longer in the set, or nothing to draw now
        std::vector<std::uint32_t> build;  // indexes into PartSet.parts of parts to build: new, or another model than built
    };

    // Whether a_set is the parts of a_vehicle on the island a_generation is for.
    inline bool For(const Protocol::PartSet& a_set, std::uint32_t a_vehicle, std::uint32_t a_generation)
    {
        return a_set.vehicle == a_vehicle && a_set.generation == a_generation;
    }

    // What changed from a_built to a_set. A part named twice counts once (its first entry).
    inline Changes Diff(const Built& a_built, const Protocol::PartSet& a_set)
    {
        Changes                           changes;
        std::unordered_set<std::uint32_t> drawn;
        const std::uint32_t count = a_set.count < Protocol::kPartSetCapacity ? a_set.count : Protocol::kPartSetCapacity;
        for (std::uint32_t i = 0; i < count; i++) {
            const auto& part = a_set.parts[i];
            if (part.part == 0 || part.model == 0 || !drawn.insert(part.part).second) {
                continue;
            }
            const auto it = a_built.find(part.part);
            if (it == a_built.end() || it->second != part.model) {
                changes.build.push_back(i);
            }
        }
        for (const auto& [part, model] : a_built) {
            if (!drawn.contains(part)) {
                changes.gone.push_back(part);
            }
        }
        return changes;
    }
}
