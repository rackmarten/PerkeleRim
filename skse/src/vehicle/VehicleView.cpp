#include "vehicle/VehicleView.h"

#include "coords.h"

namespace PerkeleRim::VehicleView
{
    namespace
    {
        // The van converted from the player's own MSC install (tools/convert-vehicle.ps1), then vanilla
        // stand-ins for when it wasn't converted. First one that loads wins.
        constexpr std::array kModels{
            "Meshes\\PerkeleRim\\Hayosiko.nif",
            "Meshes\\Furniture\\Cart\\CartFurnStatic01.nif",
            "Meshes\\Clutter\\Carts\\HandCart01.nif",
        };

        // Wheel nodes of the converted mesh, in VehicleState.wheelSpin order. "<name>" steers about Z
        // (front wheels only), "<name>:Spin" under it turns about X.
        constexpr std::array kWheels{ "WheelFL", "WheelFR", "WheelRL", "WheelRR" };

        struct Wheel
        {
            RE::NiAVObject* steer = nullptr;
            RE::NiAVObject* spin = nullptr;
        };

        // Part nodes of the converted mesh are named by their MSC path, in VehicleParts order.
        constexpr std::size_t kPartCount = Protocol::kHayosikoPartCount;

        struct State
        {
            RE::NiPointer<RE::NiNode>               node;
            std::array<Wheel, kWheels.size()>       wheels{};
            std::array<RE::NiAVObject*, kPartCount> parts{};
            bool                                    failed = false;  // nothing loaded; don't retry every frame
            bool                                    shown = false;
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

        // Right-handed rotation matrices (Skyrim space), for column vectors like NiMatrix3.
        RE::NiMatrix3 RotationX(float a_angle)
        {
            const float c = std::cos(a_angle), s = std::sin(a_angle);
            RE::NiMatrix3 m;
            m.entry[0][0] = 1.0f; m.entry[0][1] = 0.0f; m.entry[0][2] = 0.0f;
            m.entry[1][0] = 0.0f; m.entry[1][1] = c;    m.entry[1][2] = -s;
            m.entry[2][0] = 0.0f; m.entry[2][1] = s;    m.entry[2][2] = c;
            return m;
        }

        RE::NiMatrix3 RotationZ(float a_angle)
        {
            const float c = std::cos(a_angle), s = std::sin(a_angle);
            RE::NiMatrix3 m;
            m.entry[0][0] = c;    m.entry[0][1] = -s;   m.entry[0][2] = 0.0f;
            m.entry[1][0] = s;    m.entry[1][1] = c;    m.entry[1][2] = 0.0f;
            m.entry[2][0] = 0.0f; m.entry[2][1] = 0.0f; m.entry[2][2] = 1.0f;
            return m;
        }

        void FindWheels()
        {
            int found = 0;
            for (std::size_t i = 0; i < kWheels.size(); i++) {
                const std::string spin = std::string(kWheels[i]) + ":Spin";
                state.wheels[i].steer = state.node->GetObjectByName(kWheels[i]);
                state.wheels[i].spin = state.node->GetObjectByName(spin);
                found += state.wheels[i].spin ? 1 : 0;
            }
            SKSE::log::info("vehicle view: {} of {} wheel nodes", found, kWheels.size());
        }

        // Only the converted van has them; the stand-ins find none.
        void FindParts()
        {
            int found = 0;
            for (std::size_t i = 0; i < kPartCount; i++) {
                state.parts[i] = state.node->GetObjectByName(Protocol::kHayosikoParts[i]);
                found += state.parts[i] ? 1 : 0;
            }
            SKSE::log::info("vehicle view: {} of {} part nodes", found, kPartCount);
        }

        // Pose relative to the body, and hidden while MSC doesn't draw the part (lights switched off,
        // the key out of the ignition).
        void PoseParts(const Protocol::VehicleParts& a_parts)
        {
            for (std::size_t i = 0; i < kPartCount; i++) {
                auto* part = state.parts[i];
                if (!part) {
                    continue;
                }
                const auto& in = a_parts.parts[i];
                SetRotation(part->local.rotate, in.rotation);
                part->local.translate = RE::NiPoint3(in.translation[0], in.translation[1], in.translation[2]);
                part->SetAppCulled((in.flags & Protocol::kPartVisible) == 0);
            }
        }

        // Steer turns right for positive angles (clockwise seen from above, so negative about +Z);
        // a wheel rolling forward turns its top toward +Y, which is negative about +X.
        void PoseWheels(const Protocol::VehicleState& a_state)
        {
            for (std::size_t i = 0; i < kWheels.size(); i++) {
                const auto& wheel = state.wheels[i];
                if (wheel.steer && i < 2) {
                    wheel.steer->local.rotate = RotationZ(-a_state.steer);
                }
                if (wheel.spin) {
                    wheel.spin->local.rotate = RotationX(-a_state.wheelSpin[i]);
                }
            }
        }

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
            for (const char* path : kModels) {
                RE::NiPointer<RE::NiNode> model;
                const auto                error = RE::BSModelDB::Demand(path, model, {});
                if (error == RE::BSResource::ErrorCode::kNone && model) {
                    state.node = model;
                    SKSE::log::info("vehicle view: model {}", path);
                    FindWheels();
                    FindParts();
                    return true;
                }
                SKSE::log::info("vehicle view: {} did not load (error {})", path, static_cast<int>(error));
            }
            state.failed = true;
            SKSE::log::warn("vehicle view: no mesh loaded, the van stays invisible");
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
        PoseWheels(*a_state);
        if (a_parts && a_parts->vehicle == a_state->vehicle && a_parts->generation == a_generation &&
            a_parts->count == kPartCount) {
            PoseParts(*a_parts);
        }
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
