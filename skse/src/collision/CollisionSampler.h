#pragma once

// Samples Skyrim's collision around the player while MSC owns it and streams it to MSC as
// CollisionChunk messages. Each chunk is the land as a heightfield from top-down Havok raycasts
// that pass through everything but the landscape, plus the statics' real collision triangles
// (StaticMeshes), so buildings keep their doorways and bridges their undersides. Chunks nearest
// the player go first, then those around a point ahead of it while it moves fast (driving),
// within a small per-frame time budget; unfinished chunks (cells or 3D still loading) are
// sampled again.
// Main thread only.
namespace PerkeleRim::CollisionSampler
{
    void Reset(std::uint32_t a_generation, std::uint32_t a_worldspace);  // new anchor: start over
    void Stop();                                                         // Skyrim took the player back
    void Update(RE::PlayerCharacter* a_player, const RE::NiPoint3& a_velocity);  // every frame while MSC owns the player; velocity in units/s
    void OnDataLoaded();  // a door opening or closing reads the chunks around it again once it has swung
}
