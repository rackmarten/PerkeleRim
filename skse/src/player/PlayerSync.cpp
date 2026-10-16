#include "player/PlayerSync.h"

#include "camera.h"
#include "collision/CollisionSampler.h"
#include "combat/Combat.h"
#include "coords.h"
#include "death/Death.h"
#include "drink/Drink.h"
#include "garage/Garage.h"
#include "hud/Hud.h"
#include "input.h"
#include "hands/HandView.h"
#include "item/ItemView.h"
#include "link/LinkService.h"
#include "look_at.h"
#include "model/ModelView.h"
#include "pocket/Pocket.h"
#include "vehicle/VehicleView.h"

namespace PerkeleRim::PlayerSync
{
    namespace
    {
        using UEFlag = RE::UserEvents::USER_EVENT_FLAG;

        // Skyrim controls earlier versions turned off while MSC owned the player. Now they all stay on:
        // Skyrim treats a player with these off like one in a scripted scene, and enemies saw it,
        // walked up and never fought. Instead the input handlers refuse while MSC has the player
        // (BlockHook). Still turned back on at release and after loading, for saves taken back then.
        constexpr auto kOldControls = static_cast<UEFlag>(
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
        // Resuming the handover after loading a save taken while MSC had the player: once the
        // player's cell is attached, the same settle frames, given up after the timeout (seconds).
        constexpr float kResumeTimeout = 30.0f;

        // Main thread only: the input and menu sinks and the Update hook all run there.
        struct State
        {
            bool           owned = false;
            RE::TESObjectREFR* garage = nullptr;  // spawn marker while the player travels there for the first handover
            float              garageTime = 0.0f;
            int                garageFrames = 0;
            bool               resume = false;  // hand over again once the loaded save's player is in its cell
            float              resumeTime = 0.0f;
            int                resumeFrames = 0;
            std::uint32_t  generation = 0;
            std::uint32_t  anchorWorldspace = 0;
            std::uint32_t  anchorCell = 0;
            RE::NiPoint3   lastPlaced;  // where the player was last put (anchor or MSC pose)
            bool           sneak = false;  // what we last told Skyrim; its own flag can drop on its own
            int            sneakResend = 0;  // frames until a sneak event the graph ignored is sent again
            std::uint32_t  inputFrame = 0;
            Input::Builder input;
            bool           seated = false;
            RE::NiPoint3   eye;               // Skyrim's camera relative to the placed feet (MSC's eye), 0 when not posed
            float          savedFov = 0.0f;   // Skyrim's worldFOV before MSC's took over, 0 = untouched
            std::string    lookAt;            // MSC's interaction text
            bool           lookAtUse = false; // MSC shows its hand icon
            std::string    subtitle;          // MSC's subtitle line
            bool           hasNeeds = false;  // needs below came from MSC since the handover
            std::array<float, 7> needs{};     // PlayerState.needs
            bool           cameraParentLogged = false;

            // Per-second stats while owned.
            float         statTime = 0.0f;
            int           frames = 0;
            int           placed = 0;
            int           newPoses = 0;
            std::uint32_t lastPoseFrame = 0;
            float         vanShift = 0.0f;  // most the van was moved off MSC's pose to stay around the camera, units

            bool         cameraSeatKnown = false;
            bool         chase = false;  // MSC's chase camera was on last frame
            // Skyrim's camera in the driven van's local frame, units. Kept across seatings and
            // handovers: it is the same seat, and a van carried to a new anchor may still be rolling.
            RE::NiPoint3 cameraSeat;
        } state;

        // The camera seat is learned only while the van is this slow (m/s), so the camera's lag at
        // speed never ends up in it.
        constexpr float kCameraSeatLearnSpeed = 0.5f;
        constexpr float kCameraSeatBlend = 0.1f;
        // ...and while the view is level: MSC moves the seated player when it leans (Q/E), and a lean
        // learned into the seat would shift the van under the camera and stay there once driving.
        constexpr float kCameraSeatLearnRoll = 0.03f;  // radians against the van, about 2 degrees

        bool Finite(const Protocol::PlayerState& a_pose)
        {
            for (int i = 0; i < 3; ++i) {
                if (!std::isfinite(a_pose.position[i]) || !std::isfinite(a_pose.velocity[i]) || !std::isfinite(a_pose.eye[i])) {
                    return false;
                }
            }
            return std::isfinite(a_pose.heading) && std::isfinite(a_pose.pitch) && std::isfinite(a_pose.roll) && std::isfinite(a_pose.fov);
        }

        // A Skyrim menu has the input: the game is paused, or it shows its cursor (dialogue, which
        // doesn't pause). MSC then gets no keys and its mouse stands still.
        bool MenuHasInput()
        {
            auto* ui = RE::UI::GetSingleton();
            return ui && (ui->GameIsPaused() || ui->IsMenuOpen(RE::CursorMenu::MENU_NAME));
        }

        // A menu pauses the game (Escape, the map, inventories): MSC freezes its time as well.
        bool GamePaused()
        {
            auto* ui = RE::UI::GetSingleton();
            return ui && ui->GameIsPaused();
        }

        void Publish(bool a_menu = MenuHasInput(), bool a_paused = GamePaused())
        {
            auto& link = LinkService::Get();
            if (!link.IsUp()) {
                return;
            }
            auto       input = state.input.Build(++state.inputFrame, state.owned, !a_menu, a_paused);
            const auto screen = RE::BSGraphics::Renderer::GetScreenSize();
            input.screenWidth = screen.width;  // MSC renders its hand layer this size
            input.screenHeight = screen.height;
            link.WriteInput(input);
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
                controls->ToggleControls(kOldControls, a_enable, false);
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
            state.chase = false;
            state.eye = {};
            if (state.savedFov > 0.0f) {
                if (auto* camera = RE::PlayerCamera::GetSingleton()) {
                    camera->GetRuntimeData2().worldFOV = state.savedFov;
                }
                state.savedFov = 0.0f;
            }
            state.lookAt.clear();
            state.lookAtUse = false;
            state.subtitle.clear();
            state.hasNeeds = false;
            Hud::Set({});
            Drink::SetDrunk(0.0f, 0.0f);
            CollisionSampler::Stop();
            Combat::Stop();
            VehicleView::Hide();
            ItemView::Clear();
            HandView::Hide();
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
            Death::Hide();
            Death::OnHandover();
            state.statTime = 0.0f;
            state.frames = state.placed = state.newPoses = 0;
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

        void Resume(RE::PlayerCharacter* a_player, float a_delta)
        {
            state.resumeTime += a_delta;
            if (!LinkService::Get().IsUp() || state.resumeTime > kResumeTimeout) {
                SKSE::log::warn("player sync: gave up handing the loaded save's player back to MSC ({})", LinkService::Get().IsUp() ? "timed out" : "link down");
                state.resume = false;
                return;
            }
            auto*      cell = a_player->GetParentCell();
            const bool there = cell && cell->IsAttached() && a_player->Is3DLoaded();
            if (!there || ++state.resumeFrames < kGarageSettleFrames) {
                return;
            }
            state.resume = false;
            TakeOver(a_player);
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

        // Seated, the capsule sits in the van's floor and the ground, so Havok would push it out
        // against the placement. Like a rider's, it isn't simulated while seated; the player is placed every
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
            // The graph drops a sneak event that comes while it is still blending the previous one
            // (crouch and stand up quickly), and then stays crouched; send it again until it agrees.
            bool graphSneak = sneak;
            if (a_player->GetGraphVariableBool("IsSneaking"sv, graphSneak) && graphSneak != sneak) {
                if (--state.sneakResend <= 0) {
                    state.sneakResend = 15;
                    a_player->NotifyAnimationGraph(sneak ? "SneakStart"sv : "SneakStop"sv);
                    SKSE::log::info("player sync: animation still {} sneaking, sneak {} sent again", graphSneak ? "" : "not", sneak ? "on" : "off");
                }
            } else {
                state.sneakResend = 0;
            }
        }

        // Puts Skyrim's first-person camera at MSC's: the placed feet plus MSC's eye, turned to MSC's
        // heading, pitch and roll, so leaning, reaching to the door and crouching show. Skyrim
        // builds its camera before this hook (from the previous frame's placement, pivoting around
        // the head, smoothed), so this overrides the finished camera node; a_fromHeading and
        // a_fromPitch are the angles Skyrim built it for. MSC's zoom becomes Skyrim's field of view.
        // MSC's chase camera (kPlayerChaseCamera) comes the same way, far from the feet and turned
        // its own way.
        void PoseCamera(const Protocol::PlayerState& a_pose, float a_fromHeading, float a_fromPitch)
        {
            auto* camera = RE::PlayerCamera::GetSingleton();
            auto* root = camera ? camera->cameraRoot.get() : nullptr;
            if (!root || !camera->IsInFirstPerson()) {
                state.eye = {};
                return;
            }

            Camera::Matrix built, posed;
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    built[r][c] = root->world.rotate.entry[r][c];
                }
            }
            const bool chase = (a_pose.flags & Protocol::kPlayerChaseCamera) != 0;
            Camera::Retarget(built, a_fromHeading, a_fromPitch, chase ? a_pose.viewHeading : a_pose.heading,
                chase ? a_pose.viewPitch : a_pose.pitch, a_pose.roll, posed);
            RE::NiMatrix3 rotate;
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    rotate.entry[r][c] = posed[r][c];
                }
            }
            state.eye = RE::NiPoint3(a_pose.eye[0], a_pose.eye[1], a_pose.eye[2]);
            const auto at = state.lastPlaced + state.eye;

