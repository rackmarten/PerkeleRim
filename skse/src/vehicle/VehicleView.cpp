#include "vehicle/VehicleView.h"

#include "coords.h"
#include "model/ModelView.h"
#include "vehicle/Hitbox.h"

namespace PerkeleRim::VehicleView
{
    namespace
    {
        // Each vehicle's model as MSC sends it (ModelChunk, msc/src/Game/VehicleModel.cs), read from
        // MSC's running vehicle and named like PlayerCurrentVehicle: one node per MSC object, posed
        // from VehicleParts. Nothing is drawn until it has arrived. By vehicle id - 1.
        constexpr std::array<std::string_view, Protocol::kVehicleCount> kModelNames{ "Hayosiko", "Jonnez", "Kekmet" };

        struct Lamp
        {
            RE::NiPointer<RE::NiPointLight> light;
            std::uint32_t                   mode = 0;  // 0 = off, else the kLight* role that is lit
        };

        struct View
        {
            RE::NiPointer<RE::NiNode> node;
            std::uint32_t             model = 0;  // ModelChunk.model node was built from
            std::vector<RE::NiNode*>  nodes;      // its ModelNodes, VehicleParts.parts order
            bool                      mismatchLogged = false;
            bool                      shown = false;
            Lamp                      lamps[14];  // by kLight* role x 2 (the second for the right headlight), see UpdateLamps
        };
        std::array<View, Protocol::kVehicleCount> views;

        // Skyrim's lights for the vehicle's lamps (MSC's Unity Lights arrive with the model, which of
        // them are on with VehicleParts.lights). Few on purpose: Skyrim lets only 4 lights cast
        // shadows at once, torches and campfires nearby included. So one shadow spotlight for the
        // headlights (the beam changes with low and high) and plain point lights for the rear (tail,
        // larger and brighter while braking), reversing and the cabin. Color, reach and brightness
        // are tuned here, not taken from MSC (its own are for Unity's units and exposure).

        struct Look
        {
            float rgb[3];
            float radius;       // units, 70 per meter
            float fade;         // brightness
            float spotDegrees;  // beam width of a shadow spotlight, 0 = point light
        };

        constexpr float kHeadlightSplit = 40.0f;  // units between a role's lights from which each gets its own lamp

        constexpr Look kLow{ { 1.0f, 0.88f, 0.68f }, 2500.0f, 11.0f, 60.0f };
        constexpr Look kHigh{ { 1.0f, 0.88f, 0.68f }, 4000.0f, 24.0f, 35.0f };
        constexpr Look kTail{ { 1.0f, 0.08f, 0.05f }, 90.0f, 3.0f, 0.0f };
        constexpr Look kBrake{ { 1.0f, 0.08f, 0.05f }, 150.0f, 9.0f, 0.0f };
        constexpr Look kReverse{ { 1.0f, 1.0f, 1.0f }, 100.0f, 2.0f, 0.0f };
        constexpr Look kInterior{ { 1.0f, 0.96f, 0.88f }, 120.0f, 1.2f, 0.0f };

        // The lights of a role that are on, where they shine from and along on average.
        struct Group
        {
            int          count = 0;
            RE::NiPoint3 position{};
            RE::NiPoint3 direction{};
        };

        // a_side: -1 all of the role, 0 those left of a_mid (x), 1 those right of it.
        Group Collect(const std::vector<Model::Light>& a_lights, std::uint32_t a_on, std::uint32_t a_role, int a_side, float a_mid)
        {
            Group group;
            for (std::size_t i = 0; i < a_lights.size() && i < 32; i++) {
                const auto& l = a_lights[i];
                if (l.role != a_role || (a_on & (1u << i)) == 0 || (a_side == 0 && l.position[0] >= a_mid) || (a_side == 1 && l.position[0] < a_mid)) {
                    continue;
                }
                group.count++;
                group.position += RE::NiPoint3(l.position[0], l.position[1], l.position[2]);
                group.direction += RE::NiPoint3(l.direction[0], l.direction[1], l.direction[2]);
            }
            if (group.count > 0) {
                group.position /= static_cast<float>(group.count);
                group.direction.Unitize();
            }
            return group;
        }

