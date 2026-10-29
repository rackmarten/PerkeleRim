#include "people/People.h"

#include "coords.h"
#include "damage.h"
#include "bystanders.h"

#include <atomic>
#include <chrono>

namespace PerkeleRim::People
{
    namespace
    {
        constexpr double kWayCheckEvery = 0.2;  // who is in the driven vehicle's way, looked at this often

        struct State
        {
            Bystanders::Fleeing fleeing;
            std::uint32_t vehicle = 0;  // what SetInVehicle last said
            double        wayCheckAt = -1e9;
            unsigned      fled = 0;     // for the log
        } state;

        // Set from the main thread, read wherever Skyrim starts a dialogue (its AI threads too).
        std::atomic<bool> mayTalk{ true };

        double NowSeconds()
        {
            return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
        }

        const char* NameOf(RE::Actor* a_actor)
        {
            const char* name = a_actor ? a_actor->GetDisplayFullName() : nullptr;
            return name && *name ? name : "?";
        }

        const char* VehicleName(std::uint32_t a_vehicle)
        {
            switch (a_vehicle) {
            case Protocol::kVehicleHayosiko: return "the van";
            case Protocol::kVehicleJonnez: return "the moped";
            case Protocol::kVehicleKekmet: return "the Kekmet";
            case Protocol::kVehicleGifu: return "the Gifu";
            default: return "the vehicle";
            }
        }

        Bystanders::Person PersonOf(RE::Actor* a_actor, RE::PlayerCharacter* a_player)
        {
            Bystanders::Person person;
            auto*  race = a_actor->GetRace();
            person.npc = race && race->HasKeywordString("ActorTypeNPC"sv);
            person.dead = a_actor->IsDead();
            person.follower = a_actor->IsPlayerTeammate() || a_actor->IsCommandedActor();
            person.guard = a_actor->IsGuard();
            person.hostile = a_actor->IsHostileToActor(a_player);
            person.fighting = a_actor->IsInCombat();
            const auto  min = a_actor->GetBoundMin();
            const auto  max = a_actor->GetBoundMax();
            const float scale = a_actor->GetScale();
            const bool  dragon = race && race->HasKeywordString("ActorTypeDragon"sv);
            person.huge = Damage::BulkOf(Damage::Mass((max.x - min.x) * scale, (max.y - min.y) * scale, (max.z - min.z) * scale), dragon) ==
                          Damage::Bulk::kHuge;
            return person;
        }

        RE::TESPackage* RunningPackage(RE::Actor* a_actor)
        {
            auto* process = a_actor->GetActorRuntimeData().currentProcess;
            return process ? process->GetRunningPackage() : nullptr;
        }

        const char* PackageOf(RE::Actor* a_actor)
        {
            auto* package = RunningPackage(a_actor);
            return package ? package->GetObjectTypeName() : "no package";
        }

        void LetGo(std::uint32_t a_id)
        {
            auto* actor = RE::TESForm::LookupByID<RE::Actor>(a_id);
            if (!actor || actor->IsDead()) {
                return;
            }
            // The flee package outlives a new evaluation for someone who never got far enough away
            // (cornered in town), so then its AI is reset, as the console's ResetAI does.
            actor->EvaluatePackage(true, false);
            auto*      package = RunningPackage(actor);
            const bool reset = package && package->packData.packType == RE::PACKAGE_TYPE::kFlee;
            if (reset) {
                actor->EvaluatePackage(true, true);
            }
            SKSE::log::info("people: {} ({:08X}) stops fleeing{}: {}", NameOf(actor), a_id, reset ? ", its AI reset" : "", PackageOf(actor));
        }

