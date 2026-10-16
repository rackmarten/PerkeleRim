#pragma once

#include "link.h"

// Draws MSC's van in Skyrim from VehicleState: MSC's own model of it (ModelView) hung straight
// into the scene graph, so it has no reference and no collision (the collision sampler must not see
// it, and the player sits inside it), with every object of it (wheels, doors, controls, lights)
// posed and shown from VehicleParts. Main thread only.
namespace PerkeleRim::VehicleView
{
    // Shows the vehicle at a_state when it is valid for a_generation, hides it otherwise. a_parts
    // poses its objects when it belongs to the same vehicle, generation and model (else they keep
    // their pose).
    void Update(const Protocol::VehicleState* a_state, const Protocol::VehicleParts* a_parts, std::uint32_t a_generation);
    void Hide();
}
