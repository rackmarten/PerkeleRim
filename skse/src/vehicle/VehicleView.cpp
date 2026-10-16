#include "vehicle/VehicleView.h"

#include "coords.h"
#include "model/ModelView.h"

namespace PerkeleRim::VehicleView
{
    namespace
    {
        // Each vehicle's model as MSC sends it (ModelChunk, msc/src/Game/VehicleModel.cs), read from
        // MSC's running vehicle and named like PlayerCurrentVehicle: one node per MSC object, posed
        // from VehicleParts. Nothing is drawn until it has arrived. By vehicle id - 1.
        constexpr std::array<std::string_view, Protocol::kVehicleCount> kModelNames{ "Hayosiko", "Jonnez", "Kekmet" };

        struct View
        {
            RE::NiPointer<RE::NiNode> node;
            std::uint32_t             model = 0;  // ModelChunk.model node was built from
            std::vector<RE::NiNode*>  nodes;      // its ModelNodes, VehicleParts.parts order
            bool                      mismatchLogged = false;
            bool                      shown = false;
        };
        std::array<View, Protocol::kVehicleCount> views;

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
        void PoseNodes(View& a_view, std::string_view a_name, const Protocol::VehicleParts& a_parts)
        {
            if (a_parts.model != a_view.model || a_parts.count != a_view.nodes.size()) {
                if (!a_view.mismatchLogged && a_parts.model == a_view.model) {
                    a_view.mismatchLogged = true;
                    SKSE::log::warn("vehicle view: MSC poses {} nodes of {} model {:08X}, it has {}", a_parts.count, a_name, a_view.model, a_view.nodes.size());
                }
                return;
            }
            for (std::size_t i = 0; i < a_view.nodes.size(); i++) {
                auto*       node = a_view.nodes[i];
                const auto& in = a_parts.parts[i];
                SetRotation(node->local.rotate, in.rotation);
                node->local.translate = RE::NiPoint3(in.translation[0], in.translation[1], in.translation[2]);
                node->SetAppCulled((in.flags & Protocol::kPartVisible) == 0);
            }
        }

        // The node the player's own 3D hangs in (the loaded cells' actor node, at world origin), so
        // the vehicle is lit and culled like the world around it.
        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }

        // Built once the vehicle's model has arrived, and again when MSC sent a different one (MSC
        // restarted: new ids). Nodes are never destroyed (ModelView), an old one is only detached.
        bool Load(View& a_view, std::string_view a_name)
        {
            const auto model = ModelView::Find(a_name);
            if (model == 0 || model == a_view.model) {
                return a_view.node != nullptr;
            }
            std::vector<RE::NiNode*> nodes;
            auto                     node = ModelView::Instance(model, &nodes);
            if (!node) {
                return a_view.node != nullptr;
            }
            if (a_view.node && a_view.node->parent) {
                a_view.node->parent->DetachChild(a_view.node.get());
            }
            a_view.node = node;
            a_view.model = model;
            a_view.nodes = std::move(nodes);
            a_view.mismatchLogged = false;
            a_view.shown = false;
            SKSE::log::info("vehicle view: {} model {:08X} from MSC, {} nodes", a_name, model, a_view.nodes.size());
            return true;
        }

        void Attach(View& a_view, std::string_view a_name)
        {
            auto* scene = Scene();
            if (scene && a_view.node->parent != scene) {
                if (a_view.node->parent) {
                    a_view.node->parent->DetachChild(a_view.node.get());
                }
                scene->AttachChild(a_view.node.get(), true);
                SKSE::log::info("vehicle view: {} attached under {}", a_name, scene->name.c_str());
            }
        }

        void Hide(View& a_view, std::string_view a_name)
        {
            if (a_view.node && a_view.shown) {
                a_view.node->SetAppCulled(true);
                a_view.shown = false;
                SKSE::log::info("vehicle view: {} hidden", a_name);
            }
        }
    }

    void Update(std::uint32_t a_vehicle, const Protocol::VehicleState* a_state, const Protocol::VehicleParts* a_parts, std::uint32_t a_generation)
    {
        if (a_vehicle < 1 || a_vehicle > Protocol::kVehicleCount) {
            return;
        }
        auto&      view = views[a_vehicle - 1];
        const auto name = kModelNames[a_vehicle - 1];
        const bool valid = a_state && a_state->vehicle == a_vehicle && (a_state->flags & Protocol::kVehicleValid) != 0 &&
                           a_state->generation == a_generation;
        if (!valid) {
            Hide(view, name);
            return;
        }
        if (!Load(view, name)) {
            return;
        }
        Attach(view, name);

        auto& node = *view.node;
        SetRotation(node.local.rotate, a_state->rotation);
        node.local.translate = RE::NiPoint3(a_state->position[0], a_state->position[1], a_state->position[2]);
        if (a_parts && a_parts->vehicle == a_vehicle && a_parts->generation == a_generation) {
            PoseNodes(view, name, *a_parts);
        }
        node.SetAppCulled(false);
        RE::NiUpdateData update{};
        node.Update(update);

        if (!view.shown) {
            view.shown = true;
            SKSE::log::info("vehicle view: showing the {} at ({:.0f},{:.0f},{:.0f})", name,
                a_state->position[0], a_state->position[1], a_state->position[2]);
        }
    }

    void Hide()
    {
        for (std::uint32_t i = 0; i < Protocol::kVehicleCount; i++) {
            Hide(views[i], kModelNames[i]);
        }
    }
}
