#include "debug/HitTest.h"

#include <deque>
#include <filesystem>
#include <fstream>

#include "Ini.h"
#include "combat/Combat.h"
#include "coords.h"
#include "hit_test.h"
#include "link/LinkService.h"
#include "look_at.h"
#include "player/PlayerSync.h"

namespace PerkeleRim::HitTest
{
    namespace
    {
        // Skyrim.esm's Whiterun guard (GuardWhiterunImperialTemplate): a person, who takes a hit as cast.
        // Giants (LvlGiant, 0003053F) take only two thirds of it, which the pass rules would count as a fail.
        constexpr RE::FormID    kGuard = 0x000EA0A2;
        constexpr std::uint16_t kKeyH = 104;          // Unity KeyCode.H, MSC's Hit
        constexpr std::uint16_t kMouse1 = 324;        // Unity KeyCode.Mouse1, MSC's throw
        constexpr float         kPlayerRadius = 0.3f;  // meters, MSC's player capsule (Actors.Touching)
        constexpr float         kSettleSeconds = 5.0f;     // after the load, before F10
        constexpr float         kHandoverTimeout = 120.0f;  // the first handover of a save walks to the garage
        constexpr float         kOwnedSettle = 4.0f;       // MSC's island gets built under the player
        constexpr float         kLevelTimeout = 8.0f;
        constexpr float         kLevelPitch = 0.05f;  // radians, about 3 degrees: level enough
        constexpr float         kHitTimeout = 4.0f;   // from the key's press until the hit reaches Skyrim
        constexpr float         kSpellSeconds = 0.5f;  // from the hit until its spell has landed on the health
        constexpr float         kPunchHold = 0.3f;   // past MSC's 0.15 s from the key to the fist's sweep
        constexpr float         kThrowHold = 1.0f;     // a full charge (ThrowCharge)
        constexpr float         kHoldSeconds = 2.0f;   // for MSC's "hold" to put the bottle in the hand

        enum class Phase
        {
            Off,
            Loading,   // waiting for Autoload's save
            Settling,  // it's in; F10 after kSettleSeconds
            Handing,   // MSC takes the player
            Drunk,     // the scenario's drunk level goes to MSC
            Level,     // MSC's camera is turned level
            Placing,   // the target comes, then stands ahead
            Aiming,    // its capsule reaches MSC
            Holding,   // a throw: the bottle comes into the hand
            Pressing,  // the key is held
            Waiting,   // for the hit, then for its spell
            Done,
        };

        struct State
        {
            Phase                    phase = Phase::Off;
            std::vector<Scenario>    scenarios;
            std::size_t              scenario = 0;
            int                      hit = 0;  // of the scenario's, from 0
            float                    timer = 0.0f;
            float                    step = 0.0f;  // the camera levelling's own clock
            float                    owned = 0.0f;  // seconds MSC has had the player on the ground
            RE::FormID               targetBase = kGuard;
            RE::ObjectRefHandle      target;
            std::string              targetName;
            std::uint32_t            hitsBefore = 0;  // Combat::LastHit().count before the key
            Hit                      result;
            bool                     landed = false;
            float                    landedAt = 0.0f;
            float                    pitchSign = 1.0f;  // which way a mouse count turns MSC's pitch, learned
            float                    pitchGain = 4.0f;  // counts per degree, halved on every overshoot
            float                    lastPitch = 0.0f;
            std::deque<std::string>  commands;  // DebugCommand lines not sent yet (a full ring)
            std::filesystem::path    csvPath;
            std::ofstream            csv;
            int                      passed = 0;
            int                      rows = 0;
            float                    soberSum = 0.0f, drunkSum = 0.0f, drunkLevel = 0.0f;
            int                      soberCount = 0, drunkCount = 0;
            bool                     ranSober = false, ranDrunk = false;
        } state;

