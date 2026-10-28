#include "drink/HeldDrinkView.h"

#include "held_drink.h"
#include "Ini.h"
#include "item/ItemView.h"

namespace PerkeleRim::HeldDrinkView
{
    namespace
    {
        // MSC sends a pose every frame of its own; with none for this long the bottle is gone
        // (MSC stalled, or its form 0 got lost).
        constexpr float kStaleSeconds = 0.25f;

        struct State
        {
            Protocol::HeldDrink       last{};
            float                     age = kStaleSeconds;  // seconds since last came
            RE::FormID                form = 0;             // the model in node
            RE::NiPointer<RE::NiNode> node;
            Held::Vec                 boundsMin, boundsMax;  // its form's object bounds
            bool                      placedLogged = false;
            int                       world = -1;  // PerkeleRim.ini HeldDrinkScene=world: the world's scene instead (to compare)
        } state;

        // The node Skyrim's first-person body hangs in (its first-person scene), else the world's.
        RE::NiNode* Scene(RE::PlayerCharacter* a_player)
        {
            if (state.world < 0) {
                state.world = Ini::Value("HeldDrinkScene") == "world" ? 1 : 0;
            }
            auto& shader = RE::BSShaderManager::State::GetSingleton();
            if (state.world == 1) {
                auto* root = a_player ? a_player->Get3D(false) : nullptr;
                return root && root->parent ? root->parent : shader.shadowSceneNode[0];
            }
            auto* firstPerson = a_player ? a_player->Get3D(true) : nullptr;
            return firstPerson && firstPerson->parent ? firstPerson->parent : shader.shadowSceneNode[1];
        }

        void Drop(const char* a_why)
        {
            if (state.node && state.node->parent) {
                state.node->parent->DetachChild(state.node.get());
                SKSE::log::info("held drink: {:08X} out of the hand ({})", state.form, a_why);
            }
            state.placedLogged = false;
        }

        bool Load(RE::FormID a_form)
        {
            Drop("another drink");
            state.form = a_form;
            state.node = ItemView::FormModel(a_form);
            auto* bound = RE::TESForm::LookupByID<RE::TESBoundObject>(a_form);
            if (!state.node || !bound) {
                state.node.reset();
                return false;
            }
            const auto& data = bound->boundData;
            state.boundsMin = { static_cast<float>(data.boundMin.x), static_cast<float>(data.boundMin.y), static_cast<float>(data.boundMin.z) };
            state.boundsMax = { static_cast<float>(data.boundMax.x), static_cast<float>(data.boundMax.y), static_cast<float>(data.boundMax.z) };
            SKSE::log::info("held drink: {} ({:08X}) in Kumpo's hand, bounds ({}, {}, {}) to ({}, {}, {})", bound->GetName(), a_form, data.boundMin.x,
                data.boundMin.y, data.boundMin.z, data.boundMax.x, data.boundMax.y, data.boundMax.z);
            return true;
        }
    }

    void OnMessage(const std::uint8_t* a_data, std::uint32_t a_size)
    {
        if (a_size < sizeof(Protocol::HeldDrink)) {
            return;
        }
        std::memcpy(&state.last, a_data, sizeof(state.last));
        state.age = 0.0f;
    }

    void Update(RE::PlayerCharacter* a_player, bool a_shown, float a_delta)
    {
        state.age += a_delta;
        if (state.last.form == 0 || state.age >= kStaleSeconds || !a_shown) {
            Drop(state.last.form == 0 ? "MSC's bottle left the hand" : a_shown ? "nothing from MSC" : "a menu, the chase camera or no MSC camera");
            return;
        }
        if (state.last.form != state.form && !Load(state.last.form)) {
            return;  // logged; its form stays, so it isn't tried every frame
        }
        auto* camera = RE::PlayerCamera::GetSingleton();
        auto* eye = camera ? camera->cameraRoot.get() : nullptr;
        auto* scene = Scene(a_player);
        if (!state.node || !eye || !scene) {
            return;
        }
        const auto& m = state.last;
        Held::Pose  local;
        if (!Held::Place({ m.bottom[0], m.bottom[1], m.bottom[2] }, { m.top[0], m.top[1], m.top[2] }, { m.side[0], m.side[1], m.side[2] }, state.boundsMin,
                state.boundsMax, local)) {
            Drop("no length");
            return;
        }
        Held::Matrix cameraRotate;
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                cameraRotate[r][c] = eye->world.rotate.entry[r][c];
            }
        }
        const auto& at = eye->world.translate;
        const auto  world = Held::ToParent(cameraRotate, { at.x, at.y, at.z }, local);

        if (state.node->parent != scene) {
            if (state.node->parent) {
                state.node->parent->DetachChild(state.node.get());
            }
            scene->AttachChild(state.node.get(), true);
            SKSE::log::info("held drink: {:08X} hangs in {} ({}), scale {:.2f}", state.form, scene->name.c_str(),
                scene == RE::BSShaderManager::State::GetSingleton().shadowSceneNode[1] ? "first-person scene" : "world", local.scale);
        }
        // The scene's world turned back out (scale 1), as PoseCamera does for the camera.
        RE::NiMatrix3 rotate;
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                rotate.entry[r][c] = world.rotate[r][c];
            }
        }
        const auto toScene = scene->world.rotate.Transpose();
        state.node->local.rotate = toScene * rotate;
        state.node->local.translate = toScene * (RE::NiPoint3(world.translate.x, world.translate.y, world.translate.z) - scene->world.translate);
        state.node->local.scale = world.scale;
        RE::NiUpdateData update{};
        state.node->Update(update);
        if (!state.placedLogged) {
            state.placedLogged = true;
            SKSE::log::info("held drink: base ({:.1f}, {:.1f}, {:.1f}) top ({:.1f}, {:.1f}, {:.1f}) in the camera's space", m.bottom[0], m.bottom[1],
                m.bottom[2], m.top[0], m.top[1], m.top[2]);
        }
    }

    void Clear()
    {
        Drop("handback");
        state.last = {};
        state.age = kStaleSeconds;
    }
}
