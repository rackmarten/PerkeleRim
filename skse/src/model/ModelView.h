#pragma once

#include <cstdint>
#include <vector>

#include "model.h"

// Builds MSC's own models in memory from ModelChunk messages (meshes and textures read from the
// running MSC, nothing converted or written to disk) and keeps them by MSC's model id. ItemView
// asks for a fresh node of a model per item, VehicleView for each vehicle. Main thread only.
namespace PerkeleRim::ModelView
{
    void OnChunk(const std::uint8_t* a_payload, std::uint32_t a_size);

    // A new node drawing model a_model (its root at the node's origin), not attached anywhere yet;
    // null until that model has arrived. The model's own nodes (ModelNode) are child nodes by their
    // names, at their rest pose, holding their meshes; a_nodes (optional) gets them in ModelNode
    // order. Nodes are never destroyed: the game's destructors expect
    // geometry it built itself, so the caller keeps and reuses them.
    RE::NiPointer<RE::NiNode> Instance(std::uint32_t a_model, std::vector<RE::NiNode*>* a_nodes = nullptr);

    // The id of a received model by its name (ModelHeader.name), 0 when none has arrived. MSC sends
    // its few one-off models (the death overlay) under fixed names.
    std::uint32_t Find(std::string_view a_name);

    // The lights of a received model (vehicles: MSC's own Unity Lights), null for an unknown model.
    const std::vector<Model::Light>* Lights(std::uint32_t a_model);

    void Tick(float a_delta);  // every frame the player is handed over; logs the frame cost around a build

    // A GPU texture and its view wrapped in a NiSourceTexture the game's shaders and Scaleform can
    // use. Put together by hand (vtable, renderer texture) since the game only makes them while
    // loading a file; never freed (the game's destructor expects its own).
    RE::NiSourceTexture* Wrap(REX::W32::ID3D11Texture2D* a_texture, REX::W32::ID3D11ShaderResourceView* a_view, std::uint32_t a_width,
        std::uint32_t a_height, std::uint32_t a_mips, const std::string& a_name);
}