        void RunConsole(const std::string& a_command, RE::TESObjectREFR* a_on)
        {
            auto* factory = RE::IFormFactory::GetConcreteFormFactoryByType<RE::Script>();
            auto* script = factory ? factory->Create() : nullptr;
            if (!script) {
                return;
            }
            script->SetCommand(a_command);
            script->CompileAndRun(a_on);
            delete script;
        }

        void Command(std::string a_line)
        {
            SKSE::log::info("hit test: MSC, {}", a_line);
            state.commands.push_back(std::move(a_line));
        }

        void SendCommands()
        {
            while (!state.commands.empty()) {
                const auto payload = LookAt::Encode(state.commands.front());
                if (!LinkService::Get().Send(Protocol::MessageType::DebugCommand, payload.data(), static_cast<std::uint32_t>(payload.size()))) {
                    return;  // next frame
                }
                state.commands.pop_front();
            }
        }

        bool Pose(Protocol::PlayerState& a_pose)
        {
            return LinkService::Get().ReadPlayer(a_pose) && (a_pose.flags & Protocol::kPlayerValid) != 0;
        }

        RE::Actor* Target()
        {
            auto ref = state.target.get();
            return ref ? ref->As<RE::Actor>() : nullptr;
        }

        float Health(RE::Actor* a_actor)
        {
            auto* values = a_actor ? a_actor->AsActorValueOwner() : nullptr;
            return values ? values->GetActorValue(RE::ActorValue::kHealth) : 0.0f;
        }

        void RemoveTarget()
        {
            // Only disabled: markfordelete crashed Skyrim (a null in the target's MovementControllerNPC
            // job) while a light one still slid on from a stagger. Nothing is saved, so it can stay.
            if (auto ref = state.target.get()) {
                RunConsole("disable", ref.get());
            }
            state.target = {};
        }

        void WriteRow(const std::string& a_row)
        {
            if (state.csv) {
                state.csv << a_row << '\n';
                state.csv.flush();  // a crash later keeps what was done
            }
        }

        void Finish(const char* a_why)
        {
            PlayerSync::DebugHold(kKeyH, false);
            PlayerSync::DebugHold(kMouse1, false);
            RemoveTarget();
            if (state.ranSober && state.ranDrunk) {
                const auto verdict = CheckDrunk(state.soberCount ? state.soberSum / state.soberCount : 0.0f,
                    state.drunkCount ? state.drunkSum / state.drunkCount : 0.0f, state.drunkCount ? state.drunkLevel / state.drunkCount : 0.0f);
                WriteRow(SummaryRow("drunk vs punch", verdict));
                state.rows++;
                state.passed += verdict.pass ? 1 : 0;
                SKSE::log::info("hit test: drunk vs punch, {}{}", verdict.pass ? "pass" : "FAIL", verdict.note.empty() ? "" : ": " + verdict.note);
            }
            Command("drunk 0");
            SendCommands();
            state.csv.close();
            if (a_why) {
                SKSE::log::info("hit test: stopped, {} ({} of {} passed, results in {})", a_why, state.passed, state.rows, state.csvPath.string());
            } else {
                SKSE::log::info("hit test: done, {} of {} passed, results in {}", state.passed, state.rows, state.csvPath.string());
            }
            state.phase = Phase::Done;
        }

        void Go(Phase a_phase)
        {
            state.phase = a_phase;
            state.timer = 0.0f;
        }

        const Scenario& Current()
        {
            return state.scenarios[state.scenario];
        }

        void StartScenario()
        {
            if (state.scenario >= state.scenarios.size()) {
                Finish(nullptr);
                return;
            }
            state.hit = 0;
            SKSE::log::info("hit test: scenario {}", Current().name);
            char drunk[32];
            std::snprintf(drunk, sizeof(drunk), "%.2f", Current().drunk);
            Command(std::string("drunk ") + drunk);
            Go(Phase::Drunk);
        }

