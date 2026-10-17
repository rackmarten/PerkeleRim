#include "particle/ParticleView.h"

#include "model/ModelView.h"

namespace PerkeleRim::ParticleView
{
    namespace
    {
        // How much of their color the unlit squares glow with, times Skyrim's light at the player
        // (the lighting shader adds the scene's light on top). Light smoke matches MSC at 1; dark
        // smoke has a black texture, so neither changes it.
        constexpr float kGlow = 1.0f;

        struct Square
        {
            RE::NiPointer<RE::NiNode>     node;
            RE::BSLightingShaderProperty* shader = nullptr;  // its one shape's
        };

        struct Pool
        {
            std::vector<Square> squares;
            std::size_t         used = 0;  // this frame
        };

        struct State
        {
            std::unordered_map<std::uint32_t, Pool> pools;  // by particle model id
            std::uint32_t                            shown = 0;  // last logged
            bool                                     hidden = true;
        } state;

        // The node the player's own 3D hangs in, like ItemView.
        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }

        Square* Take(std::uint32_t a_model, RE::NiNode* a_scene)
        {
            auto& pool = state.pools[a_model];
            if (pool.used < pool.squares.size()) {
                return &pool.squares[pool.used++];
            }
            Square square;
            square.node = ModelView::Instance(a_model);
            if (!square.node) {
                return nullptr;  // not arrived yet
            }
            RE::BSVisit::TraverseScenegraphGeometries(square.node.get(), [&square](RE::BSGeometry* a_geometry) {
                square.shader = netimmerse_cast<RE::BSLightingShaderProperty*>(a_geometry->GetGeometryRuntimeData().shaderProperty.get());
                return RE::BSVisit::BSVisitControl::kStop;
            });
            if (a_scene) {
                a_scene->AttachChild(square.node.get(), true);
            }
            pool.squares.push_back(std::move(square));
            if (pool.squares.size() % 64 == 0) {
                SKSE::log::info("particle view: {} squares for model {:08X}", pool.squares.size(), a_model);
            }
            return &pool.squares[pool.used++];
        }

        void Look(Square& a_square, const Protocol::ParticlePose& a_pose, const std::array<float, 3>& a_light)
        {
            if (!a_square.shader) {
                return;
            }
            if (auto* material = static_cast<RE::BSLightingShaderMaterialBase*>(a_square.shader->material)) {
                material->materialAlpha = a_pose.color[3] / 255.0f;
                // One frame of the sheet: the square's UVs (0..1, v down from the top) scaled to a
                // cell and moved to it.
                const float columns = (std::max)(1.0f, static_cast<float>(a_pose.sheet[0]));
                const float rows = (std::max)(1.0f, static_cast<float>(a_pose.sheet[1]));
                const int   frame = a_pose.sheet[2];
                const auto  cells = static_cast<int>(columns) * static_cast<int>(rows);
                const int   cell = cells > 0 ? frame % cells : 0;
                for (int k = 0; k < 2; k++) {
                    material->texCoordScale[k] = RE::NiPoint2(1.0f / columns, 1.0f / rows);
                    material->texCoordOffset[k] = RE::NiPoint2((cell % static_cast<int>(columns)) / columns, (cell / static_cast<int>(columns)) / rows);
                }
            }
            if (auto* glow = a_square.shader->emissiveColor) {
                glow->red = a_pose.color[0] / 255.0f * a_light[0];
                glow->green = a_pose.color[1] / 255.0f * a_light[1];
                glow->blue = a_pose.color[2] / 255.0f * a_light[2];
                a_square.shader->emissiveMult = kGlow;
            }
        }
    }

    void Update(const Protocol::ParticleState* a_state, std::uint32_t a_generation, const std::array<float, 3>& a_light)
    {
        auto* camera = RE::Main::WorldRootCamera();
        if (!a_state || a_state->generation != a_generation || !camera) {
            Hide();
            return;
        }
        // NiCamera looks along its local +X with +Y up and +Z right (see Death's overlay).
        const auto&        r = camera->world.rotate;
        const RE::NiPoint3 forward(r.entry[0][0], r.entry[1][0], r.entry[2][0]);
        const RE::NiPoint3 up(r.entry[0][1], r.entry[1][1], r.entry[2][1]);
        const RE::NiPoint3 right(r.entry[0][2], r.entry[1][2], r.entry[2][2]);

        auto* scene = Scene();
        for (auto& [model, pool] : state.pools) {
            pool.used = 0;
        }
        const std::uint32_t count = (std::min)(a_state->count, Protocol::kParticleCapacity);
        std::uint32_t       shown = 0;
        for (std::uint32_t i = 0; i < count; i++) {
            const auto& pose = a_state->particles[i];
            if (pose.model == 0 || pose.size <= 0.0f) {
                continue;
            }
            auto* square = Take(pose.model, scene);
            if (!square) {
                continue;
            }
            auto& node = *square->node;
            if (scene && node.parent != scene) {
                if (node.parent) {
                    node.parent->DetachChild(&node);
                }
                scene->AttachChild(&node, true);
            }
            // The square's x is the screen's right and z its up, turned by the particle's rotation;
            // y looks along the camera.
            const float        c = std::cos(pose.rotation), s = std::sin(pose.rotation);
            const RE::NiPoint3 x = right * c - up * s;
            const RE::NiPoint3 z = up * c + right * s;
            for (int k = 0; k < 3; k++) {
                node.local.rotate.entry[k][0] = x[k];
                node.local.rotate.entry[k][1] = forward[k];
                node.local.rotate.entry[k][2] = z[k];
            }
            node.local.translate = RE::NiPoint3(pose.position[0], pose.position[1], pose.position[2]);
            node.local.scale = pose.size;
            node.SetAppCulled(false);
            RE::NiUpdateData update{};
            node.Update(update);
            Look(*square, pose, a_light);
            shown++;
        }
        for (auto& [model, pool] : state.pools) {
            for (std::size_t k = pool.used; k < pool.squares.size(); k++) {
                pool.squares[k].node->SetAppCulled(true);
            }
        }
        state.hidden = shown == 0;
        if ((shown == 0) != (state.shown == 0)) {
            SKSE::log::info("particle view: {} particles shown", shown);
        }
        state.shown = shown;
    }

    void Hide()
    {
        if (state.hidden) {
            return;
        }
        for (auto& [model, pool] : state.pools) {
            pool.used = 0;
            for (auto& square : pool.squares) {
                square.node->SetAppCulled(true);
            }
        }
        state.hidden = true;
        state.shown = 0;
        SKSE::log::info("particle view: hidden");
    }
}