            // Update keeps the node's local transform, so give it the wanted world one in its
            // parent's frame (scale 1).
            if (auto* parent = root->parent) {
                const auto toParent = parent->world.rotate.Transpose();
                root->local.rotate = toParent * rotate;
                root->local.translate = toParent * (at - parent->world.translate);
                if (!state.cameraParentLogged) {
                    state.cameraParentLogged = true;
                    SKSE::log::info("player sync: camera root hangs under {}", parent->name.c_str());
                }
            } else {
                root->local.rotate = rotate;
                root->local.translate = at;
            }
            RE::NiUpdateData update{};
            root->Update(update);

            if (a_pose.fov > 1.0f) {
                auto& data = camera->GetRuntimeData2();
                if (state.savedFov <= 0.0f) {
                    state.savedFov = data.worldFOV;
                }
                data.worldFOV = Camera::SkyrimFov(a_pose.fov);
            }
        }

        // Skyrim builds its camera before this hook, from where the player was put the frame before,
        // and smooths small moves on top, so a driven van drawn at MSC's pose jittered around it and
        // fell behind at speed. Instead the van is hung around the camera, at the camera's spot in
        // the van learned while it is nearly stopped: like a rider on a horse, the camera can't move
        // inside it. What is learned is the camera minus MSC's eye (PoseCamera), never while leaning, so
        // leaning moves the camera inside the van instead of dragging the van along.
        void LockVanToCamera(Protocol::VehicleState& a_vehicle, float a_roll)
        {
            auto* camera = RE::PlayerCamera::GetSingleton();
            if (!camera || !camera->cameraRoot) {
                return;
            }
            const auto         at = camera->cameraRoot->world.translate - state.eye;
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
            // Leaning = the view tilted against the van (the van may stand on a slope): its right side
            // dropping is a tilt to the right, like PlayerState roll.
            const float vanRoll = std::asin(std::clamp(-m[2][0], -1.0f, 1.0f));
            if (speed < kCameraSeatLearnSpeed && std::abs(a_roll - vanRoll) < kCameraSeatLearnRoll) {
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

        // The player died while MSC had it (Skyrim combat, or a hit Death says kills): MSC plays its
        // death sound, Skyrim takes the player back so its own death can ragdoll it and reload, and
        // MSC's blood goes over the view. MSC's player stays alive.
        void Die(RE::PlayerCharacter* a_player)
        {
            const Protocol::PlayerDeath death{};
            if (!LinkService::Get().Send(Protocol::MessageType::PlayerDeath, &death, sizeof(death))) {
                SKSE::log::warn("player sync: could not tell MSC the player died");
            }
            Release("the player died");
            Death::Show();
            if (Death::Lethal()) {
                Death::Kill(a_player);
            }
        }

        void Drive(RE::PlayerCharacter* a_player, float a_delta)
        {
            auto& link = LinkService::Get();
            if (!link.IsUp()) {
                Release("link down");
                return;
            }
            if (a_player->IsDead()) {
                Die(a_player);
                return;
            }
            ReleaseStuckKeys();
            Publish();
            ModelView::Tick(a_delta);

            if (const char* reason = MovedByGame(a_player)) {
                SendAnchor(a_player, reason);  // retried next frame if the ring is full
            }

            // A pose for an older anchor would put the player back where it came from.
            Protocol::PlayerState pose{};
            // A pose that isn't finite (a NaN out of MSC's physics) would put the camera at NaN and
            // wreck Skyrim's rendering, so it's skipped like an invalid one.
            const bool            valid = link.ReadPlayer(pose) && (pose.flags & Protocol::kPlayerValid) != 0 &&
                               pose.generation == state.generation && Finite(pose);
            Protocol::VehicleState vehicle{};
            const bool             hasVehicle = link.ReadVehicle(vehicle);
            const bool             seated = valid && hasVehicle && (vehicle.flags & Protocol::kVehicleDriven) != 0 &&
                                vehicle.generation == state.generation;
            if (seated != state.seated) {
                state.seated = seated;
                SetSimulated(a_player, !seated);
                SKSE::log::info("player sync: {}", seated ? "seated" : "out of the seat");
            }
            link.Receive([a_player](Protocol::MessageType a_type, const std::uint8_t* a_data, std::uint32_t a_size) {
                if (a_type == Protocol::MessageType::ModelChunk) {
                    ModelView::OnChunk(a_data, a_size);
                    return;
                }
                if (a_type == Protocol::MessageType::PlayerDamage) {
                    Death::OnDamage(a_player, a_data, a_size);
                    return;
                }
                if (a_type == Protocol::MessageType::PunchEvent) {
                    Combat::OnPunch(a_player, a_data, a_size, state.hasNeeds ? state.needs[Protocol::kNeedDrunk] : 0.0f);
                    return;
                }
                if (a_type == Protocol::MessageType::ImpactEvent) {
                    Combat::OnImpact(a_player, a_data, a_size);
                    return;
                }
                if (a_type == Protocol::MessageType::Consumed) {
                    Death::OnConsumed(a_player, a_data, a_size);
                    return;
                }
                if (a_type == Protocol::MessageType::PocketChange) {
                    Pocket::OnChange(a_player, a_data, a_size);
                    return;
                }
                if (a_type != Protocol::MessageType::LookAtText && a_type != Protocol::MessageType::Subtitle) {
                    return;
                }
                const auto text = LookAt::Decode(a_data, a_size);  // a Subtitle has the same payload
                if (!text) {
                    SKSE::log::warn("player sync: bad text message type {} ({} bytes)", static_cast<int>(a_type), a_size);
                } else if (a_type == Protocol::MessageType::Subtitle) {
                    state.subtitle.assign(text->text);
                } else {
                    state.lookAt.assign(text->text);
                    state.lookAtUse = (text->flags & Protocol::kLookAtUse) != 0;
                }
            });
            if (valid) {
                // The angles Skyrim built this frame's camera for, before Place changes them.
                const float fromHeading = a_player->GetAngleZ();
                const float fromPitch = a_player->data.angle.x;
                Place(a_player, pose);
                if ((pose.flags & Protocol::kPlayerNeeds) != 0) {
                    std::copy(std::begin(pose.needs), std::end(pose.needs), state.needs.begin());
                    state.hasNeeds = true;
                }
                if ((pose.flags & Protocol::kPlayerCamera) != 0) {
                    PoseCamera(pose, fromHeading, fromPitch);
                } else {
                    state.eye = {};
                }
                state.placed++;
                if (pose.frame != state.lastPoseFrame) {
                    state.newPoses++;
                    state.lastPoseFrame = pose.frame;
                }
            }
            Death::OnPose(a_player, valid ? &pose : nullptr, seated);
            if (Death::Lethal()) {
                Die(a_player);
                return;
            }
            // With MSC's chase camera on, the camera is outside and the van is drawn where MSC has it.
            const bool chase = valid && (pose.flags & Protocol::kPlayerChaseCamera) != 0;
            if (chase != state.chase) {
                state.chase = chase;
                SKSE::log::info("player sync: chase camera {}", chase ? "on" : "off");
            }
            if (seated && !chase) {
                LockVanToCamera(vehicle, pose.roll);
            }
            Protocol::VehicleParts parts{};
            const bool             hasParts = hasVehicle && link.ReadVehicleParts(parts);
            VehicleView::Update(hasVehicle ? &vehicle : nullptr, hasParts ? &parts : nullptr, state.generation);
            Protocol::ItemState items{};
            if (link.ReadItems(items)) {  // a torn read keeps last frame's items; link down releases
                ItemView::Update(&items, state.generation);
            }
            Protocol::HandLayer hands{};
            HandView::Update(link.ReadHandLayer(hands) ? &hands : nullptr, a_delta);
            Combat::Update(a_player, state.generation, a_delta);
            const bool menu = MenuHasInput();
            Combat::Guard(a_player, valid && !seated && !menu && (pose.flags & Protocol::kPlayerBlocking) != 0, !menu);
            Combat::Shouts(a_player);
            // Stream ahead of the player while it moves fast (driving).
            const RE::NiPoint3 velocity = valid ? RE::NiPoint3(pose.velocity[0], pose.velocity[1], pose.velocity[2]) : RE::NiPoint3();
            CollisionSampler::Update(a_player, velocity);

            state.frames++;
            state.statTime += a_delta;
            const bool second = state.statTime >= 1.0f;
            Hud::View hud;
            hud.show = !chase;  // clean shots
            hud.label = state.lookAt;
            hud.hand = state.lookAtUse;
            hud.subtitle = state.subtitle;
            hud.hasNeeds = state.hasNeeds;
            hud.needs = state.needs;
            hud.pocket = Pocket::Hint();
            hud.handsImage = HandView::Image();
            hud.handsWidth = HandView::Width();
            hud.handsHeight = HandView::Height();
            hud.handsLight = HandView::Light(a_player);
            Hud::Set(hud, second);  // refreshed once a second in case the HUD movie was rebuilt
            Drink::SetDrunk(state.hasNeeds ? state.needs[Protocol::kNeedDrunk] : 0.0f, a_delta);
            if (second) {
                SKSE::log::info(
                    "player sync: frames={} placed={} new poses={} pos=({:.0f},{:.0f},{:.0f}) speed={:.1f} m/s heading={:.2f} pitch={:.2f} stance={} seated={} van shift={:.1f} eye=({:.0f},{:.0f},{:.0f}) roll={:.2f} fov={:.0f}",
                    state.frames, state.placed, state.newPoses, pose.position[0], pose.position[1], pose.position[2],
                    std::hypot(pose.velocity[0], pose.velocity[1]) * Coords::kMetersPerUnit, pose.heading, pose.pitch, pose.stance,
                    state.seated, state.vanShift, pose.eye[0], pose.eye[1], pose.eye[2], pose.roll, pose.fov);
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
                Death::Tick();
                Pocket::Update(a_player, a_delta);
                if (state.garage && a_delta > 0.0f) {
                    TravelToGarage(a_player, a_delta);
                } else if (state.resume && !state.owned && a_delta > 0.0f) {
                    Resume(a_player, a_delta);
                } else if (state.owned && a_delta > 0.0f) {
                    Drive(a_player, a_delta);
                }
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // E is Skyrim's activate and MSC's lean right (and MSC's use key on some things). While MSC's
        // crosshair is on something of its own (its prompt or hand icon is up), E is MSC's alone;
        // otherwise it activates in Skyrim: doors, people, quest items.
        struct ActivateHook
        {
            static bool thunk(RE::ActivateHandler* a_handler, RE::InputEvent* a_event)
            {
                if (state.owned && (!state.lookAt.empty() || state.lookAtUse)) {
                    return false;
                }
                return original(a_handler, a_event);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // Skyrim's own reaction to keys MSC uses: moving, looking, jumping (Space), sneaking (Ctrl,
        // MSC's crouch), first or third person (F, MSC's use; the wheel, MSC's radio dial), autorun (C,
        // smoking), running, sprinting (Alt, MSC's piss), attacking and blocking (MSC's mouse buttons)
        // and drawing (R). Each handler's CanProcess refuses while MSC has the player. Shouting (Z) stays Skyrim's.
        // One instance per handler (N).
        template <class T, int N>
        struct BlockHook
        {
            static bool thunk(T* a_handler, RE::InputEvent* a_event)
            {
                return !state.owned && original(a_handler, a_event);
            }
            static inline REL::Relocation<decltype(thunk)> original;

            static void Install(REL::VariantID a_vtable)
            {
                REL::Relocation<std::uintptr_t> vtable{ a_vtable };
                original = vtable.write_vfunc(0x1, thunk);
            }
        };

        // Q (MSC's lean left) would open Skyrim's favorites, which pauses the game and with it MSC's input.
        struct FavoritesHook
        {
            static bool thunk(RE::FavoritesHandler* a_handler, RE::InputEvent* a_event)
            {
                return !state.owned && original(a_handler, a_event);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // MSC's own keys open Skyrim's menus too (P magic, J journal, I inventory, T wait), and a
        // menu pauses Skyrim and with it MSC. While MSC has the player only Escape (Skyrim's system
        // menu), Tab and M (the map; it never reaches MSC, whose finger moves to V) still open menus
        // from the keyboard.
        struct MenuOpenHook
        {
            static bool thunk(RE::MenuOpenHandler* a_handler, RE::InputEvent* a_event)
            {
                if (state.owned && a_event && a_event->GetDevice() == RE::INPUT_DEVICE::kKeyboard) {
                    if (auto* button = a_event->AsButtonEvent()) {
                        const auto key = button->GetIDCode();
                        if (key != Input::kDikEscape && key != Input::kDikTab && key != Input::kDikM) {
                            return false;
                        }
                    }
                }
                return original(a_handler, a_event);
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
                        if (MenuHasInput()) {
                            continue;  // moves the menu's cursor, not MSC's camera
                        }
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
                        if (const int notches = Input::WheelNotches(code)) {
                            if (button->IsDown() && !MenuHasInput()) {
                                state.input.AddWheel(notches);
                            }
                        } else {
                            state.input.SetKey(Input::KeyFromMouseButton(code), button->IsPressed());
                        }
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
                } else if (state.resume) {
                    state.resume = false;
                    SKSE::log::info("player sync: F10 before the loaded save's handover resumed, not handing over");
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
                    Publish(pauses || MenuHasInput(), pauses || GamePaused());
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void Install()
    {
        REL::Relocation<std::uintptr_t> vtable{ RE::VTABLE_PlayerCharacter[0] };
        UpdateHook::original = vtable.write_vfunc(0xAD, UpdateHook::thunk);
        REL::Relocation<std::uintptr_t> favorites{ RE::VTABLE_FavoritesHandler[0] };
        FavoritesHook::original = favorites.write_vfunc(0x1, FavoritesHook::thunk);
        REL::Relocation<std::uintptr_t> menuOpen{ RE::VTABLE_MenuOpenHandler[0] };
        MenuOpenHook::original = menuOpen.write_vfunc(0x1, MenuOpenHook::thunk);
        REL::Relocation<std::uintptr_t> activate{ RE::VTABLE_ActivateHandler[0] };
        ActivateHook::original = activate.write_vfunc(0x1, ActivateHook::thunk);
        BlockHook<RE::PlayerInputHandler, 0>::Install(RE::VTABLE_MovementHandler[0]);
        BlockHook<RE::PlayerInputHandler, 1>::Install(RE::VTABLE_LookHandler[0]);
        BlockHook<RE::PlayerInputHandler, 2>::Install(RE::VTABLE_SprintHandler[0]);
        BlockHook<RE::PlayerInputHandler, 3>::Install(RE::VTABLE_AutoMoveHandler[0]);
        BlockHook<RE::PlayerInputHandler, 4>::Install(RE::VTABLE_ToggleRunHandler[0]);
        BlockHook<RE::PlayerInputHandler, 5>::Install(RE::VTABLE_RunHandler[0]);
        BlockHook<RE::PlayerInputHandler, 6>::Install(RE::VTABLE_JumpHandler[0]);
        BlockHook<RE::PlayerInputHandler, 7>::Install(RE::VTABLE_SneakHandler[0]);
        BlockHook<RE::PlayerInputHandler, 8>::Install(RE::VTABLE_TogglePOVHandler[0]);
        BlockHook<RE::PlayerInputHandler, 9>::Install(RE::VTABLE_AttackBlockHandler[0]);
        BlockHook<RE::PlayerInputHandler, 10>::Install(RE::VTABLE_ReadyWeaponHandler[0]);
        BlockHook<RE::PlayerInputHandler, 11>::Install(RE::VTABLE_FirstPersonState[1]);  // its zoom and POV keys
        BlockHook<RE::PlayerInputHandler, 12>::Install(RE::VTABLE_ThirdPersonState[1]);
        SKSE::log::info("player sync: PlayerCharacter::Update and the CanProcess of the activate, favorites, menu, movement, look, jump, sneak, POV, attack and camera input handlers hooked (shouting stays Skyrim's, Z), F10 hands the player to MSC");
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

    void OnGameLoad()
    {
        Release("game loading");
        state.garage = nullptr;
        state.resume = false;
    }

    bool Owned()
    {
        return state.owned;
    }

    void RestoreControls()
    {
        if (!state.owned) {
            SetControls(true);
            SKSE::log::info("player sync: the save was taken while MSC had the player, Skyrim's controls are back");
        }
    }

    void ResumeHandover()
    {
        if (state.owned) {
            return;
        }
        state.resume = true;
        state.resumeTime = 0.0f;
        state.resumeFrames = 0;
        SKSE::log::info("player sync: the save was taken while MSC had the player, handing it back once it's in");
    }
}
