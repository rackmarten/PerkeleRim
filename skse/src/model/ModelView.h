#pragma once

#include <cstdint>

// Builds MSC's own models in memory from ModelChunk messages (meshes and textures read from the
// running MSC, nothing converted or written to disk) and shows them. Spike: each model is put down
// once in front of the player. Main thread only.
namespace PerkeleRim::ModelView
{
    void OnChunk(const std::uint8_t* a_payload, std::uint32_t a_size);
    void Tick(float a_delta);  // every frame the player is handed over; logs the frame cost around a build
}
