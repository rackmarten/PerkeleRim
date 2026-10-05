#include "vehicle/VehicleView.h"

#include "coords.h"

namespace PerkeleRim::VehicleView
{
    namespace
    {
        // Stand-in meshes, first one that loads wins.
        constexpr std::array kStandIns{
            "Meshes\\Furniture\\Cart\\CartFurnStatic01.nif",
            "Meshes\\Clutter\\Carts\\HandCart01.nif",
        };

        struct State
        {
            RE::NiPointer<RE::NiNode> node;
            bool                      failed = false;  // no stand-in loaded; don't retry every frame
            bool                      shown = false;
        } state;

        // The node the player's own 3D hangs in (the loaded cells' actor node, at world origin), so
        // the stand-in is lit and culled like the world around it.
        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }

        bool Load()
        {
            if (state.node || state.failed) {
                return state.node != nullptr;
            }
            for (const char* path : kStandIns) {
                RE::NiPointer<RE::NiNode> model;
                const auto                error = RE::BSModelDB::Demand(path, model, {});
                if (error == RE::BSResource::ErrorCode::kNone && model) {
                    state.node = model;
                    SKSE::log::info("vehicle view: stand-in {}", path);
                    return true;
                }
                SKSE::log::info("vehicle view: {} did not load (error {})", path, static_cast<int>(error));
            }
            state.failed = true;
            SKSE::log::warn("vehicle view: no stand-in mesh loaded, the van stays invisible");
            return false;
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

    void Update(const Protocol::VehicleState* a_state, std::uint32_t a_generation)
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

        auto&                node = *state.node;
        const Coords::Quat q{ a_state->rotation[0], a_state->rotation[1], a_state->rotation[2], a_state->rotation[3] };
        float              m[3][3];
        Coords::ToMatrix(q, m);
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                node.local.rotate.entry[r][c] = m[r][c];
            }
        }
        node.local.translate = RE::NiPoint3(a_state->position[0], a_state->position[1], a_state->position[2]);
        node.SetAppCulled(false);
        // A model loaded outside a cell is never faded in by the game and stays invisible.
        if (auto* fade = node.AsFadeNode()) {
            fade->GetRuntimeData().currentFade = 1.0f;
        }
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
