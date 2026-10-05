#pragma once

#include "link.h"

// Draws MSC's van in Skyrim from VehicleState. For now a stand-in: a vanilla cart mesh hung straight
// into the scene graph, so it has no reference and no collision (the collision sampler must not see
// it, and the player sits inside it). Main thread only.
namespace PerkeleRim::VehicleView
{
    // Shows the vehicle at a_state when it is valid for a_generation, hides it otherwise.
    void Update(const Protocol::VehicleState* a_state, std::uint32_t a_generation);
    void Hide();
}
