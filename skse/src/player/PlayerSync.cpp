#include "player/PlayerSync.h"

#include "camera.h"
#include "collision/CollisionSampler.h"
#include "combat/Combat.h"
#include "coords.h"
#include "death/Death.h"
#include "debug/HitTest.h"
#include "drink/Drink.h"
#include "garage/Garage.h"
#include "hud/Hud.h"
#include "input.h"
#include "intro/Intro.h"
#include "hands/HandView.h"
#include "item/ItemView.h"
#include "link/LinkService.h"
#include "look_at.h"
#include "model/ModelView.h"
#include "particle/ParticleView.h"
#include "piss_tap.h"
#include "play_mode.h"
#include "pocket/Pocket.h"
#include "rest/Rest.h"
#include "safe/Safe.h"
#include "shot/Shot.h"
#include "stamina.h"
#include "wading.h"
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

        // Main thread only: the input and menu sinks and the Update hook all run there.
        struct State
        {
            bool           owned = false;
            RE::TESObjectREFR* garage = nullptr;  // spawn marker while the player travels there for the first handover
            float              garageTime = 0.0f;
            int                garageFrames = 0;
            PlayMode::Machine  play;  // hands over by itself after a load (protocol/cpp/play_mode.h)
            bool               mscInGame = false;  // MSC's player exists (PlayerState kPlayerInGame)
            const char*        notReady = nullptr;  // why play mode last waited, for the log
            std::uint32_t  generation = 0;
            std::uint32_t  anchorWorldspace = 0;
            std::uint32_t  anchorCell = 0;
            RE::NiPoint3   lastPlaced;  // where the player was last put (anchor or MSC pose)
            bool           sneak = false;  // what we last told Skyrim; its own flag can drop on its own
            int            sneakResend = 0;  // frames until a sneak event the graph ignored is sent again
            std::uint32_t  inputFrame = 0;
            Input::Builder input;
            std::uint64_t  debugKeys[8]{};  // held by the hit test (DebugHold), on top of the devices'
            Stamina::Sprint sprint;  // MSC's run costs stamina; an empty bar stops it
            PissTap::Tap    pissTap;  // Alt reaches MSC only after a clean tap
            Wading::Edge    wading;   // waist-deep water washes Kumpo once per wade in
            std::array<std::uint16_t, 8> hotbar{ 49, 50, 51, 52, 53, 54, 55, 56 };  // MSC's key for each number held (Pocket::Hotkey)
            bool           seated = false;
            float          waterLevel = 0.0f;  // fraction of Kumpo under water, for the log
            RE::NiPoint3   eye;              // Skyrim's camera relative to the placed feet (MSC's eye), 0 when not posed
            float          savedFov = 0.0f;   // Skyrim's worldFOV before MSC's took over, 0 = untouched
            bool           wasThirdPerson = false;  // Skyrim's camera was third person at the handover
            std::string    lookAt;            // MSC's interaction text
            bool           lookAtUse = false; // MSC shows its hand icon
            std::string    subtitle;          // MSC's subtitle line
            std::string    gear;              // MSC's gear indicator while driving
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

            bool chase = false;  // MSC's chase camera was on last frame
        } state;

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
            for (int i = 0; i < 8 && !a_menu; i++) {
                input.keys[i] |= state.debugKeys[i];
            }
            const auto screen = RE::BSGraphics::Renderer::GetScreenSize();
            input.screenWidth = screen.width;  // MSC renders its hand layer this size
            input.screenHeight = screen.height;
            link.WriteInput(input);
        }

        constexpr std::uint32_t kDik1 = 0x02;  // number keys 1 to 8: 0x02..0x09, Skyrim's hotbar

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
                // A number key held holds the MSC key its hotbar slot resolved to, not its own.
                const auto viaHotbar = [&](std::uint16_t a_key) {
                    for (std::uint32_t slot = 0; slot < state.hotbar.size(); slot++) {
                        if ((down[kDik1 + slot] & 0x80) != 0 && state.hotbar[slot] == a_key) {
                            return true;
                        }
                    }
                    return false;
                };
                for (std::uint32_t dik = 0; dik < 0x100; dik++) {
                    if (dik == Input::kDikLeftAlt) {
                        continue;  // held by the piss tap's pulse, not by the keyboard
                    }
                    const bool number = dik >= kDik1 && dik < kDik1 + state.hotbar.size();
                    const auto key = number ? state.hotbar[dik - kDik1] : Input::KeyFromScanCode(dik);
                    if ((down[dik] & 0x80) == 0 && state.input.IsKeyDown(key) && !viaHotbar(key)) {
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

        // Skyrim's window is the one in front: an Alt let go after Alt+Tab switched away isn't a tap.
        bool SkyrimInFront()
        {
            DWORD process = 0;
            ::GetWindowThreadProcessId(::GetForegroundWindow(), &process);
            return process == ::GetCurrentProcessId();
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
            std::fill(std::begin(state.debugKeys), std::end(state.debugKeys), 0);
            state.input.Block(Input::KeyFromScanCode(Input::kDikLeftShift), false);
            state.sprint.Reset();
            state.pissTap.Reset();
            state.wading.Reset();
            SetControls(true);
            if (auto* player = RE::PlayerCharacter::GetSingleton(); player && (state.sneak || player->IsSneaking())) {
                player->AsActorState()->actorState1.sneaking = false;
                player->NotifyAnimationGraph("SneakStop"sv);
            }
            state.sneak = false;
            if (auto* player = RE::PlayerCharacter::GetSingleton(); player && state.seated) {
                SetSimulated(player, true);
            }
            Safe::Set(RE::PlayerCharacter::GetSingleton(), 0);
            state.seated = false;
            state.chase = false;
            state.eye = {};
            if (state.savedFov > 0.0f) {
                if (auto* camera = RE::PlayerCamera::GetSingleton()) {
                    camera->GetRuntimeData2().worldFOV = state.savedFov;
                }
                state.savedFov = 0.0f;
            }
            if (state.wasThirdPerson) {
                if (auto* camera = RE::PlayerCamera::GetSingleton()) {
                    camera->ForceThirdPerson();
                }
                state.wasThirdPerson = false;
            }
            state.lookAt.clear();
            state.lookAtUse = false;
            state.subtitle.clear();
            state.gear.clear();
            state.hasNeeds = false;
            Hud::Set({});
            Drink::SetDrunk(0.0f, 0.0f);
            CollisionSampler::Stop();
            Combat::Stop();
            VehicleView::Hide();
            ItemView::Clear();
            ParticleView::Hide();
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

        // The garage's shelves get MSC's store items once per save, at the first handover near it
        // (Garage::StockDue), on the anchor just sent.
        void Stock(RE::PlayerCharacter* a_player)
        {
            if (!Garage::StockDue(a_player->GetPosition())) {
                return;
            }
            const auto&             spots = Garage::ShelfSpots();
            Protocol::StockShelves  stock{};
            stock.generation = state.generation;
            stock.count = static_cast<std::uint32_t>(std::min<std::size_t>(spots.size(), Protocol::kStockSpotsMax));
            for (std::uint32_t i = 0; i < stock.count; i++) {
                stock.spots[i].position[0] = spots[i].position.x;
                stock.spots[i].position[1] = spots[i].position.y;
                stock.spots[i].position[2] = spots[i].position.z;
                stock.spots[i].heading = spots[i].heading;
            }
            if (!LinkService::Get().Send(Protocol::MessageType::StockShelves, &stock, sizeof(stock))) {
                SKSE::log::warn("player sync: could not send the shelf spots to MSC");
                return;
            }
            Garage::Stocked();
            SKSE::log::info("player sync: {} shelf spots to MSC for gen {}", stock.count, stock.generation);
        }

        void SayNoMsc()
        {
            RE::SendHUDMessage::ShowHUDMessage("My Summer Car isn't running: Kumpo stays in Skyrim until it is.");
        }

        void TakeOver(RE::PlayerCharacter* a_player, const Garage::Spot* a_van = nullptr)
        {
            if (!LinkService::Get().IsUp()) {
                SKSE::log::info("player sync: link is down, can't hand the player to MSC");
                SayNoMsc();
                state.play.Lost();  // hands over once MSC is there
                return;
            }
            if (!SendAnchor(a_player, a_van ? "garage" : "handover", a_van)) {
                SKSE::log::warn("player sync: the handover's anchor didn't go out, trying again once the game has settled");
                state.play.Load();
                return;
            }

            // MSC's view is first person (PoseCamera only works there); in third person both Finn's
            // hands and the whole Skyrim character showed. Back to third person at the release.
            if (auto* camera = RE::PlayerCamera::GetSingleton(); camera && !camera->IsInFirstPerson()) {
                state.wasThirdPerson = camera->IsInThirdPerson();
                camera->ForceFirstPerson();
            }
            state.owned = true;
            Death::Hide();
            Death::OnHandover();
            state.statTime = 0.0f;
            state.frames = state.placed = state.newPoses = 0;
            Publish();
            SKSE::log::info("player sync: MSC owns the player");
            Stock(a_player);
        }

        // A save's first handover starts at the garage: move the player to the shack's wake-up
        // marker (or the garage's spawn marker), and hand over once it has arrived (TravelToGarage).
        void HandOver(RE::PlayerCharacter* a_player)
        {
            auto* marker = Garage::FirstVisit();
            if (!marker) {
                TakeOver(a_player);
                return;
            }
            if (!LinkService::Get().IsUp()) {
                SKSE::log::info("player sync: link is down, can't hand the player to MSC");
                SayNoMsc();
                state.play.Lost();
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
                if (!LinkService::Get().IsUp()) {
                    state.play.Lost();  // hands over where the player stands once MSC is back
                } else {
                    Garage::Visited();  // the shack didn't work out: the next handover starts where he stands
                    state.play.Load();
                }
                return;
            }
            auto*      cell = a_player->GetParentCell();
            const bool there = cell && cell->IsAttached() && a_player->Is3DLoaded() &&
                               a_player->GetPosition().GetDistance(state.garage->GetPosition()) < kGarageArrivedUnits;
            if (there && state.garageFrames == 0 && Garage::WakeUp(state.garage)) {  // fades up from black while the handover settles
                Intro::WakeUp();  // with Kumpo's hangover line, as in the Helgen cart
            }
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

        const char* PhaseName(PlayMode::Phase a_phase)
        {
            switch (a_phase) {
            case PlayMode::Phase::Settling:
                return "waits for the game to settle and let the player move";
            case PlayMode::Phase::Intro:
                return "waits for Kumpo to get out of Helgen";
            case PlayMode::Phase::Msc:
                return "waits for MSC";
            default:
                return "is off";
            }
        }

        // Play mode (protocol/cpp/play_mode.h): after a load Skyrim hands the player to MSC by
        // itself, once the game has settled, Helgen is behind it and MSC is in its game.
        void Play(RE::PlayerCharacter* a_player, float a_delta)
        {
            const bool intro = Intro::Running();
            if (intro) {
                Garage::CartStart();  // no trip to the shack for a save that started in the cart
            }
            if (!state.play.Waiting()) {
                return;
            }
            auto*           ui = RE::UI::GetSingleton();
            auto*           cell = a_player->GetParentCell();
            auto*           controls = RE::ControlMap::GetSingleton();
            PlayMode::Frame frame;
            frame.linkUp = LinkService::Get().IsUp();
            frame.mscInGame = state.mscInGame;
            frame.loading = ui && ui->IsMenuOpen(RE::LoadingMenu::MENU_NAME);
            frame.paused = GamePaused();
            frame.player3D = a_player->Is3DLoaded();
            frame.cellAttached = cell && cell->IsAttached();
            frame.controls = controls && controls->IsMovementControlsEnabled();
            auto* actorState = a_player->AsActorState();
            frame.combat = a_player->IsInCombat();
            frame.mounted = a_player->IsOnMount();
            frame.swimming = actorState && actorState->IsSwimming();
            frame.knocked = a_player->IsInRagdollState() ||
                            (actorState && actorState->GetKnockState() != RE::KNOCK_STATE_ENUM::kNormal);
            frame.sitting = actorState && actorState->GetSitSleepState() != RE::SIT_SLEEP_STATE::kNormal;
            frame.talking = ui && ui->IsMenuOpen(RE::DialogueMenu::MENU_NAME);
            frame.intro = intro;
            frame.delta = a_delta;
            const auto  before = state.play.Current();
            const auto  say = state.play.Tick(frame);
            const char* reason = state.play.Current() == PlayMode::Phase::Settling ? PlayMode::NotReady(frame) : nullptr;
            if ((state.play.Current() != before || reason != state.notReady) && state.play.Waiting()) {
                SKSE::log::info("player sync: play mode {}{}{} ({})", PhaseName(state.play.Current()), reason ? ": " : "", reason ? reason : "",
                    Intro::Describe());
            }
            state.notReady = reason;
            switch (say) {
            case PlayMode::Say::HandOver:
                SKSE::log::info("player sync: play mode hands the player to MSC");
                HandOver(a_player);
                break;
            case PlayMode::Say::NoMsc:
                SKSE::log::info("player sync: play mode: MSC isn't linked, waiting for it");
                SayNoMsc();
                break;
            case PlayMode::Say::MscLoading:
                SKSE::log::info("player sync: play mode: MSC is linked but not in its game yet, waiting for it");
                RE::SendHUDMessage::ShowHUDMessage("My Summer Car is still loading: Kumpo goes over once it's in.");
                break;
            default:
                break;
            }
        }

        // MSC's player exists (its game is loaded). A torn read keeps the last answer.
        void ReadMscInGame()
        {
            auto&                link = LinkService::Get();
            Protocol::PlayerState pose{};
            if (!link.IsUp()) {
                state.mscInGame = false;
            } else if (link.ReadPlayer(pose)) {
                state.mscInGame = (pose.flags & Protocol::kPlayerInGame) != 0;
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
        // It turns to where MSC's camera looks (viewHeading, viewPitch, roll), not to the player's
        // heading and pitch, which miss the view once the vehicle the player sits in is rolled.
        // MSC's chase camera (kPlayerChaseCamera) comes the same way, far from the feet. Returns
        // whether the camera was posed.
        bool PoseCamera(const Protocol::PlayerState& a_pose, float a_fromHeading, float a_fromPitch)
        {
            auto* camera = RE::PlayerCamera::GetSingleton();
            auto* root = camera ? camera->cameraRoot.get() : nullptr;
            if (!root || !camera->IsInFirstPerson()) {
                state.eye = {};
                return false;
            }

            Camera::Matrix built, posed;
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    built[r][c] = root->world.rotate.entry[r][c];
                }
            }
            Camera::Retarget(built, a_fromHeading, a_fromPitch, a_pose.viewHeading, a_pose.viewPitch, a_pose.roll, posed);
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
            return true;
        }

        // Skyrim builds its camera before this hook, from where the player was put the frame before,
        // and smooths small moves on top, so a driven van drawn at MSC's pose jittered around it and
        // fell behind at speed. Instead the vehicle is hung around the posed camera, at the spot MSC
        // has its camera in the vehicle this same frame (VehicleState.seat): the controls are drawn
        // where MSC picks them, also while leaning, on the moped's wobbly head joint and tilted. A
        // seat learned in Skyrim while stopped (before v30) went stale whenever the camera moved in
        // the vehicle, and the controls twitched and drifted off the crosshair.
        void LockVehicleToCamera(Protocol::VehicleState& a_vehicle)
        {
            auto* camera = RE::PlayerCamera::GetSingleton();
            if (!camera || !camera->cameraRoot || (a_vehicle.flags & Protocol::kVehicleSeat) == 0) {
                return;
            }
            const auto         at = camera->cameraRoot->world.translate;
            const RE::NiPoint3 seat(a_vehicle.seat[0], a_vehicle.seat[1], a_vehicle.seat[2]);
            const RE::NiPoint3 van(a_vehicle.position[0], a_vehicle.position[1], a_vehicle.position[2]);
            float              m[3][3];
            Coords::ToMatrix({ a_vehicle.rotation[0], a_vehicle.rotation[1], a_vehicle.rotation[2], a_vehicle.rotation[3] }, m);
            RE::NiMatrix3 rotate;
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    rotate.entry[r][c] = m[r][c];
                }
            }
            const auto locked = at - rotate * seat;
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

        // MSC's run (LeftShift) costs Skyrim stamina; with the bar empty the key is held back from MSC
        // (from the next Publish on) until a fifth of the stamina is back.
        void Sprint(RE::PlayerCharacter* a_player, const Protocol::PlayerState* a_pose, bool a_seated, bool a_menu, float a_delta)
        {
            auto*       values = a_player->AsActorValueOwner();
            const float stamina = values->GetActorValue(RE::ActorValue::kStamina);
            const float max = values->GetPermanentActorValue(RE::ActorValue::kStamina) +
                              a_player->GetActorValueModifier(RE::ACTOR_VALUE_MODIFIER::kTemporary, RE::ActorValue::kStamina);
            const float speed = a_pose ? std::hypot(a_pose->velocity[0], a_pose->velocity[1]) * Coords::kMetersPerUnit : 0.0f;
            const auto  key = Input::KeyFromScanCode(Input::kDikLeftShift);
            const bool  was = state.sprint.Exhausted();
            const auto  result = state.sprint.Step(a_delta, !a_menu && state.input.IsKeyDown(key), speed, stamina, max, !a_seated);
            state.input.Block(key, result.blockRun && !a_seated);  // driving, Shift is the gear shift
            if (result.drain > 0.0f) {
                values->DamageActorValue(RE::ActorValue::kStamina, result.drain);
            }
            if (result.blockRun != was) {
                SKSE::log::info("player sync: {} (stamina {:.0f} of {:.0f})",
                    result.blockRun ? "out of breath, MSC's run is held back" : "caught its breath, running again", stamina, max);
            }
        }

        // Waist-deep water washes Kumpo: MSC zeroes its dirtiness when he wades in (protocol/cpp/wading.h).
        // The level is the fraction of him under water at his feet, 0 to 1.
        void Wash(RE::PlayerCharacter* a_player, bool a_onFoot)
        {
            float level = 0.0f;
            if (auto* cell = a_onFoot ? a_player->GetParentCell() : nullptr) {
                level = a_player->GetSubmergeLevel(a_player->GetPositionZ(), cell);
            }
            state.waterLevel = level;
            if (!state.wading.Step(level, a_onFoot)) {
                return;
            }
            const std::uint8_t none = 0;
            if (LinkService::Get().Send(Protocol::MessageType::PlayerWashed, &none, 0)) {
                SKSE::log::info("player sync: wading, {:.0f}% under water, MSC washes Kumpo", level * 100.0f);
            } else {
                state.wading.Reset();  // the ring was full, try again next frame
            }
        }

        void Drive(RE::PlayerCharacter* a_player, float a_delta)
        {
            auto& link = LinkService::Get();
            if (!link.IsUp()) {
                Release("link down");
                state.play.Lost();  // hands over again once MSC is back
                RE::SendHUDMessage::ShowHUDMessage("My Summer Car is gone: Kumpo is back in Skyrim until it returns.");
                return;
            }
            if (a_player->IsDead()) {
                Die(a_player);
                return;
            }
            ReleaseStuckKeys();
            state.input.SetKey(Input::KeyFromScanCode(Input::kDikLeftAlt), state.pissTap.Tick(a_delta));
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
            // Every vehicle's slot, by id - 1; seated in whichever one says the player drives it.
            std::array<Protocol::VehicleState, Protocol::kVehicleCount> vehicles{};
            std::array<bool, Protocol::kVehicleCount>                   hasVehicle{};
            Protocol::VehicleState*                                     driven = nullptr;
            for (std::uint32_t i = 0; i < Protocol::kVehicleCount; i++) {
                hasVehicle[i] = link.ReadVehicle(i + 1, vehicles[i]);
                if (hasVehicle[i] && (vehicles[i].flags & Protocol::kVehicleDriven) != 0 && vehicles[i].generation == state.generation) {
                    driven = &vehicles[i];
                }
            }
            const bool seated = valid && driven != nullptr;
            Safe::Set(a_player, seated ? driven->vehicle : 0);
            if (seated != state.seated) {
                state.seated = seated;
                SetSimulated(a_player, !seated);
                SKSE::log::info("player sync: {}", seated ? "seated" : "out of the seat");
            }
            Wash(a_player, valid && !seated);
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
                if (a_type == Protocol::MessageType::ThrownPath) {
                    Combat::OnThrown(a_player, a_data, a_size);
                    return;
                }
                if (a_type == Protocol::MessageType::HornEvent) {
                    Combat::OnHorn(a_player, a_data, a_size);
                    return;
                }
                if (a_type == Protocol::MessageType::MiddleFinger) {
                    Combat::OnMiddleFinger(a_player, a_data, a_size);
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
                if (a_type != Protocol::MessageType::LookAtText && a_type != Protocol::MessageType::Subtitle &&
                    a_type != Protocol::MessageType::GearText) {
                    return;
                }
                const auto text = LookAt::Decode(a_data, a_size);  // a Subtitle has the same payload
                if (!text) {
                    SKSE::log::warn("player sync: bad text message type {} ({} bytes)", static_cast<int>(a_type), a_size);
                } else if (a_type == Protocol::MessageType::Subtitle) {
                    state.subtitle.assign(text->text);
                } else if (a_type == Protocol::MessageType::GearText) {
                    state.gear.assign(text->text);
                } else {
                    state.lookAt.assign(text->text);
                    state.lookAtUse = (text->flags & Protocol::kLookAtUse) != 0;
                }
            });
            bool posed = false;  // Skyrim's first-person camera sits at MSC's this frame
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
                    posed = PoseCamera(pose, fromHeading, fromPitch);
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
            if (seated && !chase && posed) {
                LockVehicleToCamera(*driven);
            }
            Protocol::VehicleParts parts{};
            for (std::uint32_t i = 0; i < Protocol::kVehicleCount; i++) {
                const bool hasParts = hasVehicle[i] && link.ReadVehicleParts(i + 1, parts);
                VehicleView::Update(i + 1, hasVehicle[i] ? &vehicles[i] : nullptr, hasParts ? &parts : nullptr, state.generation);
            }
            Protocol::ItemState items{};
            if (link.ReadItems(items)) {  // a torn read keeps last frame's items; link down releases
                ItemView::Update(&items, state.generation);
            }
            static Protocol::ParticleState particles{};  // 16 KiB, kept off the stack
            if (link.ReadParticles(particles)) {         // a torn read keeps last frame's smoke
                ParticleView::Update(&particles, state.generation, HandView::Light(a_player));
            }
            Protocol::HandLayer hands{};
            HandView::Update(link.ReadHandLayer(hands) ? &hands : nullptr, a_delta);
            Combat::Update(a_player, state.generation, a_delta);
            const bool menu = MenuHasInput();
            Combat::Disarm(a_player);
            Combat::Drunk(a_player, state.hasNeeds ? state.needs[Protocol::kNeedDrunk] : 0.0f, a_delta);
            Combat::Guard(a_player, valid && !seated && !menu && (pose.flags & Protocol::kPlayerBlocking) != 0, !menu);
            Combat::Shouts(a_player);
            Sprint(a_player, valid ? &pose : nullptr, seated, menu, a_delta);
            // Stream ahead of the player while it moves fast (driving).
            const RE::NiPoint3 velocity = valid ? RE::NiPoint3(pose.velocity[0], pose.velocity[1], pose.velocity[2]) : RE::NiPoint3();
            CollisionSampler::Update(a_player, velocity);

            state.frames++;
            state.statTime += a_delta;
            const bool second = state.statTime >= 1.0f;
            Hud::View hud;
            hud.show = !chase;  // clean shots
            hud.hideStealth = !state.sneak;  // Skyrim's sneak eye stayed up after standing
            hud.label = state.lookAt;
            hud.hand = state.lookAtUse;
            hud.subtitle = state.subtitle.empty() ? std::string(Intro::Caption()) : state.subtitle;  // the shack's wake-up caption carries over the handover
            hud.gear = state.gear;
            hud.hasNeeds = state.hasNeeds;
            hud.needs = state.needs;
            hud.handsImage = HandView::Image();
            hud.handsWidth = HandView::Width();
            hud.handsHeight = HandView::Height();
            hud.handsLight = HandView::Light(a_player);
            Hud::Set(hud, second);  // refreshed once a second in case the HUD movie was rebuilt
            Drink::SetDrunk(state.hasNeeds ? state.needs[Protocol::kNeedDrunk] : 0.0f, a_delta);
            if (second) {
                SKSE::log::info(
                    "player sync: frames={} placed={} new poses={} pos=({:.0f},{:.0f},{:.0f}) speed={:.1f} m/s heading={:.2f} pitch={:.2f} stance={} seated={} water={:.2f} van shift={:.1f} eye=({:.0f},{:.0f},{:.0f}) roll={:.2f} fov={:.0f}",
                    state.frames, state.placed, state.newPoses, pose.position[0], pose.position[1], pose.position[2],
                    std::hypot(pose.velocity[0], pose.velocity[1]) * Coords::kMetersPerUnit, pose.heading, pose.pitch, pose.stance,
                    state.seated, state.waterLevel, state.vanShift, pose.eye[0], pose.eye[1], pose.eye[2], pose.roll, pose.fov);
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
                if (!state.owned) {
                    Safe::Set(a_player, 0);  // keeps clearing god mode if it was slow to go off
                }
                if (a_delta > 0.0f) {
                    ReadMscInGame();
                    Intro::Tick(a_delta, LinkService::Get().IsUp() && state.mscInGame, state.owned);
                }
                if (state.garage && a_delta > 0.0f) {
                    TravelToGarage(a_player, a_delta);
                } else if (state.owned && a_delta > 0.0f) {
                    Drive(a_player, a_delta);
                    Rest::OnFrame(a_player, a_delta);
                } else if (!state.owned && a_delta > 0.0f) {
                    Play(a_player, a_delta);
                }
                HitTest::Update(a_player, a_delta);
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
        // menu), Tab, M (the map; it never reaches MSC, whose finger moves to V) and, on foot, T (the
        // Wait menu; the hours go to MSC, Rest) still open menus from the keyboard.
        struct MenuOpenHook
        {
            static bool thunk(RE::MenuOpenHandler* a_handler, RE::InputEvent* a_event)
            {
                if (state.owned && a_event && a_event->GetDevice() == RE::INPUT_DEVICE::kKeyboard) {
                    if (auto* button = a_event->AsButtonEvent()) {
                        const auto key = button->GetIDCode();
                        // T is Skyrim's Wait menu on foot; driving it is MSC's boost.
                        if (key == Input::kDikT && !state.seated) {
                            return original(a_handler, a_event);
                        }
                        if (key != Input::kDikEscape && key != Input::kDikTab && key != Input::kDikM) {
                            return false;
                        }
                    }
                }
                return original(a_handler, a_event);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // F10, a debug toggle since play mode: takes the player back, cancels a handover play mode
        // waits for, or hands the player to MSC. Never in Helgen.
        void Toggle()
        {
            if (state.owned) {
                Release("F10");
            } else if (auto* controls = RE::ControlMap::GetSingleton();
                       PlayMode::F10Refused(Intro::Running(), controls && controls->IsMovementControlsEnabled())) {
                SKSE::log::info("player sync: F10 in the Helgen intro's scene, not handing over ({})", Intro::Describe());
                RE::SendHUDMessage::ShowHUDMessage("Kumpo can't go over to My Summer Car until he's out of Helgen.");
            } else if (state.play.Waiting()) {
                state.play.Cancel();
                SKSE::log::info("player sync: F10 while play mode waited to hand over, the player stays in Skyrim");
                RE::SendHUDMessage::ShowHUDMessage("Kumpo stays in Skyrim (F10 hands him over).");
            } else if (state.garage) {
                state.garage = nullptr;
                SKSE::log::info("player sync: F10 again on the way to the garage, not handing over");
            } else if (auto* player = RE::PlayerCharacter::GetSingleton()) {
                HandOver(player);
            }
        }

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
                        if (code == Input::kDikF11) {
                            if (button->IsDown() && state.owned) {
                                Shot::Take();
                                CollisionSampler::Probe(RE::PlayerCharacter::GetSingleton());  // what the island was made of there
                            }
                            continue;
                        }
                        if (code == Input::kDikLeftAlt) {
                            if (button->IsDown()) {
                                state.pissTap.Press();
                            } else if (button->IsUp() && state.owned) {
                                const float held = button->HeldDuration();
                                const bool  front = SkyrimInFront();
                                SKSE::log::info("player sync: Alt let go after {:.2f} s{}{}", held, front ? "" : ", Skyrim not in front",
                                    state.pissTap.Release(held, front) ? ", a tap: MSC pisses" : ", no tap");
                            }
                            continue;  // reaches MSC as the tap's pulse (Drive)
                        }
                        if (button->IsDown()) {
                            state.pissTap.OtherKey();  // Alt+Tab and other combinations aren't a tap
                        }
                        if (code >= kDik1 && code < kDik1 + state.hotbar.size()) {
                            // Skyrim's hotbar: what the number's hotkey holds, decided when it goes down
                            // (protocol/cpp/hotbar.h); a menu's own use of numbers (binding hotkeys) is left alone.
                            auto& key = state.hotbar[code - kDik1];
                            if (button->IsDown()) {
                                key =state.owned && !MenuHasInput() ? Pocket::Hotkey(RE::PlayerCharacter::GetSingleton(), static_cast<int>(code - kDik1))
                                                                     : Input::KeyFromScanCode(code);
                            }
                            state.input.SetKey(key, button->IsPressed());
                            changed = true;
                            break;
                        }
                        if (code == Input::kDikH && button->IsDown() && state.owned) {
                            Combat::OnSwing(RE::PlayerCharacter::GetSingleton());  // MSC's hit key: webs too
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
                            if (button->IsDown()) {
                                state.pissTap.OtherKey();
                            }
                            if (code == 0 && button->IsDown() && state.owned && !MenuHasInput()) {
                                Combat::OnSwing(RE::PlayerCharacter::GetSingleton());  // left click punches: webs too
                            }
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
        Safe::Install();
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
        SKSE::log::info("player sync: PlayerCharacter::Update and the CanProcess of the activate, favorites, menu, movement, look, jump, sneak, POV, attack and camera input handlers hooked (shouting stays Skyrim's, Z), play mode hands the player to MSC after a load, F10 toggles it");
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
        state.wasThirdPerson = false;  // the loaded save has its own camera
        Release("game loading");
        state.garage = nullptr;
        state.play.Cancel();
    }

    void OnGameLoaded()
    {
        state.play.Load();
        SKSE::log::info("player sync: play mode hands the player to MSC once the game has settled");
    }

    bool Owned()
    {
        return state.owned;
    }

    bool Seated()
    {
        return state.owned && state.seated;
    }

    void DebugHandOver()
    {
        if (!state.owned && !state.play.Waiting() && !state.garage) {  // play mode hands over by itself after a load
            Toggle();
        }
    }

    void DebugHold(std::uint16_t a_keyCode, bool a_held)
    {
        if (a_keyCode >= 512) {
            return;
        }
        const auto bit = 1ull << (a_keyCode & 63);
        if (a_held) {
            state.debugKeys[a_keyCode >> 6] |= bit;
        } else {
            state.debugKeys[a_keyCode >> 6] &= ~bit;
        }
        if (state.owned) {
            Publish();
        }
    }

    void DebugMouse(std::int32_t a_dx, std::int32_t a_dy)
    {
        state.input.AddMouse(a_dx, a_dy);
    }

    void RestoreControls()
    {
        if (!state.owned) {
            SetControls(true);
            SKSE::log::info("player sync: the save was taken while MSC had the player, Skyrim's controls are back");
        }
    }
}