        // Every dialogue with the player starts with Actor::SetDialogueWithPlayer(true, ...): talking to
        // someone, someone walking up to the player, a force-greet. Refused while Kumpo may not talk;
        // a package that keeps trying is logged once per speaker per 5 s.
        struct DialogueHook
        {
            static bool thunk(RE::Actor* a_actor, bool a_start, bool a_forceGreet, RE::TESTopicInfo* a_topic)
            {
                if (a_start && !mayTalk) {
                    static std::atomic<RE::FormID>   lastId{ 0 };
                    static std::atomic<std::int64_t> lastAt{ 0 };
                    const auto now = std::chrono::duration_cast<std::chrono::milliseconds>(std::chrono::steady_clock::now().time_since_epoch()).count();
                    const auto id = a_actor ? a_actor->GetFormID() : 0;
                    if (id != lastId.exchange(id) || now - lastAt > 5000) {
                        lastAt = now;
                        SKSE::log::info("people: {} ({:08X}) can't {} Kumpo: he's in a vehicle", NameOf(a_actor), id,
                            a_forceGreet ? "force-greet" : "talk to");
                    }
                    return false;
                }
                return original(a_actor, a_start, a_forceGreet, a_topic);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };
    }

    void Install()
    {
        REL::Relocation<std::uintptr_t> character{ RE::VTABLE_Character[0] };
        DialogueHook::original = character.write_vfunc(0x41, DialogueHook::thunk);
    }

    void SetInVehicle(std::uint32_t a_vehicle)
    {
        if (a_vehicle != state.vehicle) {
            SKSE::log::info("people: {}", a_vehicle ? fmt::format("Kumpo is in {}, nobody may talk to him", VehicleName(a_vehicle)) :
                                                      std::string("Kumpo is on foot, people may talk to him"));
            state.vehicle = a_vehicle;
        }
        mayTalk = Bystanders::MayTalk(a_vehicle != 0);
    }

    bool Flee(RE::PlayerCharacter* a_player, RE::Actor* a_actor, const char* a_why)
    {
        if (!Bystanders::Flees(PersonOf(a_actor, a_player))) {
            return false;
        }
        const bool fresh = !state.fleeing.Has(a_actor->GetFormID());
        if (!state.fleeing.Start(a_actor->GetFormID(), NowSeconds())) {
            return false;
        }
        // Away from the player (in its vehicle), outside combat: a flee package (no crime, no aggro).
        a_actor->InitiateFlee(a_player, false, true, false, nullptr, nullptr, Bystanders::kFleeUnits, Bystanders::kFleeUnits);
        state.fled++;
        SKSE::log::info("people: {} ({:08X}) {} {} ({:.0f} units away, now running {})", NameOf(a_actor), a_actor->GetFormID(),
            fresh ? "flees from" : "keeps fleeing from", a_why, a_actor->GetPosition().GetDistance(a_player->GetPosition()), PackageOf(a_actor));
        return true;
    }

    void Update(RE::PlayerCharacter* a_player, const Protocol::VehicleState* a_driven)
    {
        const double now = NowSeconds();
        for (const auto id : state.fleeing.Expired(now)) {
            LetGo(id);
        }
        if (!a_driven || now - state.wayCheckAt < kWayCheckEvery) {
            return;
        }
        state.wayCheckAt = now;
        const float wayX = a_driven->velocity[0], wayY = a_driven->velocity[1];
        const float speed = std::hypot(wayX, wayY) * static_cast<float>(Coords::kMetersPerUnit);
        if (Damage::WayReach(speed) <= 0.0f) {
            return;
        }
        auto* lists = RE::ProcessLists::GetSingleton();
        if (!lists) {
            return;
        }
        const auto why = fmt::format("{} at {:.0f} km/h", VehicleName(a_driven->vehicle), speed * 3.6f);
        for (auto& handle : lists->highActorHandles) {
            auto  ptr = handle.get();
            auto* actor = ptr.get();
            if (!actor || actor == a_player || actor->IsDead() || actor->IsDisabled() || !actor->Is3DLoaded() || actor->IsInRagdollState()) {
                continue;
            }
            const auto pos = actor->GetPosition();
            if (Damage::InVehicleWay(pos.x - a_driven->position[0], pos.y - a_driven->position[1], wayX, wayY, speed)) {
                Flee(a_player, actor, why.c_str());
            }
        }
    }

    void Stop()
    {
        SetInVehicle(0);
        const auto all = state.fleeing.All();
        for (const auto id : all) {
            LetGo(id);
        }
        if (state.fled) {
            SKSE::log::info("people: {} flees from Kumpo's vehicles this handover, {} let go now", state.fled, all.size());
        }
        state.fled = 0;
    }
}
