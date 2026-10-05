#pragma once

#include "link.h"

// Draws MSC's van in Skyrim from VehicleState: the mesh converted from MSC (or a vanilla cart
// stand-in) hung straight into the scene graph, so it has no reference and no collision (the
// collision sampler must not see it, and the player sits inside it), with its wheels steered and
// spun. Main thread only.
namespace PerkeleRim::VehicleView
{
    // Shows the vehicle at a_state when it is valid for a_generation, hides it otherwise.
    void Update(const Protocol::VehicleState* a_state, std::uint32_t a_generation);
    void Hide();
}
