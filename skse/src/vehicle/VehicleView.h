#pragma once

#include "link.h"

// Draws MSC's vehicles (the van, the moped, the Kekmet, the Gifu, the Satsuma's body and its parts) in Skyrim from their VehicleStates: MSC's
// own model of each (ModelView) hung straight into the scene graph, so it has no reference and no
// collision of its own (the collision sampler must not see it), with every object of it (wheels,
// doors, controls, lights) posed and shown from VehicleParts. Hitbox gives it a solid body in
// Skyrim's world while it is shown. Main thread only.
namespace PerkeleRim::VehicleView
{
    // Shows vehicle a_vehicle (kVehicle* id) at a_state when it is valid for a_generation, hides it
    // otherwise. a_parts poses its objects when it belongs to the same vehicle, generation and model
    // (else they keep their pose).
    void Update(std::uint32_t a_vehicle, const Protocol::VehicleState* a_state, const Protocol::VehicleParts* a_parts, std::uint32_t a_generation);
    // The parts attached to a vehicle built from parts (the Satsuma) from a_set, when it is for that
    // vehicle on a_generation's island: each part a node of its own model under the vehicle's node,
    // posed relative to its body; only parts that went on, came off or changed their look are
    // built or taken down (protocol/cpp/part_set.h). They show and hide with the vehicle.
    void UpdateParts(const Protocol::PartSet& a_set, std::uint32_t a_generation);
    // How many parts of vehicle a_vehicle are drawn now (built and visible), for the test kit.
    std::uint32_t PartsDrawn(std::uint32_t a_vehicle);
    // Hides every vehicle.
    void Hide();
    // The node vehicle a_vehicle is drawn under while it is shown (its lamps' lights hang under it too), else null:
    // for the test kit's pr.vehicle target.
    RE::NiNode* Node(std::uint32_t a_vehicle);
}
