#pragma once

#include <cstdint>

// Builds MSC's own models in memory from ModelChunk messages (meshes and textures read from the
// running MSC, nothing converted or written to disk) and keeps them by MSC's model id. ItemView
// asks for a fresh node of a model per item. Main thread only.
namespace PerkeleRim::ModelView
{
    void OnChunk(const std::uint8_t* a_payload, std::uint32_t a_size);

    // A new node drawing model a_model (its root at the node's origin), not attached anywhere yet;
    // null until that model has arrived. Nodes are never destroyed: the game's destructors expect
    // geometry it built itself, so the caller keeps and reuses them.
    RE::NiPointer<RE::NiNode> Instance(std::uint32_t a_model);

    void Tick(float a_delta);  // every frame the player is handed over; logs the frame cost around a build
}
