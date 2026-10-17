#pragma once

#include "link.h"

// Draws MSC's smoke in Skyrim from ParticleState: one square per particle, made from its
// particle model (ModelView, one alpha-blended square of the texture), turned to Skyrim's camera
// every frame and hung straight into the scene graph like the items. Squares are kept per model
// and reused; the ones not needed this frame are culled. Main thread only.
namespace PerkeleRim::ParticleView
{
    // Poses every particle of a_state when it belongs to a_generation, hides them otherwise.
    // a_light is Skyrim's light at the player (HandView::Light), MSC's colors are for daylight.
    void Update(const Protocol::ParticleState* a_state, std::uint32_t a_generation, const std::array<float, 3>& a_light);
    void Hide();
}
