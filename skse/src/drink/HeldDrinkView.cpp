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

        float IniFloat(const char* a_name)
        {
            auto* settings = RE::INISettingCollection::GetSingleton();
            auto* setting = settings ? settings->GetSetting(a_name) : nullptr;
            return setting ? setting->GetFloat() : -1.0f;
        }

        // Skyrim's first-person 3D itself: its own render pass draws it after the world with the
        // first-person near plane (fNear1stPersonDistance) and FOV. Its parent, which v50's first
        // try used, is the world's: the bottle at the mouth was inside fNearDistance and cut to a
        // ring. Combat::Guard culls the first-person body's children, not its root, so the bottle
        // shows. HeldDrinkScene=world: the world's scene, to compare.
        RE::NiNode* Scene(RE::PlayerCharacter* a_player)
        {
            if (state.world < 0) {
                state.world = Ini::Value("HeldDrinkScene") == "world" ? 1 : 0;
                SKSE::log::info("held drink: near planes {:.1f} (world), {:.1f} (first person), in the {} scene", IniFloat("fNearDistance:Display"),
                    IniFloat("fNear1stPersonDistance:Display"), state.world == 1 ? "world's" : "first-person");
            }
            if (state.world == 1) {
                auto* root = a_player ? a_player->Get3D(false) : nullptr;
                return root && root->parent ? root->parent : RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
            }
            auto* firstPerson = a_player ? a_player->Get3D(true) : nullptr;
            return firstPerson ? firstPerson->AsNode() : nullptr;
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
            state.node->name = kNodeName;
            scene->AttachChild(state.node.get(), true);
            SKSE::log::info("held drink: {:08X} hangs in {} ({}), scale {:.2f}", state.form, scene->name.c_str(),
                state.world == 1 ? "the world's scene" : "the first-person 3D", local.scale);
        }
        // The scene's world turned back out, as PoseCamera does for the camera.
        RE::NiMatrix3 rotate;
        for (int r = 0; r < 3; r++) {
            for (int c = 0; c < 3; c++) {
                rotate.entry[r][c] = world.rotate[r][c];
            }
        }
        const auto  toScene = scene->world.rotate.Transpose();
        const float sceneScale = scene->world.scale > 1e-4f ? scene->world.scale : 1.0f;
        state.node->local.rotate = toScene * rotate;
        state.node->local.translate = toScene * (RE::NiPoint3(world.translate.x, world.translate.y, world.translate.z) - scene->world.translate) / sceneScale;
        state.node->local.scale = world.scale / sceneScale;
        RE::NiUpdateData update{};
        state.node->Update(update);
        if (!state.placedLogged) {
            state.placedLogged = true;
            const auto  bottle = state.node->world.translate - eye->world.translate;
            const auto& p = scene->world.translate;
            SKSE::log::info("held drink: base ({:.1f}, {:.1f}, {:.1f}) top ({:.1f}, {:.1f}, {:.1f}) in the camera's space; model origin {:.1f} from the "
                            "camera; its parent at ({:.0f}, {:.0f}, {:.0f}) scale {:.2f}, the camera at ({:.0f}, {:.0f}, {:.0f})",
                m.bottom[0], m.bottom[1], m.bottom[2], m.top[0], m.top[1], m.top[2], bottle.Length(), p.x, p.y, p.z, scene->world.scale, eye->world.translate.x,
                eye->world.translate.y, eye->world.translate.z);
        }
    }

    bool IsHeldDrink(const RE::NiAVObject* a_object)
    {
        return a_object && a_object->name == kNodeName;
    }

    void Clear()
    {
        Drop("handback");
        state.last = {};
        state.age = kStaleSeconds;
    }
}