        // Turns MSC's camera level with mouse moves: a throw flies where it looks, and a punch reaches
        // further. MSC's sensitivity isn't known, so the gain halves whenever it overshoots, and a move
        // that tilted the wrong way turns the sign around.
        bool Leveled(const Protocol::PlayerState& a_pose, float a_delta)
        {
            const float pitch = a_pose.viewPitch;
            if (std::abs(pitch) < kLevelPitch) {
                return true;
            }
            state.step += a_delta;
            if (state.step < 0.1f) {
                return false;
            }
            state.step = 0.0f;
            if (state.lastPitch != 0.0f) {
                if (std::abs(pitch) > std::abs(state.lastPitch) + 0.01f && (pitch > 0.0f) == (state.lastPitch > 0.0f)) {
                    state.pitchSign = -state.pitchSign;  // went the wrong way
                } else if ((pitch > 0.0f) != (state.lastPitch > 0.0f)) {
                    state.pitchGain = (std::max)(0.25f, state.pitchGain * 0.5f);  // overshot
                }
            }
            state.lastPitch = pitch;
            const float degrees = pitch * 57.29578f;
            const auto  counts = static_cast<std::int32_t>(std::clamp(degrees * state.pitchGain, -80.0f, 80.0f) * state.pitchSign);
            PlayerSync::DebugMouse(0, counts == 0 ? (pitch > 0.0f ? 1 : -1) : counts);
            return false;
        }

        // The target just ahead of the player along MSC's view, its capsule a_gap meters from the
        // player's (radius as Combat sizes it: half the narrower side of its bounds). Again every frame
        // until the attack: a light target with its AI off slides on after a stagger, and MSC's camera
        // sways while drunk.
        void Stand(RE::PlayerCharacter* a_player, RE::Actor* a_target, const Protocol::PlayerState& a_pose, bool a_log = false)
        {
            const auto  min = a_target->GetBoundMin();
            const auto  max = a_target->GetBoundMax();
            const float scale = a_target->GetScale();
            const float radius = 0.5f * (std::min)(max.x - min.x, max.y - min.y) * scale;
            const float ahead = (kPlayerRadius + Current().gap) / Coords::kMetersPerUnit + radius;
            const float heading = a_pose.viewHeading;
            const auto  at = a_player->GetPosition() + RE::NiPoint3(std::sin(heading) * ahead, std::cos(heading) * ahead, 0.0f);
            a_target->SetPosition(at, true);
            a_target->SetHeading(heading + 3.14159265f);  // facing the player
            if (!a_log) {
                return;
            }
            SKSE::log::info("hit test: {} ({:08X}) stands {:.0f} units ahead (radius {:.0f}), health {:.0f}", state.targetName,
                a_target->GetFormID(), ahead, radius, Health(a_target));
        }

        void Press()
        {
            state.hitsBefore = Combat::LastHit().count;
            state.result = {};
            state.result.before = Health(Target());
            state.landed = false;
            PlayerSync::DebugHold(Current().attack == Attack::kPunch ? kKeyH : kMouse1, true);
            Go(Phase::Pressing);
        }

        void Record()
        {
            auto& hit = state.result;
            hit.after = Health(Target());
            const auto verdict = Check(Current(), hit);
            WriteRow(Row(Current(), state.hit + 1, state.targetName, hit, verdict));
            state.rows++;
            state.passed += verdict.pass ? 1 : 0;
            SKSE::log::info("hit test: {} {}: {}, {:.1f} cast ({:.1f} by damage.h), health {:.1f} -> {:.1f}, {}{}", Current().name, state.hit + 1,
                hit.landed ? "landed" : "missed", hit.damage, Expected(Current(), hit), hit.before, hit.after, verdict.pass ? "pass" : "FAIL",
                verdict.note.empty() ? "" : ": " + verdict.note);
            if (hit.landed && Current().attack == Attack::kPunch) {  // the ratio is of what Combat cast, so a health mismatch doesn't hide it
                if (Current().drunk > 0.0f) {
                    state.drunkSum += hit.damage;
                    state.drunkLevel += hit.drunk;
                    state.drunkCount++;
                } else {
                    state.soberSum += hit.damage;
                    state.soberCount++;
                }
            }
            if (Current().attack == Attack::kPunch) {
                (Current().drunk > 0.0f ? state.ranDrunk : state.ranSober) = true;
            }
            if (++state.hit < Current().hits && Target() && !Target()->IsDead()) {
                Go(Current().attack == Attack::kThrow ? Phase::Holding : Phase::Aiming);
                if (Current().attack == Attack::kThrow) {
                    Command("hold " + std::to_string(Protocol::kPocketBottle));
                }
                return;
            }
            RemoveTarget();
            state.scenario++;
            StartScenario();
        }
    }

