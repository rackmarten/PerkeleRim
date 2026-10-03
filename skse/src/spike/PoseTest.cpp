#include "spike/PoseTest.h"

namespace PerkeleRim::Spike::PoseTest
{
    namespace
    {
        enum class Mode
        {
            Off,
            Place,       // Actor::SetPosition only
            PlaceStill,  // SetPosition + heading + zeroed character controller velocity
        };

        constexpr const char* ModeName(Mode a_mode)
        {
            switch (a_mode) {
            case Mode::Place:
                return "Place";
            case Mode::PlaceStill:
                return "PlaceStill";
            default:
                return "Off";
            }
        }

        constexpr std::uint32_t kToggleKey = 0x44;  // DIK_F10
        constexpr float         kRadius = 300.0f;   // game units, about 4 m
        constexpr float         kPeriod = 10.0f;    // seconds per lap

        // Main thread only: the input sink and the Update hook both run there.
        struct State
        {
            Mode         mode = Mode::Off;
            RE::NiPoint3 center;
            float        elapsed = 0.0f;
            bool         placed = false;
            RE::NiPoint3 lastPlaced;

            // Per-second stats.
            float statTime = 0.0f;
            int   frames = 0;
            float driftSum = 0.0f;
            float driftMax = 0.0f;
        } state;

        void ResetStats()
        {
            state.statTime = 0.0f;
            state.frames = 0;
            state.driftSum = 0.0f;
            state.driftMax = 0.0f;
        }

        void SetMode(RE::PlayerCharacter* a_player, Mode a_mode)
        {
            state.mode = a_mode;
            state.placed = false;
            state.elapsed = 0.0f;
            ResetStats();
            if (a_mode != Mode::Off) {
                // Circle starts at the player's current spot.
                state.center = a_player->GetPosition();
                state.center.x -= kRadius;
            }
            SKSE::log::info("pose test: mode {}", ModeName(a_mode));
        }

        void Drive(RE::PlayerCharacter* a_player, float a_delta)
        {
            // Drift: how far the native update moved the player since we placed it last frame.
            if (state.placed) {
                const float drift = a_player->GetPosition().GetDistance(state.lastPlaced);
                state.driftSum += drift;
                state.driftMax = (std::max)(state.driftMax, drift);
            }

            state.elapsed += a_delta;
            const float  angle = state.elapsed / kPeriod * RE::NI_TWO_PI;
            RE::NiPoint3 target = state.center;
            target.x += kRadius * std::cos(angle);
            target.y += kRadius * std::sin(angle);
            target.z = a_player->GetPositionZ();  // keep whatever height Skyrim resolves; no terrain sampling yet

            a_player->SetPosition(target, true);
            if (state.mode == Mode::PlaceStill) {
                // Face along the circle (heading 0 = +Y, clockwise), and stop the controller carrying momentum.
                a_player->SetHeading(-angle);
                if (auto* controller = a_player->GetCharController()) {
                    controller->SetLinearVelocityImpl(RE::hkVector4(0.0f, 0.0f, 0.0f, 0.0f));
                }
            }
            state.lastPlaced = a_player->GetPosition();
            state.placed = true;

            state.frames++;
            state.statTime += a_delta;
            if (state.statTime >= 1.0f) {
                SKSE::log::info(
                    "pose test: mode={} frames={} drift avg={:.2f} max={:.2f} units pos=({:.0f},{:.0f},{:.0f})",
                    ModeName(state.mode), state.frames, state.driftSum / (std::max)(state.frames - 1, 1), state.driftMax,
                    target.x, target.y, state.lastPlaced.z);
                ResetStats();
            }
        }

        // PlayerCharacter::Update (vfunc 0xAD): run the native update, then override the pose.
        struct UpdateHook
        {
            static void thunk(RE::PlayerCharacter* a_player, float a_delta)
            {
                original(a_player, a_delta);
                if (state.mode != Mode::Off && a_delta > 0.0f) {
                    Drive(a_player, a_delta);
                }
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        class ToggleSink : public RE::BSTEventSink<RE::InputEvent*>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(RE::InputEvent* const* a_event, RE::BSTEventSource<RE::InputEvent*>*) override
            {
                for (auto* e = a_event ? *a_event : nullptr; e; e = e->next) {
                    auto* button = e->AsButtonEvent();
                    if (!button || button->GetDevice() != RE::INPUT_DEVICE::kKeyboard) {
                        continue;
                    }
                    if (button->GetIDCode() == kToggleKey && button->IsDown()) {
                        if (auto* player = RE::PlayerCharacter::GetSingleton()) {
                            SetMode(player, static_cast<Mode>((static_cast<int>(state.mode) + 1) % 3));
                        }
                    }
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void Install()
    {
        REL::Relocation<std::uintptr_t> vtable{ RE::VTABLE_PlayerCharacter[0] };
        UpdateHook::original = vtable.write_vfunc(0xAD, UpdateHook::thunk);
        SKSE::log::info("pose test: PlayerCharacter::Update hooked, F10 cycles modes");
    }

    void OnDataLoaded()
    {
        static ToggleSink sink;
        if (auto* input = RE::BSInputDeviceManager::GetSingleton()) {
            input->AddEventSink(&sink);
        }
    }
}
