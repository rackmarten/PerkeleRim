#include "vehicle/VehicleView.h"

#include "coords.h"
#include "model/ModelView.h"

namespace PerkeleRim::VehicleView
{
    namespace
    {
        // The van's model as MSC sends it (ModelChunk, msc/src/Game/VehicleModel.cs), read from MSC's
        // running van: one node per MSC object, posed from VehicleParts. Nothing is drawn until it
        // has arrived.
        constexpr std::string_view kModelName = "Hayosiko";

        struct State
        {
            RE::NiPointer<RE::NiNode> node;
            std::uint32_t             model = 0;  // ModelChunk.model node was built from
            std::vector<RE::NiNode*>  nodes;      // its ModelNodes, VehicleParts.parts order
            bool                      mismatchLogged = false;
            bool                      shown = false;
        } state;

        void SetRotation(RE::NiMatrix3& a_out, const float (&a_quat)[4])
        {
            float m[3][3];
            Coords::ToMatrix({ a_quat[0], a_quat[1], a_quat[2], a_quat[3] }, m);
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    a_out.entry[r][c] = m[r][c];
                }
            }
        }

        // Each node where MSC has its object, relative to the body, and hidden while MSC doesn't
        // draw it (lights switched off, the key out of the ignition, the broken windshield while
        // it is whole).
        void PoseNodes(const Protocol::VehicleParts& a_parts)
        {
            if (a_parts.model != state.model || a_parts.count != state.nodes.size()) {
                if (!state.mismatchLogged && a_parts.model == state.model) {
                    state.mismatchLogged = true;
                    SKSE::log::warn("vehicle view: MSC poses {} nodes of model {:08X}, it has {}", a_parts.count, state.model, state.nodes.size());
                }
                return;
            }
            for (std::size_t i = 0; i < state.nodes.size(); i++) {
                auto*       node = state.nodes[i];
                const auto& in = a_parts.parts[i];
                SetRotation(node->local.rotate, in.rotation);
                node->local.translate = RE::NiPoint3(in.translation[0], in.translation[1], in.translation[2]);
                node->SetAppCulled((in.flags & Protocol::kPartVisible) == 0);
            }
        }

        // The node the player's own 3D hangs in (the loaded cells' actor node, at world origin), so
        // the van is lit and culled like the world around it.
        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }

        // Built once the van's model has arrived, and again when MSC sent a different one (MSC
        // restarted: new ids). Nodes are never destroyed (ModelView), an old one is only detached.
        bool Load()
        {
            const auto model = ModelView::Find(kModelName);
            if (model == 0 || model == state.model) {
                return state.node != nullptr;
            }
            std::vector<RE::NiNode*> nodes;
            auto                     node = ModelView::Instance(model, &nodes);
            if (!node) {
                return state.node != nullptr;
            }
            if (state.node && state.node->parent) {
                state.node->parent->DetachChild(state.node.get());
            }
            state.node = node;
            state.model = model;
            state.nodes = std::move(nodes);
            state.mismatchLogged = false;
            state.shown = false;
            SKSE::log::info("vehicle view: model {:08X} from MSC, {} nodes", model, state.nodes.size());
            return true;
        }

        void Attach()
        {
            auto* scene = Scene();
            if (scene && state.node->parent != scene) {
                if (state.node->parent) {
                    state.node->parent->DetachChild(state.node.get());
                }
                scene->AttachChild(state.node.get(), true);
                SKSE::log::info("vehicle view: attached under {}", scene->name.c_str());
            }
        }
    }

    void Update(const Protocol::VehicleState* a_state, const Protocol::VehicleParts* a_parts, std::uint32_t a_generation)
    {
        const bool valid = a_state && (a_state->flags & Protocol::kVehicleValid) != 0 && a_state->generation == a_generation;
        if (!valid) {
            Hide();
            return;
        }
        if (!Load()) {
            return;
        }
        Attach();

        auto& node = *state.node;
        SetRotation(node.local.rotate, a_state->rotation);
        node.local.translate = RE::NiPoint3(a_state->position[0], a_state->position[1], a_state->position[2]);
        if (a_parts && a_parts->vehicle == a_state->vehicle && a_parts->generation == a_generation) {
            PoseNodes(*a_parts);
        }
        node.SetAppCulled(false);
        RE::NiUpdateData update{};
        node.Update(update);

        if (!state.shown) {
            state.shown = true;
            SKSE::log::info("vehicle view: showing vehicle {} at ({:.0f},{:.0f},{:.0f})", a_state->vehicle,
                a_state->position[0], a_state->position[1], a_state->position[2]);
        }
    }

    void Hide()
    {
        if (state.node && state.shown) {
            state.node->SetAppCulled(true);
            state.shown = false;
            SKSE::log::info("vehicle view: hidden");
        }
    }
}
