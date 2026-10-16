#pragma once

#include "link.h"

// Draws MSC's vehicles (the van, the moped, the Kekmet) in Skyrim from their VehicleStates: MSC's
// own model of each (ModelView) hung straight into the scene graph, so it has no reference and no
// collision (the collision sampler must not see it, and the player sits inside it), with every
// object of it (wheels, doors, controls, lights) posed and shown from VehicleParts. Main thread only.
namespace PerkeleRim::VehicleView
{
    // Shows vehicle a_vehicle (kVehicle* id) at a_state when it is valid for a_generation, hides it
    // otherwise. a_parts poses its objects when it belongs to the same vehicle, generation and model
    // (else they keep their pose).
    void Update(std::uint32_t a_vehicle, const Protocol::VehicleState* a_state, const Protocol::VehicleParts* a_parts, std::uint32_t a_generation);
    // Hides every vehicle.
    void Hide();
}