        // Turns a light node so its local +X (the way Gamebryo lights shine) points along
        // a_direction, in the vehicle's frame.
        void Aim(RE::NiMatrix3& a_out, RE::NiPoint3 a_direction)
        {
            if (a_direction.Unitize() == 0.0f) {
                a_direction = RE::NiPoint3(0.0f, 1.0f, 0.0f);
            }
            const RE::NiPoint3 up = std::fabs(a_direction.z) > 0.99f ? RE::NiPoint3(0.0f, 1.0f, 0.0f) : RE::NiPoint3(0.0f, 0.0f, 1.0f);
            RE::NiPoint3       y = up.Cross(a_direction);
            y.Unitize();
            const RE::NiPoint3 z = a_direction.Cross(y);
            const RE::NiPoint3 columns[3] = { a_direction, y, z };
            for (int c = 0; c < 3; c++) {
                a_out.entry[0][c] = columns[c].x;
                a_out.entry[1][c] = columns[c].y;
                a_out.entry[2][c] = columns[c].z;
            }
        }

        void TurnOff(Lamp& a_lamp)
        {
            if (a_lamp.mode != 0) {
                if (auto* scene = RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0]) {
                    scene->RemoveLight(a_lamp.light.get());
                }
                a_lamp.mode = 0;
                SKSE::log::info("vehicle view: lamp off");
            }
        }

        // a_mode 0 switches the lamp off. A change of look re-adds the light (a shadow light's cone is
        // set when it is added); otherwise only its place moves.
        void Set(RE::NiNode& a_vehicle, Lamp& a_lamp, std::uint32_t a_mode, const Look& a_look, const Group& a_where, bool a_aimed, float a_level)
        {
            if (a_mode == 0) {
                TurnOff(a_lamp);
                return;
            }
            if (!a_lamp.light) {
                a_lamp.light.reset(RE::NiPointLight::Create());
                if (!a_lamp.light) {
                    return;
                }
                a_lamp.light->name = "PerkeleRimLamp";
                a_vehicle.AttachChild(a_lamp.light.get(), true);
            }
            auto& light = *a_lamp.light;
            light.local.translate = a_where.position;
            if (a_aimed) {
                Aim(light.local.rotate, a_where.direction);
            }
            light.GetLightRuntimeData().fade = a_look.fade * a_level;
            if (a_lamp.mode == a_mode) {
                return;
            }
            auto* scene = RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
            if (!scene) {
                return;
            }
            TurnOff(a_lamp);
            auto& data = light.GetLightRuntimeData();
            data.ambient = RE::NiColor(0.0f, 0.0f, 0.0f);
            data.diffuse = RE::NiColor(a_look.rgb[0], a_look.rgb[1], a_look.rgb[2]);
            data.radius = RE::NiPoint3(a_look.radius, a_look.radius, a_look.radius * 0.1f);
            data.fade = a_look.fade * a_level;
            light.SetLightAttenuation(a_look.radius);
            const bool                               spot = a_look.spotDegrees > 0.0f;
            RE::ShadowSceneNode::LIGHT_CREATE_PARAMS params{};
            params.dynamic = true;
            params.shadowLight = spot;
            params.affectLand = true;
            params.affectWater = true;
            params.neverFades = true;
            params.fov = spot ? a_look.spotDegrees * 3.14159265f / 180.0f : 1.0f;
            params.falloff = 1.0f;
            params.nearDistance = spot ? 30.0f : 5.0f;
            params.depthBias = 1.0f;
            scene->AddLight(&light, params);
            a_lamp.mode = a_mode;
            SKSE::log::info("vehicle view: lamp role {} ({} shadow lights, {} lights in the scene) on at ({:.0f},{:.0f},{:.0f}) aimed ({:.2f},{:.2f},{:.2f}), reach {:.0f}, fade {:.1f}{}", a_mode, scene->GetRuntimeData().activeShadowLights.size(), scene->GetRuntimeData().activeLights.size(),
                a_where.position.x, a_where.position.y, a_where.position.z, a_where.direction.x, a_where.direction.y, a_where.direction.z,
                a_look.radius, a_look.fade, spot ? ", shadow spot" : "");
        }

