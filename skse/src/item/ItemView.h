#pragma once

#include "link.h"

// Draws MSC's physics items in Skyrim from ItemState: one node per item, made from its model
// (ModelView) and hung straight into the scene graph like the van, so it has no reference and no
// collision. Nodes of items that went away are kept per model and reused. Main thread only.
namespace PerkeleRim::ItemView
{
    // Poses every item of a_state when it belongs to a_generation, clears them otherwise.
    void Update(const Protocol::ItemState* a_state, std::uint32_t a_generation);
    void Clear();

    // A copy of a Skyrim form's own model (thrown junk, a drink in the hand), without its collision:
    // it only shows. Null when the form has no model or it didn't load (logged once per form).
    RE::NiPointer<RE::NiNode> FormModel(RE::FormID a_form);
}
