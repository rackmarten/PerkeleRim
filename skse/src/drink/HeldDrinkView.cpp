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
            bool                      covered = false;  // Cover
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
        if (state.last.form == 0 || state.age >= kStaleSeconds || !a_shown || state.covered) {
            Drop(state.last.form == 0 ? "MSC's bottle left the hand"
                 : state.covered      ? "a menu over the view"
                 : a_shown            ? "nothing from MSC"
                                      : "the chase camera or no MSC camera");
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
        if (!Held::Place({ m.bottom[0], m.bottom[1], m.bottom[2] }, { m.top[0], m.top[1], m.top[2] }, { m.side[0], m.side[1], m.side[2] },
                { m.grip[0], m.grip[1], m.grip[2] }, state.boundsMin, state.boundsMax, local)) {
            Drop("no length");
            return;
        }
        // From the camera Skyrim draws the world with, which MSC's hand layer lines up with; the
        // camera root (PoseCamera) only when there's none. NiCamera looks along its +X with +Y up
        // and +Z right; the held drink's camera space is +X right, +Y forward, +Z up.
        auto*        view = RE::Main::WorldRootCamera();
        Held::Matrix cameraRotate;
        for (int r = 0; r < 3; r++) {
            if (view) {
                cameraRotate[r][0] = view->world.rotate.entry[r][2];
                cameraRotate[r][1] = view->world.rotate.entry[r][0];
                cameraRotate[r][2] = view->world.rotate.entry[r][1];
            } else {
                for (int c = 0; c < 3; c++) {
                    cameraRotate[r][c] = eye->world.rotate.entry[r][c];
                }
            }
        }
        const auto& at = view ? view->world.translate : eye->world.translate;
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
            // The model's neck where it ended up, against the grip MSC sent: both from the eye.
            const auto   neckLocal = Held::Apply(local.rotate, Held::Scale(Held::NeckOf(state.boundsMin, state.boundsMax), local.scale));
            const auto   neck = Held::Add(local.translate, neckLocal);
            const auto&  p = scene->world.translate;
            SKSE::log::info("held drink: base ({:.1f}, {:.1f}, {:.1f}) top ({:.1f}, {:.1f}, {:.1f}) grip ({:.1f}, {:.1f}, {:.1f}) in the camera's space, "
                            "{:.1f} from the eye; the model's neck {:.1f} from it, its height {:.1f}; its parent at ({:.0f}, {:.0f}, {:.0f}) scale {:.2f}, "
                            "the model's origin {:.1f} from the eye",
                m.bottom[0], m.bottom[1], m.bottom[2], m.top[0], m.top[1], m.top[2], m.grip[0], m.grip[1], m.grip[2],
                Held::Length({ m.grip[0], m.grip[1], m.grip[2] }), Held::Length(neck), (state.boundsMax.z - state.boundsMin.z) * local.scale, p.x, p.y, p.z,
                scene->world.scale, (state.node->world.translate - at).Length());
            const auto& fov = camera->GetRuntimeData2();
            const auto& draw = RE::DrawWorld::GetSingleton();
            SKSE::log::info("held drink: fov {:.1f} world, {:.1f} first person (camera); {:.1f} world, {:.1f} first person (drawn)", fov.worldFOV,
                fov.firstPersonFOV, draw.worldFOV, draw.firstPersonFOV);
            if (view) {
                // The drawing camera against the camera root, in the root's own axes (right, forward, up).
                const auto off = eye->world.rotate.Transpose() * (view->world.translate - eye->world.translate);
                SKSE::log::info("held drink: the world's camera is ({:.1f}, {:.1f}, {:.1f}) from the camera root (right, forward, up)", off.x, off.y, off.z);
            }
        }
    }

    bool IsHeldDrink(const RE::NiAVObject* a_object)
    {
        return a_object && a_object->name == kNodeName;
    }

    void Cover(bool a_covered)
    {
        state.covered = a_covered;
        if (a_covered) {
            Drop("a menu over the view");
        }
    }

    void Clear()
    {
        Drop("handback");
        state.last = {};
        state.age = kStaleSeconds;
    }
}