        // One light object per role, so a beam change never removes and re-adds the same light in one
        // frame (Skyrim runs queued removals after queued additions, which dropped the new light).
        void UpdateLamps(View& a_view, const Protocol::VehicleParts& a_parts, float a_steer)
        {
            const auto* lights = ModelView::Lights(a_view.model);
            if (!lights || lights->empty()) {
                return;
            }
            struct Role
            {
                std::uint32_t role;
                const Look&   look;
                bool          aimed;
            };
            const Role roles[] = { { Protocol::kLightLow, kLow, true }, { Protocol::kLightHigh, kHigh, true },
                { Protocol::kLightTail, kTail, false }, { Protocol::kLightBrake, kBrake, false },
                { Protocol::kLightReverse, kReverse, false }, { Protocol::kLightInterior, kInterior, false } };
            for (const auto& r : roles) {
                // Headlights set wide apart (the van's) are two lights, one per side; close together
                // (the Kekmet's) or single (the moped's) they stay one.
                float lo = 1e9f, hi = -1e9f;
                for (const auto& l : *lights) {
                    if (l.role == r.role) {
                        lo = (std::min)(lo, l.position[0]);
                        hi = (std::max)(hi, l.position[0]);
                    }
                }
                const bool  head = r.role == Protocol::kLightLow || r.role == Protocol::kLightHigh;
                const bool  split = head && hi - lo > kHeadlightSplit;
                for (int side = split ? 0 : -1; side < (split ? 2 : 0); side++) {
                    auto  group = Collect(*lights, a_parts.lights, r.role, side, (lo + hi) * 0.5f);
                    float level = 1.0f;
                    if (head) {
                        level = r.role == Protocol::kLightLow ? a_parts.lowLevel : a_parts.highLevel;
                        // The moped's lamp turns with its handlebar.
                        const float s = std::sin(a_steer), c = std::cos(a_steer);
                        group.direction = RE::NiPoint3(group.direction.x * c + group.direction.y * s, -group.direction.x * s + group.direction.y * c, group.direction.z);
                    }
                    Set(*a_view.node, a_view.lamps[r.role * 2 + (side == 1 ? 1 : 0)], group.count > 0 ? r.role : 0, r.look, group, r.aimed, level);
                }
            }
        }

        void LampsOff(View& a_view)
        {
            for (auto& lamp : a_view.lamps) {
                TurnOff(lamp);
            }
        }

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
            LampsOff(a_view);
            for (auto& lamp : a_view.lamps) {
                lamp = Lamp{};
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
                LampsOff(a_view);
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
            Hitbox::Hide(a_vehicle);
            Hide(view, name);
            return;
        }
        if (!Load(view, name)) {
            Hitbox::Hide(a_vehicle);
            return;
        }
        Attach(view, name);

        auto& node = *view.node;
        SetRotation(node.local.rotate, a_state->rotation);
        node.local.translate = RE::NiPoint3(a_state->position[0], a_state->position[1], a_state->position[2]);
        if (a_parts && a_parts->vehicle == a_vehicle && a_parts->generation == a_generation) {
            PoseNodes(view, name, *a_parts);
            if (view.node && a_parts->model == view.model) {
                UpdateLamps(view, *a_parts, a_vehicle == Protocol::kVehicleJonnez ? a_state->steer : 0.0f);
            }
        }
        node.SetAppCulled(false);
        RE::NiUpdateData update{};
        node.Update(update);
        Hitbox::Show(a_vehicle, name, view.model, *a_state);

        if (!view.shown) {
            view.shown = true;
            SKSE::log::info("vehicle view: showing the {} at ({:.0f},{:.0f},{:.0f})", name,
                a_state->position[0], a_state->position[1], a_state->position[2]);
        }
    }

    void Hide()
    {
        for (std::uint32_t i = 0; i < Protocol::kVehicleCount; i++) {
            Hitbox::Hide(i + 1);
            Hide(views[i], kModelNames[i]);
        }
    }
}