    void OnDataLoaded()
    {
        const auto list = Ini::Value("HitTest");
        if (list.empty()) {
            return;
        }
        std::vector<std::string> unknown;
        state.scenarios = Parse(list, &unknown);
        for (const auto& name : unknown) {
            SKSE::log::warn("hit test: no scenario '{}' (protocol/cpp/hit_test.h)", name);
        }
        if (state.scenarios.empty()) {
            SKSE::log::warn("hit test: HitTest={} names no scenario, off", list);
            return;
        }
        if (const auto base = Ini::Value("HitTestTarget"); !base.empty()) {
            try {
                state.targetBase = static_cast<RE::FormID>(std::stoul(base, nullptr, 16));
            } catch (...) {
                SKSE::log::warn("hit test: HitTestTarget={} isn't a hex form id, a guard instead", base);
            }
        }
        const auto csv = Ini::Value("HitTestCsv");
        const auto logs = SKSE::log::log_directory();
        state.csvPath = !csv.empty() ? std::filesystem::absolute(std::filesystem::path(std::u8string(csv.begin(), csv.end()))) :
                        logs              ? logs->parent_path().parent_path() / "PerkeleRim" / "hittest.csv" :
                                            std::filesystem::path("hittest.csv");
        std::error_code error;
        std::filesystem::create_directories(state.csvPath.parent_path(), error);
        state.csv.open(state.csvPath, std::ios::trunc);
        state.csv << kCsvHeader << '\n';
        state.csv.flush();
        state.phase = Phase::Loading;
        SKSE::log::info("hit test: on, {} scenario(s) ({}), target {:08X}, results in {}", state.scenarios.size(), list, state.targetBase,
            state.csvPath.string());
    }

    void OnGameLoaded()
    {
        if (state.phase == Phase::Loading) {
            Go(Phase::Settling);
        } else if (state.phase != Phase::Off && state.phase != Phase::Done) {
            Finish("a save loaded in the middle (a death?)");
        }
    }

