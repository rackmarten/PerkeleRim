#include "player/PlayerSync.h"

#include "coords.h"
#include "input.h"
#include "link/LinkService.h"

namespace PerkeleRim::PlayerSync
{
    namespace
    {
        using UEFlag = RE::UserEvents::USER_EVENT_FLAG;

        // Skyrim controls that MSC replaces while it owns the player. Activate and the menus stay
        // with Skyrim.
        constexpr auto kTakenControls = static_cast<UEFlag>(
            static_cast<std::uint32_t>(UEFlag::kMovement) | static_cast<std::uint32_t>(UEFlag::kLooking) |
            static_cast<std::uint32_t>(UEFlag::kSneaking) | static_cast<std::uint32_t>(UEFlag::kJumping) |
            static_cast<std::uint32_t>(UEFlag::kFighting) | static_cast<std::uint32_t>(UEFlag::kPOVSwitch) |
            static_cast<std::uint32_t>(UEFlag::kWheelZoom));

        // Main thread only: the input and menu sinks and the Update hook all run there.
        struct State
        {
            bool           owned = false;
            std::uint32_t  generation = 0;
            std::uint32_t  inputFrame = 0;
            Input::Builder input;

            // Per-second stats while owned.
            float         statTime = 0.0f;
            int           frames = 0;
            int           placed = 0;
            int           newPoses = 0;
            std::uint32_t lastPoseFrame = 0;
        } state;

        bool GamePaused()
        {
            auto* ui = RE::UI::GetSingleton();
            return ui && ui->GameIsPaused();
        }

        void Publish(bool a_paused = GamePaused())
        {
            auto& link = LinkService::Get();
            if (!link.IsUp()) {
                return;
            }
            link.WriteInput(state.input.Build(++state.inputFrame, state.owned, !a_paused));
        }

        void SetControls(bool a_enable)
        {
            if (auto* controls = RE::ControlMap::GetSingleton()) {
                controls->ToggleControls(kTakenControls, a_enable, false);
            }
        }

        void Release(const char* a_reason)
        {
            if (!state.owned) {
                return;
            }
            state.owned = false;
            state.input.ReleaseAll();
            SetControls(true);
            if (auto* player = RE::PlayerCharacter::GetSingleton(); player && player->IsSneaking()) {
                player->AsActorState()->actorState1.sneaking = false;
                player->NotifyAnimationGraph("SneakStop"sv);
            }
            Publish();
            SKSE::log::info("player sync: Skyrim owns the player ({})", a_reason);
        }

        void TakeOver(RE::PlayerCharacter* a_player)
        {
            if (!LinkService::Get().IsUp()) {
                SKSE::log::info("player sync: link is down, can't hand the player to MSC");
                return;
            }
            auto* cell = a_player->GetParentCell();
            if (!cell) {
                return;
            }

            const auto pos = a_player->GetPosition();
            Protocol::WorldspaceChange change{};
            auto* world = a_player->GetWorldspace();
            change.worldspace = world ? world->GetFormID() : 0;
            change.cell = cell->IsInteriorCell() ? cell->GetFormID() : 0;
            change.generation = ++state.generation;
            change.spawn[0] = pos.x;
            change.spawn[1] = pos.y;
            change.spawn[2] = pos.z;
            change.spawnHeading = a_player->GetAngleZ();
            if (!LinkService::Get().Send(Protocol::MessageType::WorldspaceChange, &change, sizeof(change))) {
                SKSE::log::warn("player sync: could not send the anchor to MSC");
                return;
            }

            state.owned = true;
            state.statTime = 0.0f;
            state.frames = state.placed = state.newPoses = 0;
            SetControls(false);
            Publish();
            SKSE::log::info(
                "player sync: MSC owns the player, anchor ({:.0f},{:.0f},{:.0f}) heading {:.3f} worldspace {:08X} cell {:08X} gen {}",
                pos.x, pos.y, pos.z, change.spawnHeading, change.worldspace, change.cell, change.generation);
        }

        void Place(RE::PlayerCharacter* a_player, const Protocol::PlayerState& a_pose)
        {
            a_player->SetPosition(RE::NiPoint3(a_pose.position[0], a_pose.position[1], a_pose.position[2]), true);
            a_player->SetHeading(a_pose.heading);
            a_player->data.angle.x = a_pose.pitch;
            // The native controller would otherwise carry momentum into the next frame.
            if (auto* controller = a_player->GetCharController()) {
                controller->SetLinearVelocityImpl(RE::hkVector4(0.0f, 0.0f, 0.0f, 0.0f));
            }

            // MSC crouch and prone both mean sneak; Skyrim's own sneak control is off, so set it here.
            const bool sneak = a_pose.stance != 0;
            if (auto* actorState = a_player->AsActorState(); actorState && actorState->IsSneaking() != sneak) {
                actorState->actorState1.sneaking = sneak;
                a_player->NotifyAnimationGraph(sneak ? "SneakStart"sv : "SneakStop"sv);
            }
        }

