#include "player/PlayerSync.h"

#include "collision/CollisionSampler.h"
#include "coords.h"
#include "garage/Garage.h"
#include "input.h"
#include "link/LinkService.h"
#include "vehicle/VehicleView.h"

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

        // A jump this big between two frames means the game moved the player (fast travel, a
        // script), so MSC needs a new anchor.
        constexpr float kTeleportUnits = 2048.0f;

        // The trip to the garage on a save's first handover: arrived once the player is this close to
        // the spawn marker with its cell attached, then a few more frames so the cell's collision is
        // in the world before the sampler looks at it. Given up after the timeout (seconds).
        constexpr float kGarageArrivedUnits = 256.0f;
        constexpr int   kGarageSettleFrames = 30;
        constexpr float kGarageTimeout = 60.0f;

        // Main thread only: the input and menu sinks and the Update hook all run there.
        struct State
        {
            bool           owned = false;
            RE::TESObjectREFR* garage = nullptr;  // spawn marker while the player travels there for the first handover
            float              garageTime = 0.0f;
            int                garageFrames = 0;
            std::uint32_t  generation = 0;
            std::uint32_t  anchorWorldspace = 0;
            std::uint32_t  anchorCell = 0;
            RE::NiPoint3   lastPlaced;  // where the player was last put (anchor or MSC pose)
            bool           sneak = false;  // what we last told Skyrim; its own flag can drop on its own
            std::uint32_t  inputFrame = 0;
            Input::Builder input;
            float          eyeHeight = 0.0f;  // Skyrim camera above the player's feet on foot, units (0 until seen)
            bool           seated = false;

            // Per-second stats while owned.
            float         statTime = 0.0f;
            int           frames = 0;
            int           placed = 0;
            int           newPoses = 0;
            std::uint32_t lastPoseFrame = 0;
            float         vanShift = 0.0f;  // most the van was moved off MSC's pose to stay around the camera, units

            bool         cameraSeatKnown = false;
            // Skyrim's camera in the driven van's local frame, units. Kept across seatings and
            // handovers: it is the same seat, and a van carried to a new anchor may still be rolling.
            RE::NiPoint3 cameraSeat;
        } state;

        // The camera seat is learned only while the van is this slow (m/s), so the camera's lag at
        // speed never ends up in it.
        constexpr float kCameraSeatLearnSpeed = 0.5f;
        constexpr float kCameraSeatBlend = 0.1f;

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

        // Releases keys the devices no longer hold. A release event can get lost (a crash or menu at
        // the wrong moment), and MSC would then see the key held forever and never a new press, so
        // clicks on door handles and the like stop working until Skyrim loses focus.
        void ReleaseStuckKeys()
        {
            auto* devices = RE::BSInputDeviceManager::GetSingleton();
            if (!devices) {
                return;
            }
            if (auto* keyboard = devices->GetKeyboard()) {
                const auto& down = keyboard->GetRuntimeData().curState;
                for (std::uint32_t dik = 0; dik < 0x100; dik++) {
                    const auto key = Input::KeyFromScanCode(dik);
                    if ((down[dik] & 0x80) == 0 && state.input.IsKeyDown(key)) {
                        state.input.SetKey(key, false);
                        SKSE::log::info("player sync: released stuck key {} (scan code {:02X})", key, dik);
                    }
                }
            }
            if (auto* mouse = devices->GetMouse()) {
                const auto& buttons = mouse->GetRuntimeData().dInputNextState.rgbButtons;
                for (std::uint32_t b = 0; b < 7; b++) {
                    const auto key = Input::KeyFromMouseButton(b);
                    if ((buttons[b] & 0x80) == 0 && state.input.IsKeyDown(key)) {
                        state.input.SetKey(key, false);
                        SKSE::log::info("player sync: released stuck mouse button {}", b);
                    }
                }
            }
        }

        void SetControls(bool a_enable)
        {
            if (auto* controls = RE::ControlMap::GetSingleton()) {
                controls->ToggleControls(kTakenControls, a_enable, false);
            }
        }

        void SetSimulated(RE::PlayerCharacter* a_player, bool a_simulated);

        void Release(const char* a_reason)
        {
            if (!state.owned) {
                return;
            }
            state.owned = false;
            state.input.ReleaseAll();
            SetControls(true);
            if (auto* player = RE::PlayerCharacter::GetSingleton(); player && (state.sneak || player->IsSneaking())) {
                player->AsActorState()->actorState1.sneaking = false;
                player->NotifyAnimationGraph("SneakStop"sv);
            }
            state.sneak = false;
            if (auto* player = RE::PlayerCharacter::GetSingleton(); player && state.seated) {
                SetSimulated(player, true);
            }
            state.seated = false;
            CollisionSampler::Stop();
            VehicleView::Hide();
            Publish();
            SKSE::log::info("player sync: Skyrim owns the player ({})", a_reason);
        }

        std::uint32_t WorldspaceOf(RE::PlayerCharacter* a_player)
        {
            auto* world = a_player->GetWorldspace();
            return world ? world->GetFormID() : 0;
        }

        std::uint32_t InteriorOf(RE::TESObjectCELL* a_cell)
        {
            return a_cell && a_cell->IsInteriorCell() ? a_cell->GetFormID() : 0;
        }

        // Sends the player's spot as a new anchor (WorldspaceChange). MSC rebuilds its island
        // around it and holds the player until the ground there has arrived; poses for older
        // anchors are ignored from here on. With a_van, MSC parks the van there instead of next to
        // the player.
        bool SendAnchor(RE::PlayerCharacter* a_player, const char* a_reason, const Garage::Spot* a_van = nullptr)
        {
            auto* cell = a_player->GetParentCell();
            if (!cell) {
                return false;
            }
            const auto               pos = a_player->GetPosition();
            Protocol::WorldspaceChange change{};
            change.worldspace = WorldspaceOf(a_player);
            change.cell = InteriorOf(cell);
            change.generation = state.generation + 1;
            change.spawn[0] = pos.x;
            change.spawn[1] = pos.y;
            change.spawn[2] = pos.z;
            change.spawnHeading = a_player->GetAngleZ();
            if (a_van) {
                change.flags |= Protocol::kAnchorVehicleSpot;
                change.vehicleSpot[0] = a_van->position.x;
                change.vehicleSpot[1] = a_van->position.y;
                change.vehicleSpot[2] = a_van->position.z;
                change.vehicleHeading = a_van->heading;
            }
            if (!LinkService::Get().Send(Protocol::MessageType::WorldspaceChange, &change, sizeof(change))) {
                SKSE::log::warn("player sync: could not send the anchor to MSC");
                return false;
            }

            state.generation = change.generation;
            state.anchorWorldspace = change.worldspace;
            state.anchorCell = change.cell;
            state.lastPlaced = pos;
            CollisionSampler::Reset(change.generation, change.worldspace);
            SKSE::log::info(
                "player sync: anchor gen {} ({}) at ({:.0f},{:.0f},{:.0f}) heading {:.3f} worldspace {:08X} cell {:08X}",
                change.generation, a_reason, pos.x, pos.y, pos.z, change.spawnHeading, change.worldspace, change.cell);
            return true;
        }

        void TakeOver(RE::PlayerCharacter* a_player, const Garage::Spot* a_van = nullptr)
        {
            if (!LinkService::Get().IsUp()) {
                SKSE::log::info("player sync: link is down, can't hand the player to MSC");
                return;
            }
            if (!SendAnchor(a_player, a_van ? "garage" : "handover", a_van)) {
                return;
            }

            state.owned = true;
            state.statTime = 0.0f;
            state.frames = state.placed = state.newPoses = 0;
            SetControls(false);
            Publish();
            SKSE::log::info("player sync: MSC owns the player");
        }

        // A save's first handover starts at the garage: move the player to its spawn marker, and
        // hand over once it has arrived (TravelToGarage).
        void HandOver(RE::PlayerCharacter* a_player)
        {
            auto* marker = Garage::FirstVisit();
            if (!marker) {
                TakeOver(a_player);
                return;
            }
            if (!LinkService::Get().IsUp()) {
                SKSE::log::info("player sync: link is down, can't hand the player to MSC");
                return;
            }
            state.garage = marker;
            state.garageTime = 0.0f;
            state.garageFrames = 0;
            a_player->MoveTo(marker);
            SKSE::log::info("player sync: first handover of this save, moving the player to the garage");
        }

        void TravelToGarage(RE::PlayerCharacter* a_player, float a_delta)
        {
            state.garageTime += a_delta;
            if (!LinkService::Get().IsUp() || state.garageTime > kGarageTimeout) {
                SKSE::log::warn("player sync: gave up on the garage ({})", LinkService::Get().IsUp() ? "timed out" : "link down");
                state.garage = nullptr;
                return;
            }
            auto*      cell = a_player->GetParentCell();
            const bool there = cell && cell->IsAttached() && a_player->Is3DLoaded() &&
                               a_player->GetPosition().GetDistance(state.garage->GetPosition()) < kGarageArrivedUnits;
            if (!there || ++state.garageFrames < kGarageSettleFrames) {
                return;
            }

            a_player->SetHeading(state.garage->GetAngleZ());
            state.garage = nullptr;
            const auto van = Garage::Van();
            TakeOver(a_player, van ? &*van : nullptr);
            if (state.owned) {
                Garage::Visited();
            }
        }

        // A load door, fast travel or a script moved the player somewhere MSC's island doesn't cover.
        const char* MovedByGame(RE::PlayerCharacter* a_player)
        {
            auto* cell = a_player->GetParentCell();
            if (!cell) {
                return nullptr;  // loading
            }
            if (WorldspaceOf(a_player) != state.anchorWorldspace || InteriorOf(cell) != state.anchorCell) {
                return "worldspace or cell changed";
            }
            if (a_player->GetPosition().GetDistance(state.lastPlaced) > kTeleportUnits) {
                return "teleported";
            }
            return nullptr;
        }

        // MSC's camera sits this far above the feet it sends, standing and seated alike (measured;
        // sitting moves its whole capsule down). Skyrim's eyes are higher above the feet, which is
        // harmless on foot but puts them over the van's roof when seated.
        constexpr float kMscEyeHeight = static_cast<float>(0.85 * Coords::kUnitsPerMeter);

        void MeasureEye(RE::PlayerCharacter* a_player)
        {
            auto* camera = RE::PlayerCamera::GetSingleton();
            if (camera && camera->cameraRoot && camera->IsInFirstPerson()) {
                state.eyeHeight = camera->cameraRoot->world.translate.z - a_player->GetPosition().z;
            }
        }

        // Seated, the lowered capsule sits in the ground, so Havok would push it out against the
        // placement. Like a rider's, it isn't simulated while seated; the player is placed every
        // frame anyway.
        void SetSimulated(RE::PlayerCharacter* a_player, bool a_simulated)
        {
            if (auto* controller = a_player->GetCharController()) {
                if (a_simulated) {
                    controller->flags.reset(RE::CHARACTER_FLAGS::kNoSim);
                } else {
                    controller->flags.set(RE::CHARACTER_FLAGS::kNoSim);
                }
            }
        }

        // Seated, put Skyrim's camera where MSC's is.
        void LowerIntoSeat(Protocol::PlayerState& a_pose)
        {
            if (state.eyeHeight > kMscEyeHeight) {
                a_pose.position[2] -= state.eyeHeight - kMscEyeHeight;
            }
        }

        void Place(RE::PlayerCharacter* a_player, const Protocol::PlayerState& a_pose)
        {
            state.lastPlaced = RE::NiPoint3(a_pose.position[0], a_pose.position[1], a_pose.position[2]);
            a_player->SetPosition(state.lastPlaced, true);
            a_player->SetHeading(a_pose.heading);
            a_player->data.angle.x = a_pose.pitch;
            // The native controller would otherwise carry momentum into the next frame.
            if (auto* controller = a_player->GetCharController()) {
                controller->SetLinearVelocityImpl(RE::hkVector4(0.0f, 0.0f, 0.0f, 0.0f));
            }

            // MSC crouch and prone both mean sneak; Skyrim's own sneak control is off, so set it here.
            // Compared against what we sent last, not IsSneaking(): Skyrim can clear its flag while
            // the animation graph stays in sneak, and then the stop would never be sent.
            const bool sneak = a_pose.stance != 0;
            auto*      actorState = a_player->AsActorState();
            if (actorState && sneak != state.sneak) {
                state.sneak = sneak;
                a_player->NotifyAnimationGraph(sneak ? "SneakStart"sv : "SneakStop"sv);
                SKSE::log::info("player sync: sneak {}", sneak ? "on" : "off");
            }
            if (actorState && actorState->IsSneaking() != sneak) {
                actorState->actorState1.sneaking = sneak;
            }
        }

        // Skyrim builds its camera before this hook, from where the player was put the frame before,
        // and smooths small moves on top, so a driven van drawn at MSC's pose jittered around it and
        // fell behind at speed. Instead the van is hung around the camera, at the camera's spot in
        // the van learned while it is nearly stopped: like a rider on a horse, the camera can't move
        // inside it.
        void LockVanToCamera(Protocol::VehicleState& a_vehicle)
        {
            auto* camera = RE::PlayerCamera::GetSingleton();
            if (!camera || !camera->cameraRoot) {
                return;
            }
            const auto         at = camera->cameraRoot->world.translate;
            const RE::NiPoint3 van(a_vehicle.position[0], a_vehicle.position[1], a_vehicle.position[2]);
            float              m[3][3];
            Coords::ToMatrix({ a_vehicle.rotation[0], a_vehicle.rotation[1], a_vehicle.rotation[2], a_vehicle.rotation[3] }, m);
            RE::NiMatrix3 rotate;
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    rotate.entry[r][c] = m[r][c];
                }
            }

            const float speed = std::hypot(a_vehicle.velocity[0], a_vehicle.velocity[1], a_vehicle.velocity[2]) *
                                static_cast<float>(Coords::kMetersPerUnit);
            if (speed < kCameraSeatLearnSpeed) {
                const auto local = rotate.Transpose() * (at - van);
                state.cameraSeat = state.cameraSeatKnown ? state.cameraSeat + (local - state.cameraSeat) * kCameraSeatBlend : local;
                state.cameraSeatKnown = true;
            }
            if (!state.cameraSeatKnown) {
                return;
            }
            const auto locked = at - rotate * state.cameraSeat;
            state.vanShift = (std::max)(state.vanShift, locked.GetDistance(van));
            a_vehicle.position[0] = locked.x;
            a_vehicle.position[1] = locked.y;
            a_vehicle.position[2] = locked.z;
        }

        void Drive(RE::PlayerCharacter* a_player, float a_delta)
        {
            auto& link = LinkService::Get();
            if (!link.IsUp()) {
                Release("link down");
                return;
            }
            ReleaseStuckKeys();
            Publish();

            if (const char* reason = MovedByGame(a_player)) {
                SendAnchor(a_player, reason);  // retried next frame if the ring is full
            }

            // A pose for an older anchor would put the player back where it came from.
            Protocol::PlayerState pose{};
            const bool            valid = link.ReadPlayer(pose) && (pose.flags & Protocol::kPlayerValid) != 0 &&
                               pose.generation == state.generation;
            Protocol::VehicleState vehicle{};
            const bool             hasVehicle = link.ReadVehicle(vehicle);
            const bool             seated = valid && hasVehicle && (vehicle.flags & Protocol::kVehicleDriven) != 0 &&
                                vehicle.generation == state.generation;
            if (seated != state.seated) {
                state.seated = seated;
                SetSimulated(a_player, !seated);
                SKSE::log::info("player sync: {} (Skyrim eye {:.0f} units, MSC eye {:.0f})", seated ? "seated, lowering the player" : "out of the seat",
                    state.eyeHeight, kMscEyeHeight);
            }
            if (valid) {
                if (seated) {
                    LowerIntoSeat(pose);
                } else if (pose.stance == 0) {
                    MeasureEye(a_player);
                }
                Place(a_player, pose);
                state.placed++;
                if (pose.frame != state.lastPoseFrame) {
                    state.newPoses++;
                    state.lastPoseFrame = pose.frame;
                }
            }
            if (seated) {
                LockVanToCamera(vehicle);
            }
            VehicleView::Update(hasVehicle ? &vehicle : nullptr, state.generation);
            // Stream ahead of the player while it moves fast (driving).
            const RE::NiPoint3 velocity = valid ? RE::NiPoint3(pose.velocity[0], pose.velocity[1], pose.velocity[2]) : RE::NiPoint3();
            CollisionSampler::Update(a_player, velocity);

            state.frames++;
            state.statTime += a_delta;
            if (state.statTime >= 1.0f) {
                SKSE::log::info(
                    "player sync: frames={} placed={} new poses={} pos=({:.0f},{:.0f},{:.0f}) speed={:.1f} m/s heading={:.2f} pitch={:.2f} stance={} seated={} van shift={:.1f}",
                    state.frames, state.placed, state.newPoses, pose.position[0], pose.position[1], pose.position[2],
                    std::hypot(pose.velocity[0], pose.velocity[1]) * Coords::kMetersPerUnit, pose.heading, pose.pitch, pose.stance,
                    state.seated, state.vanShift);
                state.vanShift = 0.0f;
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
                if (state.garage && a_delta > 0.0f) {
                    TravelToGarage(a_player, a_delta);
                } else if (state.owned && a_delta > 0.0f) {
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
                } else if (state.garage) {
                    state.garage = nullptr;
                    SKSE::log::info("player sync: F10 again on the way to the garage, not handing over");
                } else if (auto* player = RE::PlayerCharacter::GetSingleton()) {
                    HandOver(player);
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