    void Update(RE::PlayerCharacter* a_player, float a_delta)
    {
        if (state.phase == Phase::Off || state.phase == Phase::Done || state.phase == Phase::Loading || a_delta <= 0.0f) {
            return;
        }
        auto* ui = RE::UI::GetSingleton();
        if (ui && (ui->IsMenuOpen(RE::LoadingMenu::MENU_NAME) || ui->GameIsPaused())) {
            return;
        }
        SendCommands();
        state.timer += a_delta;
        if (state.phase != Phase::Settling && state.phase != Phase::Handing && !PlayerSync::Owned()) {
            Finish("Skyrim has the player back");
            return;
        }
        if (a_player->IsDead()) {
            Finish("the player died");
            return;
        }
        Protocol::PlayerState pose{};
        const bool            posed = Pose(pose);

        switch (state.phase) {
        case Phase::Settling:
            if (a_player->Is3DLoaded() && a_player->GetParentCell() && a_player->GetParentCell()->IsAttached() && state.timer >= kSettleSeconds) {
                SKSE::log::info("hit test: the save is in, handing the player to MSC");
                PlayerSync::DebugHandOver();
                Go(Phase::Handing);
            }
            break;
        case Phase::Handing:
            if (PlayerSync::Seated()) {
                Finish("Kumpo sits in a vehicle, the hit test needs him on foot");
            } else if (PlayerSync::Owned() && posed && (pose.flags & Protocol::kPlayerGrounded) != 0) {
                state.owned += a_delta;
                if (state.owned >= kOwnedSettle) {
                    StartScenario();
                }
            } else if (state.timer > kHandoverTimeout) {
                Finish("MSC never got the player");
            } else {
                state.owned = 0.0f;
            }
            break;
        case Phase::Drunk: {
            // Combat punches with the level MSC reports, which follows the global a frame or two later.
            const float drunk = posed && (pose.flags & Protocol::kPlayerNeeds) ? pose.needs[Protocol::kNeedDrunk] : 0.0f;
            if ((std::abs(drunk - Current().drunk) < 0.25f && state.timer >= 1.0f) || state.timer >= 5.0f) {
                SKSE::log::info("hit test: MSC's drunk level is {:.2f}", drunk);
                state.step = state.lastPitch = 0.0f;
                Go(Phase::Level);
            }
            break;
        }
        case Phase::Level:
            if (!posed) {
                break;
            }
            if (Leveled(pose, a_delta) || state.timer >= kLevelTimeout) {
                SKSE::log::info("hit test: MSC's camera pitch {:.2f}{}", pose.viewPitch, state.timer >= kLevelTimeout ? " (gave up levelling it)" : "");
                auto* base = RE::TESForm::LookupByID<RE::TESBoundObject>(state.targetBase);
                auto  ref = base ? a_player->PlaceObjectAtMe(base, false) : nullptr;
                if (!ref || !ref->As<RE::Actor>()) {
                    Finish("couldn't place the target");
                    return;
                }
                state.target = ref->GetHandle();
                state.targetName = ref->GetName();
                Go(Phase::Placing);
            }
            break;
        case Phase::Placing: {
            auto* target = Target();
            if (!target) {
                Finish("the target is gone");
                return;
            }
            if (!target->Is3DLoaded() && state.timer < 10.0f) {
                break;
            }
            if (!posed) {
                break;
            }
            RunConsole("tai", target);  // its AI off: it stands, doesn't fight back and nobody hears of it
            Stand(a_player, target, pose, true);
            Go(Current().attack == Attack::kThrow ? Phase::Holding : Phase::Aiming);
            if (Current().attack == Attack::kThrow) {
                Command("hold " + std::to_string(Protocol::kPocketBottle));
            }
            break;
        }
        case Phase::Holding:
        case Phase::Aiming:
            if (auto* target = Target(); target && posed) {
                Stand(a_player, target, pose);
            }
            // A punch waits for MSC to have the capsule and a stagger to be over, a throw for the bottle.
            if (state.timer >= (state.phase == Phase::Holding ? kHoldSeconds : 1.5f)) {
                Press();
            }
            break;
        case Phase::Pressing:
            if (state.timer >= (Current().attack == Attack::kPunch ? kPunchHold : kThrowHold)) {
                PlayerSync::DebugHold(Current().attack == Attack::kPunch ? kKeyH : kMouse1, false);
                Go(Phase::Waiting);
            }
            break;
        case Phase::Waiting: {
            const auto& last = Combat::LastHit();
            auto*       target = Target();
            if (!state.landed && target && last.count != state.hitsBefore && last.actor == target->GetFormID()) {
                state.landed = true;
                state.landedAt = state.timer;
                auto& hit = state.result;
                hit.landed = true;
                hit.damage = last.damage;
                hit.unarmed = last.unarmed;
                hit.drunk = last.drunk;
                hit.speed = last.speed;
                hit.sneak = last.sneak;
                hit.stress = last.stress;
            }
            if ((state.landed && state.timer - state.landedAt >= kSpellSeconds) || (!state.landed && state.timer >= kHitTimeout)) {
                Record();
            }
            break;
        }
        default:
            break;
        }
    }
}