        void Drive(RE::PlayerCharacter* a_player, float a_delta)
        {
            auto& link = LinkService::Get();
            if (!link.IsUp()) {
                Release("link down");
                return;
            }
            Publish();

            Protocol::PlayerState pose{};
            const bool            valid = link.ReadPlayer(pose) && (pose.flags & Protocol::kPlayerValid) != 0;
            if (valid) {
                Place(a_player, pose);
                state.placed++;
                if (pose.frame != state.lastPoseFrame) {
                    state.newPoses++;
                    state.lastPoseFrame = pose.frame;
                }
            }

            state.frames++;
            state.statTime += a_delta;
            if (state.statTime >= 1.0f) {
                SKSE::log::info(
                    "player sync: frames={} placed={} new poses={} pos=({:.0f},{:.0f},{:.0f}) speed={:.1f} m/s heading={:.2f} pitch={:.2f} stance={}",
                    state.frames, state.placed, state.newPoses, pose.position[0], pose.position[1], pose.position[2],
                    std::hypot(pose.velocity[0], pose.velocity[1]) * Coords::kMetersPerUnit, pose.heading, pose.pitch, pose.stance);
                state.statTime = 0.0f;
                state.frames = state.placed = state.newPoses = 0;
            }
        }

        // PlayerCharacter::Update (vfunc 0xAD): run the native update, then override the pose.
        struct UpdateHook
        {
            static void thunk(RE::PlayerCharacter* a_player, float a_delta)
            {
                original(a_player, a_delta);
                if (state.owned && a_delta > 0.0f) {
                    Drive(a_player, a_delta);
                }
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        class InputSink : public RE::BSTEventSink<RE::InputEvent*>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(RE::InputEvent* const* a_event, RE::BSTEventSource<RE::InputEvent*>*) override
            {
                bool changed = false;
                for (auto* e = a_event ? *a_event : nullptr; e; e = e->next) {
                    if (auto* move = e->AsMouseMoveEvent()) {
                        state.input.AddMouse(move->mouseInputX, move->mouseInputY);
                        changed = true;
                        continue;
                    }
                    auto* button = e->AsButtonEvent();
                    if (!button) {
                        continue;
                    }
                    const auto code = button->GetIDCode();
                    switch (button->GetDevice()) {
                    case RE::INPUT_DEVICE::kKeyboard:
                        if (code == Input::kDikF10) {
                            if (button->IsDown()) {
                                Toggle();
                            }
                            continue;
                        }
                        state.input.SetKey(Input::KeyFromScanCode(code), button->IsPressed());
                        changed = true;
                        break;
                    case RE::INPUT_DEVICE::kMouse:
                        state.input.SetKey(Input::KeyFromMouseButton(code), button->IsPressed());
                        changed = true;
                        break;
                    default:
                        break;
                    }
                }
                if (changed && state.owned) {
                    Publish();
                }
                return RE::BSEventNotifyControl::kContinue;
            }

        private:
            static void Toggle()
            {
                if (state.owned) {
                    Release("F10");
                } else if (auto* player = RE::PlayerCharacter::GetSingleton()) {
                    TakeOver(player);
                }
            }
        };

        // A menu that pauses the game stops the Update hook, so publish the cleared input right away
        // instead of leaving MSC walking with whatever was held.
        class MenuSink : public RE::BSTEventSink<RE::MenuOpenCloseEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::MenuOpenCloseEvent* a_event, RE::BSTEventSource<RE::MenuOpenCloseEvent>*) override
            {
                if (state.owned && a_event) {
                    // The game may not count as paused yet while the menu is still opening.
                    bool pauses = false;
                    if (a_event->opening) {
                        auto* ui = RE::UI::GetSingleton();
                        auto  menu = ui ? ui->GetMenu(a_event->menuName) : nullptr;
                        pauses = menu && menu->PausesGame();
                    }
                    Publish(pauses || GamePaused());
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void Install()
    {
        REL::Relocation<std::uintptr_t> vtable{ RE::VTABLE_PlayerCharacter[0] };
        UpdateHook::original = vtable.write_vfunc(0xAD, UpdateHook::thunk);
        SKSE::log::info("player sync: PlayerCharacter::Update hooked, F10 hands the player to MSC");
    }

    void OnDataLoaded()
    {
        static InputSink inputSink;
        static MenuSink  menuSink;
        if (auto* input = RE::BSInputDeviceManager::GetSingleton()) {
            input->AddEventSink(&inputSink);
        }
        if (auto* ui = RE::UI::GetSingleton()) {
            ui->AddEventSink<RE::MenuOpenCloseEvent>(&menuSink);
        }
    }
}
