#include "combat/Combat.h"

#include "coords.h"
#include "damage.h"
#include "death/Death.h"
#include "drink/HeldDrinkView.h"
#include "finger.h"
#include "link/LinkService.h"
#include "needs.h"
#include "player/PlayerSync.h"
#include "shout.h"

#include <cstring>
#include <optional>
#include <unordered_set>

namespace PerkeleRim::Combat
{
    namespace
    {
        constexpr auto          kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID    kPunchSpell = 0x904;   // PerkeleRimPunch: damage, magnitude set per cast
        constexpr RE::FormID    kStaggerSpell = 0x905;  // PerkeleRimPunchStagger
        constexpr RE::FormID    kPissSpell = 0x90E;     // PerkeleRimPiss: poison, magnitude (per second) set per cast
        constexpr RE::FormID    kMuteVoice = 0x90F;     // PerkeleRimMuteVoice: 1 keeps the player quiet on shout and grunt lines
        // PerkeleRimDrunkArmor, PerkeleRimDrunkStamina, PerkeleRimDrunkStaminaRate: fortify, magnitude set per cast, kDrunkSeconds.
        constexpr RE::FormID    kDrunkSpells[] = { 0x914, 0x915, 0x916 };
        constexpr std::size_t   kDrunkSpellCount = std::size(kDrunkSpells);
        constexpr RE::FormID    kJarls = 0x50920;       // Skyrim.esm's JobJarlFaction
        constexpr float         kDrunkSeconds = 10.0f;  // their duration (esp-gen): a save taken meanwhile loses them soon after
        constexpr float         kDrunkRecast = 5.0f;    // recast before they run out
        static_assert(kDrunkRecast < kDrunkSeconds);
        constexpr RE::FormID    kUnarmed = 0x1F4;       // Skyrim.esm's Unarmed weapon, for perk conditions
        constexpr float         kRefuseQuiet = 3.0f;    // seconds between "fists only" notifications
        constexpr float         kListUnits = 2800.0f;   // actors within 40 m go to MSC
        constexpr float         kPunchMaxUnits = 400.0f;  // a punch further than this away is stale
        constexpr float         kSwingReach = 200.0f;        // units from the player a swing reaches a web or other destructible
        constexpr float         kFlatUnits = 16.0f;          // a destructible under this tall lies on the floor (an oil puddle)
        constexpr float         kFlightScanUnits = 8000.0f;  // destructibles within about 110 m can be hit by a thrown item
        constexpr float         kFlightSneakSeconds = 10.0f;  // a throw's launch stance counts for its impact this long
        constexpr float         kItemUnits = 7.0f;           // a thrown item's radius, about 0.1 m
        constexpr float         kObjectRadius = 30.0f;       // a destructible's reach with no 3D loaded to measure
        constexpr float         kWatchUnits = 1000.0f;  // attackers the per-second log follows
        constexpr int           kThrowWaitFrames = 30;  // frames a run-over actor gets to go ragdoll
        constexpr int           kThrowFrames = 3;       // frames its ragdoll is thrown for (the first settle eats one)
        // Capsule size when an actor's bounds look wrong (a person).
        constexpr float kDefaultRadius = 20.0f;
        constexpr float kDefaultHeight = 128.0f;

        // A run-over actor whose ragdoll is to be thrown, in Havok units per second.
        struct Throw
        {
            RE::ActorHandle actor;
            RE::hkVector4   velocity;
            int             wait = kThrowWaitFrames;
            int             frames = kThrowFrames;
        };

        struct State
        {
            std::vector<Throw>            throws;
            RE::SpellItem*                punch = nullptr;
            RE::SpellItem*                stagger = nullptr;
            RE::SpellItem*                piss = nullptr;
            RE::SpellItem*                drunkSpells[kDrunkSpellCount] = {};
            float                         drunkApplied = 0.0f;   // drunk level the spells were cast for, 0 = none on
            int                           drunkLogged = -1;      // tenth of full drunk last logged
            float                         drunkAge = 0.0f;       // seconds since they were cast
            float                         drunk = 0.0f;          // MSC's drunk level this frame, for the shout cooldown
            float                         refusedAt = -100.0f;   // when the last "fists only" showed
            float                         clock = 0.0f;
            bool                          unarmedLogged = false;
            RE::TESGlobal*                muteVoice = nullptr;
            bool                          muted = false;         // muteVoice is 1
            Shout::Watch                  shouts;
            bool                          watching = false;      // shouts has this handover's first value
            int                           charged = -1;          // the variation the shout being charged reached, -1 none
            RE::TESShout*                 charging = nullptr;    // and that shout
            RE::TESFaction*               jarls = nullptr;
            Finger::Bounties              fingered;
            std::uint32_t                 frame = 0;
            bool                          sent = false;  // a non-empty list went out
            float                         statTime = 0.0f;
            std::unordered_set<RE::FormID> logged;  // actors whose capsule size was logged
            bool                          blocking = false;  // what Skyrim was last told
            bool                          drew = false;      // Guard drew the hands, Stop sheathes them
            int                           drawWait = 0;      // frames until drawing is tried again
            bool                          culled = false;    // Guard hid the first-person model
        } state;

        Hit lastHit;

        const char* NameOf(RE::TESObjectREFR* a_ref)
        {
            const char* name = a_ref ? a_ref->GetDisplayFullName() : nullptr;
            return name && *name ? name : "?";
        }

        bool IsDragon(RE::Actor* a_actor)
        {
            auto* race = a_actor->GetRace();
            return race && race->HasKeywordString("ActorTypeDragon"sv);
        }

        // Skyrim's first-person body on or off, all but the held drink. Its root stays shown (an
        // older build culled it).
        void ShowFirstPerson(RE::NiAVObject* a_firstPerson, bool a_show)
        {
            if (a_firstPerson->GetAppCulled()) {
                a_firstPerson->SetAppCulled(false);
            }
            auto* node = a_firstPerson->AsNode();
            if (!node) {
                a_firstPerson->SetAppCulled(!a_show);
                return;
            }
            for (auto& child : node->GetChildren()) {
                if (child && !HeldDrinkView::IsHeldDrink(child.get()) && child->GetAppCulled() == a_show) {
                    child->SetAppCulled(!a_show);
                }
            }
        }

        float Setting(const char* a_name, float a_default)
        {
            auto* settings = RE::GameSettingCollection::GetSingleton();
            auto* setting = settings ? settings->GetSetting(a_name) : nullptr;
            return setting ? setting->GetFloat() : a_default;
        }

        // The player's unarmed damage on a_target as Skyrim works it out for a fist: the race's and
        // Fortify Unarmed (CalcUnarmedDamage), perks on attack damage with the Unarmed weapon (Fists
        // of Steel's gauntlets, mods' perks) and the difficulty's multiplier on damage the player deals.
        float Unarmed(RE::PlayerCharacter* a_player, RE::Actor* a_target)
        {
            const float calc = a_player->CalcUnarmedDamage();
            auto*       values = a_player->AsActorValueOwner();
            const float value = values ? values->GetActorValue(RE::ActorValue::kUnarmedDamage) : 0.0f;
            auto*       race = a_player->GetRace();
            const float raceDamage = race ? race->data.unarmedDamage : 0.0f;
            float       damage = calc > 0.0f ? calc : value > 0.0f ? value : raceDamage > 0.0f ? raceDamage : 4.0f;
            if (auto* fists = RE::TESForm::LookupByID<RE::TESObjectWEAP>(kUnarmed)) {
                RE::BGSEntryPoint::HandleEntryPoint(RE::BGSEntryPoint::ENTRY_POINT::kModAttackDamage, a_player, fists, a_target, &damage);
            }
            static constexpr const char* kDifficulty[] = { "fDiffMultHPByPCVE", "fDiffMultHPByPCE", "fDiffMultHPByPCN", "fDiffMultHPByPCH",
                "fDiffMultHPByPCVH", "fDiffMultHPByPCL" };
            const auto  difficulty = a_player->GetGameStatsData().difficulty;
            const float mult = difficulty >= 0 && difficulty < 6 ? Setting(kDifficulty[difficulty], 1.0f) : 1.0f;
            if (!state.unarmedLogged) {
                state.unarmedLogged = true;
                SKSE::log::info("combat: the player's unarmed damage: CalcUnarmedDamage {:.1f}, UnarmedDamage {:.1f}, race {:.1f}; {:.1f} after perks, "
                                "difficulty {} x{:.2f}",
                    calc, value, raceDamage, damage, difficulty, mult);
            }
            return (std::max)(0.0f, damage) * mult;
        }

        // Its weight guessed from its bounds; a dragon counts as huge whatever they say.
        float MassOf(RE::Actor* a_actor)
        {
            const auto  min = a_actor->GetBoundMin();
            const auto  max = a_actor->GetBoundMax();
            const float scale = a_actor->GetScale();
            const float mass = Damage::Mass((max.x - min.x) * scale, (max.y - min.y) * scale, (max.z - min.z) * scale);
            return IsDragon(a_actor) ? (std::max)(mass, Damage::kHugeMass) : mass;
        }

        // An actor MSC's player can bump into and punch: alive, standing, its 3D in the world.
        bool Standing(RE::Actor* a_actor)
        {
            if (!a_actor || a_actor->IsPlayerRef() || a_actor->IsDead() || a_actor->IsDisabled() || !a_actor->Is3DLoaded() ||
                a_actor->IsInRagdollState() || a_actor->IsOnMount()) {
                return false;
            }
            auto* actorState = a_actor->AsActorState();
            return !actorState || actorState->GetKnockState() == RE::KNOCK_STATE_ENUM::kNormal;
        }

        void Fill(Protocol::ActorPose& a_pose, RE::Actor* a_actor)
        {
            const auto pos = a_actor->GetPosition();
            RE::NiPoint3 velocity;
            a_actor->GetLinearVelocity(velocity);
            const auto  min = a_actor->GetBoundMin();
            const auto  max = a_actor->GetBoundMax();
            const float scale = a_actor->GetScale();
            float       height = (max.z - min.z) * scale;
            float       radius = (std::min)(max.x - min.x, max.y - min.y) * 0.5f * scale;
            if (!(height > 8.0f && height < 2000.0f)) {  // small creatures stay small, MSC's hand reaches down to them
                height = kDefaultHeight * scale;
            }
            if (!(radius > 8.0f && radius < 400.0f)) {
                radius = kDefaultRadius * scale;
            }
            a_pose.id = a_actor->GetFormID();
            a_pose.position[0] = pos.x;
            a_pose.position[1] = pos.y;
            a_pose.position[2] = pos.z;
            a_pose.heading = a_actor->GetAngleZ();
            a_pose.velocity[0] = velocity.x;
            a_pose.velocity[1] = velocity.y;
            a_pose.velocity[2] = velocity.z;
            a_pose.radius = radius;
            a_pose.height = height;
            a_pose.mass = MassOf(a_actor);
            if (state.logged.insert(a_pose.id).second) {
                SKSE::log::info("combat: {} ({:08X}) as a capsule r {:.0f} h {:.0f} (bounds {:.0f}x{:.0f}x{:.0f}, scale {:.2f}), {:.0f} kg",
                    NameOf(a_actor), a_pose.id, radius, height, max.x - min.x, max.y - min.y, max.z - min.z, scale, a_pose.mass);
            }
        }

        // Once a second: what the actors fighting the player near it are doing. Enemies ran up to
        // the handed-over player and never attacked; this shows whether they swing at all.
        void LogAttackers(RE::PlayerCharacter* a_player, const std::vector<std::pair<float, RE::Actor*>>& a_nearby)
        {
            int lines = 0;
            for (const auto& [distance, actor] : a_nearby) {
                if (distance > kWatchUnits || lines >= 4) {
                    break;
                }
                auto target = actor->GetActorRuntimeData().currentCombatTarget.get();
                if (!actor->IsInCombat() || target.get() != a_player) {
                    continue;
                }
                auto* actorState = actor->AsActorState();
                // What its combat group believes about the player: enemies went to where the
                // player was handed over and waited there.
                std::string belief = "no combat group";
                if (auto* group = actor->GetCombatGroup()) {
                    belief = "player not a group target";
                    for (auto& t : group->targets) {
                        if (t.targetHandle.get().get() == a_player) {
                            belief = fmt::format("detect level {}, known {}, lost {}, last known {:.0f} units off", t.detectLevel,
                                t.flags.any(RE::CombatTarget::Flags::kTargetKnown), t.flags.any(RE::CombatTarget::Flags::kTargetLost),
                                t.lastKnownLoc.pos.GetDistance(a_player->GetPosition()));
                        }
                    }
                }
                SKSE::log::info("combat: {} fights the player at {:.0f} units, attack state {}, knock state {}, hostile {}, {}",
                    NameOf(actor), distance, actorState ? static_cast<int>(actorState->GetAttackState()) : -1,
                    actorState ? static_cast<int>(actorState->GetKnockState()) : -1, actor->IsHostileToActor(a_player), belief);
                lines++;
            }
            if (lines > 0) {
                // Where Skyrim's systems see the player, against where it was placed.
                const auto pos = a_player->GetPosition();
                auto*      root = a_player->Get3D(false);
                float      offRoot = root ? root->world.translate.GetDistance(pos) : -1.0f;
                float      offController = -1.0f;
                if (auto* controller = a_player->GetCharController()) {
                    RE::hkVector4 at;
                    controller->GetPosition(at, false);
                    const RE::NiPoint3 world(at.quad.m128_f32[0], at.quad.m128_f32[1], at.quad.m128_f32[2]);
                    offController = (world * RE::bhkWorld::GetWorldScaleInverse()).GetDistance(pos);
                }
                SKSE::log::info("combat: the player's 3D is {:.0f} units off its position, its controller {:.0f}, midair {}, sneaking {}",
                    offRoot, offController, a_player->IsInMidair(), a_player->IsSneaking());
            }
        }

        // Sets every rigid body of the actor's ragdoll moving at a_velocity. Returns how many.
        int ThrowRagdoll(RE::Actor* a_actor, const RE::hkVector4& a_velocity)
        {
            int count = 0;
            if (auto* root = a_actor->Get3D(false)) {
                RE::BSVisit::TraverseScenegraphCollision(root, [&](RE::bhkNiCollisionObject* a_object) {
                    auto* body = a_object->body ? skyrim_cast<RE::bhkRigidBody*>(a_object->body.get()) : nullptr;
                    if (body) {
                        body->SetLinearVelocity(a_velocity);
                        count++;
                    }
                    return RE::BSVisit::BSVisitControl::kContinue;
                });
            }
            return count;
        }

        // Each frame: run-over actors that went ragdoll get thrown, for a few frames.
        void Throws()
        {
            for (auto it = state.throws.begin(); it != state.throws.end();) {
                auto actor = it->actor.get();
                bool done = !actor;
                if (actor && actor->IsInRagdollState()) {
                    const int bodies = ThrowRagdoll(actor.get(), it->velocity);
                    if (it->frames == kThrowFrames) {
                        SKSE::log::info("combat: {} thrown as a ragdoll of {} bodies after {} frames", NameOf(actor.get()), bodies,
                            kThrowWaitFrames - it->wait);
                    }
                    done = --it->frames <= 0;
                } else if (actor && --it->wait <= 0) {
                    SKSE::log::info("combat: {} never went ragdoll, not thrown", NameOf(actor.get()));
                    done = true;
                }
                it = done ? state.throws.erase(it) : it + 1;
            }
        }

        // Skyrim's block. The graph refuses blockStart while MSC has the player (it never sees the hands
        // drawn: iState stays 0), but the hit code only reads the graph's IsBlocking, so that is set on
        // both graphs (third and first person). The event still goes, for the animation if it is taken.
        void SetBlocking(RE::PlayerCharacter* a_player, bool a_block)
        {
            a_player->AsActorState()->actorState2.wantBlocking = a_block ? 1 : 0;
            a_player->NotifyAnimationGraph(a_block ? "blockStart"sv : "blockStop"sv);
            RE::BSTSmartPointer<RE::BSAnimationGraphManager> manager;
            if (a_player->GetAnimationGraphManager(manager) && manager) {
                for (auto& graph : manager->graphs) {
                    if (graph) {
                        graph->SetGraphVariableBool("IsBlocking"sv, a_block);
                    }
                }
            }
        }

        void DispelDrunk(RE::PlayerCharacter* a_player)
        {
            if (auto* target = a_player->AsMagicTarget()) {
                auto caster = a_player->GetHandle();
                for (auto* spell : state.drunkSpells) {
                    if (spell) {
                        target->DispelEffect(spell, caster);
                    }
                }
            }
        }

        void DrunkOff(RE::PlayerCharacter* a_player)
        {
            DispelDrunk(a_player);
            state.drunkApplied = 0.0f;
            state.drunk = 0.0f;
            state.drunkLogged = -1;
        }

        class HitSink : public RE::BSTEventSink<RE::TESHitEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::TESHitEvent* a_event, RE::BSTEventSource<RE::TESHitEvent>*) override
            {
                if (a_event && PlayerSync::Owned() && a_event->target && a_event->target->IsPlayerRef()) {
                    auto*      source = RE::TESForm::LookupByID(a_event->source);
                    const bool blocked = a_event->flags.any(RE::TESHitEvent::Flag::kHitBlocked);
                    SKSE::log::info("combat: {} hit the player with {} ({:08X}){}", NameOf(a_event->cause.get()),
                        source ? source->GetName() : "?", a_event->source, blocked ? ", blocked" : "");
                    if (auto* player = RE::PlayerCharacter::GetSingleton(); player && a_event->cause && !a_event->cause->IsPlayerRef()) {
                        Death::OnHit(player, blocked);
                    }
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };

        // A destructible a swing or a thrown item can hit: its base has destruction data, it isn't an
        // actor and it's there. An oil puddle lies flat on the floor (its bounds are under kFlatUnits
        // tall): a hit sets it alight under the player's feet, so it is left alone. Webs are flat too,
        // but upright, and are always kept.
        struct Destructible
        {
            bool        web;
            const char* model;
        };

        std::optional<Destructible> AsDestructible(RE::TESObjectREFR* a_ref)
        {
            auto* base = a_ref ? a_ref->GetBaseObject() : nullptr;
            auto* model = base ? base->As<RE::TESModel>() : nullptr;
            auto* destructible = base ? base->As<RE::BGSDestructibleObjectForm>() : nullptr;
            if (!model || !model->GetModel() || !destructible || !destructible->data || a_ref->IsDisabled() || a_ref->As<RE::Actor>()) {
                return std::nullopt;
            }
            std::string path = model->GetModel();
            std::ranges::transform(path, path.begin(), [](unsigned char a_c) { return static_cast<char>(std::tolower(a_c)); });
            const bool web = path.find("web") != std::string::npos;
            if (!web && a_ref->GetBoundMax().z - a_ref->GetBoundMin().z < kFlatUnits) {
                return std::nullopt;
            }
            return Destructible{ web, model->GetModel() };
        }

        // DamageObject is Papyrus', and runs the object's own script (Arvel's web frees him, an oil pot
        // drops). A web with no script bound can't take the call, so it is just taken away; anything
        // else without one stays as it is. Returns what happened, for the log.
        const char* DamageDestructible(RE::TESObjectREFR* a_ref, const Destructible& a_what, float a_damage)
        {
            auto* vm = RE::BSScript::Internal::VirtualMachine::GetSingleton();
            auto* policy = vm ? vm->GetObjectHandlePolicy() : nullptr;
            const auto handle = policy ? policy->GetHandleForObject(RE::FormType::Reference, a_ref) : RE::VMHandle{};
            RE::BSTSmartPointer<RE::BSScript::IStackCallbackFunctor> callback;
            const bool scripted = vm && policy && handle != policy->EmptyHandle() &&
                                  vm->DispatchMethodCall(handle, "ObjectReference", "DamageObject", RE::MakeFunctionArguments(static_cast<float>(a_damage)), callback);
            if (!scripted && a_what.web) {
                a_ref->Disable();
            }
            return scripted ? "damaged" : a_what.web ? "taken away (no script to damage)" : "no script to damage it";
        }

        // Thrown items' flights (ThrownPath): the destructibles near the player when the newest flight
        // started, and which of them each flight already hit.
        struct Flights
        {
            std::uint32_t                   scanned = 0;  // the flight candidates were gathered for
            std::vector<RE::FormID>         candidates;
            std::unordered_set<std::uint64_t> hit;        // flight << 32 | form id
            bool                            sneaking = false;  // the newest flight left the hand while the player sneaked
            float                           sneakAt = -100.0f; // state.clock then
        } flights;
    }

    void OnDataLoaded()
    {
        auto* data = RE::TESDataHandler::GetSingleton();
        state.punch = data ? data->LookupForm<RE::SpellItem>(kPunchSpell, kPlugin) : nullptr;
        state.stagger = data ? data->LookupForm<RE::SpellItem>(kStaggerSpell, kPlugin) : nullptr;
        state.piss = data ? data->LookupForm<RE::SpellItem>(kPissSpell, kPlugin) : nullptr;
        state.muteVoice = data ? data->LookupForm<RE::TESGlobal>(kMuteVoice, kPlugin) : nullptr;
        bool drunkSpells = true;
        for (std::size_t i = 0; i < kDrunkSpellCount; i++) {
            state.drunkSpells[i] = data ? data->LookupForm<RE::SpellItem>(kDrunkSpells[i], kPlugin) : nullptr;
            drunkSpells = drunkSpells && state.drunkSpells[i];
        }
        SKSE::log::info("combat: drunk spells {}", drunkSpells ? "found" : "missing (PerkeleRim.esp not loaded or too old), drinking doesn't toughen the player");
        state.jarls = RE::TESForm::LookupByID<RE::TESFaction>(kJarls);
        SKSE::log::info("combat: Jarl faction {}", state.jarls ? "found" : "missing, only guards mind the finger");
        if (state.muteVoice) {
            state.muteVoice->value = 0.0f;  // a save from back then may have kept 1
        }
        SKSE::log::info("combat: voice mute global {}", state.muteVoice ? "found" : "missing (PerkeleRim.esp not loaded or too old), Skyrim's shouts and grunts stay");
        static HitSink hits;
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESHitEvent>(&hits);
        }
        SKSE::log::info("combat: punch spells {}", state.punch && state.stagger ? "found" : "missing (PerkeleRim.esp not loaded or too old), punches and throws do nothing");
        SKSE::log::info("combat: piss spell {}", state.piss ? "found" : "missing (PerkeleRim.esp not loaded or too old), piss does nothing");
    }

    void OnGameLoaded()
    {
        if (state.muteVoice) {
            state.muteVoice->value = 0.0f;
        }
        state.muted = false;
        state.drunkApplied = 0.0f;  // drunk spells in the save run out on their own
        state.drunkLogged = -1;
    }

    void Update(RE::PlayerCharacter* a_player, std::uint32_t a_generation, float a_delta)
    {
        std::vector<std::pair<float, RE::Actor*>> nearby;
        if (auto* lists = RE::ProcessLists::GetSingleton()) {
            const auto at = a_player->GetPosition();
            for (auto& handle : lists->highActorHandles) {
                auto actor = handle.get();
                if (!Standing(actor.get())) {
                    continue;
                }
                const float distance = actor->GetPosition().GetDistance(at);
                if (distance <= kListUnits) {
                    nearby.emplace_back(distance, actor.get());
                }
            }
        }
        std::sort(nearby.begin(), nearby.end(), [](const auto& a_l, const auto& a_r) { return a_l.first < a_r.first; });

        Protocol::ActorList list{};
        list.frame = ++state.frame;
        list.generation = a_generation;
        for (const auto& [distance, actor] : nearby) {
            if (list.count >= Protocol::kActorCapacity) {
                break;
            }
            Fill(list.actors[list.count++], actor);
        }
        LinkService::Get().WriteActors(list);
        state.sent = list.count > 0;

        Throws();

        state.statTime += a_delta;
        if (state.statTime >= 1.0f) {
            state.statTime = 0.0f;
            LogAttackers(a_player, nearby);
        }
    }

    void OnPunch(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size, float a_drunk, float a_stress)
    {
        if (a_size < sizeof(Protocol::PunchEvent)) {
            SKSE::log::warn("combat: bad punch message ({} bytes)", a_size);
            return;
        }
        Protocol::PunchEvent punch;
        std::memcpy(&punch, a_data, sizeof(punch));
        auto* target = RE::TESForm::LookupByID<RE::Actor>(punch.actor);
        if (!target || !Standing(target) || target->GetPosition().GetDistance(a_player->GetPosition()) > kPunchMaxUnits) {
            SKSE::log::info("combat: punch on {:08X} dropped (gone, down or too far)", punch.actor);
            return;
        }
        auto* caster = a_player->GetMagicCaster(RE::MagicSystem::CastingSource::kInstant);
        if (!caster || !state.punch || !state.stagger) {
            return;
        }
        auto*       values = target->AsActorValueOwner();
        const float max = values ? values->GetPermanentActorValue(RE::ActorValue::kHealth) : 0.0f;
        const float unarmed = Unarmed(a_player, target);
        const float damage = Damage::Punch(unarmed, a_drunk) * Needs::StressPunch(a_stress);
        const float health = values ? values->GetActorValue(RE::ActorValue::kHealth) : 0.0f;  // the spell lands next frame
        caster->CastSpellImmediate(state.punch, false, target, 1.0f, false, damage, a_player);
        caster->CastSpellImmediate(state.stagger, false, target, 1.0f, false, 0.0f, a_player);
        lastHit = { lastHit.count + 1, target->GetFormID(), false, damage, unarmed, a_drunk, 0.0f, 1.0f, 0.0f, a_stress };
        SKSE::log::info("combat: the player punched {} for {:.0f} (unarmed {:.1f}, drunk {:.1f}, stress {:.0f}), it had {:.0f} of {:.0f} max health",
            NameOf(target), damage, unarmed, a_drunk, a_stress, health, max);
    }

    const Hit& LastHit()
    {
        return lastHit;
    }

    void OnSwing(RE::PlayerCharacter* a_player)
    {
        auto* tes = RE::TES::GetSingleton();
        if (!a_player || !tes) {
            return;
        }
        const auto         pos = a_player->GetPosition();
        const float        heading = a_player->GetAngleZ();
        const RE::NiPoint3 forward(std::sin(heading), std::cos(heading), 0.0f);
        tes->ForEachReferenceInRange(a_player, kSwingReach, [&](RE::TESObjectREFR* a_ref) {
            const auto what = AsDestructible(a_ref);
            if (!what) {
                return RE::BSContainer::ForEachResult::kContinue;
            }
            // In front (within 60 degrees) and about at the player's height; a web's origin is its middle.
            auto       to = a_ref->GetPosition() - pos;
            const float dz = to.z;
            to.z = 0.0f;
            const float flat = to.Length();
            if (std::abs(dz) > kSwingReach || (flat > 1.0f && forward.Dot(to / flat) < 0.5f)) {
                return RE::BSContainer::ForEachResult::kContinue;
            }
            const char* result = DamageDestructible(a_ref, *what, what->web ? Damage::kWebDamage : Damage::kObjectDamage);
            SKSE::log::info("combat: the player hit {} {:08X} ({}), {}", what->web ? "the web" : "a destructible", a_ref->GetFormID(), what->model, result);
            return RE::BSContainer::ForEachResult::kContinue;
        });
    }

    void OnThrown(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        auto* tes = RE::TES::GetSingleton();
        if (!a_player || !tes || a_size < sizeof(Protocol::ThrownPath)) {
            return;
        }
        Protocol::ThrownPath path;
        std::memcpy(&path, a_data, sizeof(path));
        if (path.flight > flights.scanned || flights.scanned - path.flight > 1000) {  // a new flight (or MSC restarted)
            flights.scanned = path.flight;
            flights.sneaking = a_player->IsSneaking();
            flights.sneakAt = state.clock;
            flights.candidates.clear();
            tes->ForEachReferenceInRange(a_player, kFlightScanUnits, [&](RE::TESObjectREFR* a_ref) {
                if (AsDestructible(a_ref)) {
                    flights.candidates.push_back(a_ref->GetFormID());
                }
                return RE::BSContainer::ForEachResult::kContinue;
            });
            if (flights.hit.size() > 256) {
                flights.hit.clear();
            }
        }
        for (const auto id : flights.candidates) {
            auto* ref = RE::TESForm::LookupByID<RE::TESObjectREFR>(id);
            const auto what = AsDestructible(ref);
            if (!what) {
                continue;
            }
            // Its 3D's bounding sphere, or a small one around its origin while it has no 3D.
            auto*        root = ref->Get3D();
            RE::NiPoint3 center = root ? root->worldBound.center : ref->GetPosition();
            const float  radius = root && root->worldBound.radius > 0.0f ? root->worldBound.radius : kObjectRadius;
            const float  at[3] = { center.x, center.y, center.z };
            if (Damage::SegmentDistance(path.from, path.to, at) > radius + kItemUnits) {
                continue;
            }
            const std::uint64_t key = (std::uint64_t(path.flight) << 32) | id;
            if (!flights.hit.insert(key).second) {
                continue;
            }
            const float damage = Damage::ThrownObject(what->web, path.speed);
            const char* result = DamageDestructible(ref, *what, damage);
            SKSE::log::info("combat: a thrown item hit {} {:08X} ({}) at {:.1f} m/s for {:.0f}, {}", what->web ? "the web" : "a destructible", id, what->model,
                path.speed, damage, result);
        }
    }

    void OnImpact(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size, float a_drunk)
    {
        if (a_size < sizeof(Protocol::ImpactEvent)) {
            SKSE::log::warn("combat: bad impact message ({} bytes)", a_size);
            return;
        }
        Protocol::ImpactEvent impact;
        std::memcpy(&impact, a_data, sizeof(impact));
        const bool  piss = impact.kind == Protocol::kImpactPiss;
        const char* what = piss                                       ? "piss" :
                           impact.kind == Protocol::kImpactVehicle ? "run-over" :
                           impact.kind == Protocol::kImpactPush    ? "push" :
                                                                     "throw";
        auto*       target = RE::TESForm::LookupByID<RE::Actor>(impact.actor);
        // A little past the listed range: the target may have moved on since the list.
        if (!target || !Standing(target) || target->GetPosition().GetDistance(a_player->GetPosition()) > kListUnits + kPunchMaxUnits) {
            SKSE::log::info("combat: {} on {:08X} dropped (gone, down or too far)", what, impact.actor);
            return;
        }
        auto* caster = a_player->GetMagicCaster(RE::MagicSystem::CastingSource::kInstant);
        auto* values = target->AsActorValueOwner();
        if (!caster || !values) {
            return;
        }
        const float max = values->GetPermanentActorValue(RE::ActorValue::kHealth);
        if (impact.kind == Protocol::kImpactPush) {
            // Skyrim's stagger stumble, sent to its graph: no hit, so no damage, no assault, no fight.
            // Too heavy to shove (a bear, a giant) stays put.
            if (target->GetPosition().GetDistance(a_player->GetPosition()) > kPunchMaxUnits ||
                Damage::BulkOf(MassOf(target), IsDragon(target)) != Damage::Bulk::kLight) {
                SKSE::log::info("combat: the player pushed {}, too far or too heavy to move", NameOf(target));
                return;
            }
            const float direction = Damage::StaggerDirection(target->GetAngleZ(), impact.direction[0], impact.direction[1]);
            target->SetGraphVariableFloat("staggerDirection"sv, direction);
            target->SetGraphVariableFloat("staggerMagnitude"sv, Damage::kPushStagger);
            const bool took = target->NotifyAnimationGraph("staggerStart"sv);
            SKSE::log::info("combat: the player pushed {} aside (stagger from {:.2f}){}", NameOf(target), direction, took ? "" : ", but its graph refused");
            return;
        }
        if (piss) {
            if (!state.piss) {
                return;
            }
            // Recast while the stream stays on: Skyrim refreshes the running poison, it doesn't stack.
            const float perSecond = Damage::PissPerSecond(max, IsDragon(target));
            caster->CastSpellImmediate(state.piss, false, target, 1.0f, false, perSecond, a_player);
            SKSE::log::info("combat: the player pissed on {}, poison {:.1f}/s for {:.0f} s of {:.0f} max health", NameOf(target), perSecond,
                Damage::kPissSeconds, max);
            return;
        }
        if (impact.kind == Protocol::kImpactVehicle) {
            if (!state.punch) {
                return;
            }
            // Knocked over first unless huge, away along the vehicle's way: the knock comes from
            // just behind the target's feet. Then the damage, the player's hostile spell like a
            // punch (assault). Light and heavy ones are thrown (Throws): the vehicle plowed through
            // them; a huge one stopped it.
            const auto   bulk = Damage::BulkOf(MassOf(target), IsDragon(target));
            const float  knock = bulk == Damage::Bulk::kHuge ? 0.0f : Damage::KnockOver(impact.speed);
            RE::NiPoint3 from = target->GetPosition();
            from.x -= impact.direction[0] * 100.0f;
            from.y -= impact.direction[1] * 100.0f;
            auto* process = target->GetActorRuntimeData().currentProcess;
            if (process && knock > 0.0f) {
                process->KnockExplosion(target, from, knock);
            }
            const float damage = Damage::RunOver(max, IsDragon(target), impact.speed);
            const float health = values->GetActorValue(RE::ActorValue::kHealth);
            if (damage > 0.0f) {
                caster->CastSpellImmediate(state.punch, false, target, 1.0f, false, damage, a_player);
            }
            SKSE::log::info("combat: the player ran {} over at {:.0f} km/h for {:.0f} of {:.0f} max health (it had {:.0f}), {}, knock {:.1f}",
                NameOf(target), impact.speed * 3.6f, damage, max, health,
                bulk == Damage::Bulk::kLight ? "light" : bulk == Damage::Bulk::kHeavy ? "heavy" : "huge", knock);
            if (bulk == Damage::Bulk::kHuge) {
                return;
            }
            // Havok units are meters, near enough: the vehicle's speed carries over as is.
            const float throwSpeed = impact.speed * Damage::kThrowShare;
            state.throws.push_back({ target->CreateRefHandle(),
                RE::hkVector4(impact.direction[0] * throwSpeed, impact.direction[1] * throwSpeed,
                    impact.direction[2] * throwSpeed + impact.speed * Damage::kThrowLift, 0.0f) });
            return;
        }
        if (impact.kind != Protocol::kImpactThrown || !state.punch || !state.stagger) {
            return;
        }
        // Sneak attack: the throw left the hand from sneak (the newest flight's launch; the player's
        // state now if no flight was seen lately) and the target does not see the player.
        const bool  sneaking = state.clock - flights.sneakAt < kFlightSneakSeconds ? flights.sneaking : a_player->IsSneaking();
        const int   detection = target->RequestDetectionLevel(a_player);
        const float sneak = Damage::SneakAttack(sneaking, detection);
        const float unarmed = Unarmed(a_player, target);
        const float damage = Damage::Thrown(Damage::Punch(unarmed), impact.speed, impact.mass, a_drunk) * sneak;
        if (damage <= 0.0f) {
            SKSE::log::info("combat: a throw at {:.1f} m/s bounced off {} harmlessly", impact.speed, NameOf(target));
            return;
        }
        const float health = values->GetActorValue(RE::ActorValue::kHealth);
        caster->CastSpellImmediate(state.punch, false, target, 1.0f, false, damage, a_player);
        caster->CastSpellImmediate(state.stagger, false, target, 1.0f, false, 0.0f, a_player);
        lastHit = { lastHit.count + 1, target->GetFormID(), true, damage, unarmed, a_drunk, impact.speed, sneak, impact.mass };
        if (sneak > 1.0f) {
            RE::SendHUDMessage::ShowHUDMessage(fmt::format("Sneak attack for {:.1f}x damage", sneak).c_str());
        }
        SKSE::log::info("combat: the player's throw at {:.1f} m/s ({:.2f} kg, x{:.2f} by weight, drunk {:.2f} x{:.1f}) hit {} for {:.0f}, it had {:.0f} of {:.0f} max health (from sneak {}, its detection level {}, x{:.1f})",
            impact.speed, impact.mass, Damage::ThrownMass(impact.mass), a_drunk, Damage::DrunkThrow(a_drunk), NameOf(target), damage, health, max, sneaking,
            detection, sneak);
    }

    void OnHorn(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        if (a_size < sizeof(Protocol::HornEvent)) {
            SKSE::log::warn("combat: bad horn message ({} bytes)", a_size);
            return;
        }
        Protocol::HornEvent horn;
        std::memcpy(&horn, a_data, sizeof(horn));
        auto* lists = RE::ProcessLists::GetSingleton();
        if (!lists) {
            return;
        }
        // The player sits in the vehicle, so everything is measured from it. Each actor in the way
        // dives aside out of the lane (Damage::HornDodge, its ragdoll thrown by Throws).
        const auto at = a_player->GetPosition();
        int        scared = 0;
        for (auto& handle : lists->highActorHandles) {
            auto actor = handle.get();
            if (!Standing(actor.get())) {
                continue;
            }
            const auto pos = actor->GetPosition();
            const float dx = pos.x - at.x, dy = pos.y - at.y;
            if (!Damage::InHornPath(dx, dy, horn.direction[0], horn.direction[1]) ||
                Damage::BulkOf(MassOf(actor.get()), IsDragon(actor.get())) == Damage::Bulk::kHuge) {
                continue;
            }
            // A dive already asked for (not a ragdoll yet, so still Standing) isn't asked again.
            const bool diving = std::any_of(state.throws.begin(), state.throws.end(),
                [&](const Throw& a_throw) { return a_throw.actor.get().get() == actor.get(); });
            auto* process = actor->GetActorRuntimeData().currentProcess;
            if (!diving && process) {
                float moveX, moveY;
                Damage::HornDodge(dx, dy, horn.direction[0], horn.direction[1], moveX, moveY);
                const float move = std::hypot(moveX, moveY);
                // Knocked from its side nearest the vehicle's line, then thrown over the move in
                // kHornDiveSeconds (Havok units are meters, near enough).
                RE::NiPoint3 from = pos;
                from.x -= moveX / move * 100.0f;
                from.y -= moveY / move * 100.0f;
                process->KnockExplosion(actor.get(), from, Damage::kHornDiveKnock);
                const float perSecond = static_cast<float>(Coords::kMetersPerUnit) / Damage::kHornDiveSeconds;
                state.throws.push_back({ actor->CreateRefHandle(), RE::hkVector4(moveX * perSecond, moveY * perSecond, Damage::kHornDiveLift, 0.0f) });
            }
            if (scared++ < 4) {
                SKSE::log::info("combat: the horn makes {} dive aside, {:.0f} units away{}", NameOf(actor.get()), pos.GetDistance(at),
                    diving ? " (already diving)" : "");
            }
        }
        SKSE::log::info("combat: the player honked at {:.0f} km/h, {} actors in the way", horn.speed * 3.6f, scared);
    }

    void OnMiddleFinger(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        if (a_size < sizeof(Protocol::MiddleFinger)) {
            SKSE::log::warn("combat: bad middle finger message ({} bytes)", a_size);
            return;
        }
        Protocol::MiddleFinger finger;
        std::memcpy(&finger, a_data, sizeof(finger));
        auto* lists = RE::ProcessLists::GetSingleton();
        if (!lists) {
            return;
        }
        const auto at = a_player->GetPosition();
        int        seen = 0;
        for (auto& handle : lists->highActorHandles) {
            auto  actor = handle.get();
            auto* person = actor.get();
            if (!Standing(person) || !(person->IsGuard() || (state.jarls && person->IsInFaction(state.jarls)))) {
                continue;
            }
            const auto pos = person->GetPosition();
            if (!Finger::InView(pos.x - at.x, pos.y - at.y, finger.direction[0], finger.direction[1])) {
                continue;
            }
            bool unused = false;
            if (!person->HasLineOfSight(a_player, unused)) {
                SKSE::log::info("combat: {} ({:08X}) is in front but doesn't see the finger", NameOf(person), person->GetFormID());
                continue;
            }
            seen++;
            auto* hold = person->GetCrimeFaction();
            if (!hold) {
                SKSE::log::info("combat: {} ({:08X}) saw the finger but answers to no hold", NameOf(person), person->GetFormID());
                continue;
            }
            if (!state.fingered.Take(person->GetFormID(), state.clock)) {
                SKSE::log::info("combat: {} ({:08X}) saw the finger again within a minute, no bounty", NameOf(person), person->GetFormID());
                continue;
            }
            a_player->ModCrimeGoldValue(hold, false, Finger::kBounty);
            RE::SendHUDMessage::ShowHUDMessage(fmt::format("{} didn't like the finger: {} gold bounty", NameOf(person), Finger::kBounty).c_str());
            SKSE::log::info("combat: {} ({:08X}) saw the finger: {} gold bounty in {:08X}, now {}", NameOf(person), person->GetFormID(), Finger::kBounty,
                hold->GetFormID(), a_player->GetCrimeGoldValue(hold));
        }
        SKSE::log::info("combat: the player gave the finger, {} guards or Jarls saw it", seen);
    }

    void Shouts(RE::PlayerCharacter* a_player)
    {
        // PerkeleRim.esp's shout and grunt lines ("Fus!", "Ro Dah!", "Gah!", ...) need this global at 0 to be the player's.
        if (!state.muted && state.muteVoice) {
            state.muteVoice->value = 1.0f;
            state.muted = true;
            SKSE::log::info("combat: Skyrim's shouts and grunts muted for the player");
        }
        if (!state.watching) {
            state.shouts.Reset();
            state.watching = true;
        }
        // How far the shout got while charging: by the release frame Skyrim may have cleared it (as
        // AhShitRim found), and a tap can be released before any frame sees it.
        auto* process = a_player->GetActorRuntimeData().currentProcess;
        auto* high = process ? process->high : nullptr;
        if (high && high->currentShout) {
            state.charging = high->currentShout;
            if (high->currentShoutVariation < RE::TESShout::VariationID::kTotal) {
                state.charged = static_cast<int>(high->currentShoutVariation);
            }
        }
        const float recovery = a_player->GetVoiceRecoveryTime();
        if (!state.shouts.Step(recovery)) {
            return;
        }
        auto* shout = state.charging ? state.charging : a_player->GetCurrentShout();
        float cooldowns[3] = {};
        for (int i = 0; shout && i < 3; i++) {
            cooldowns[i] = shout->variations[i].recoveryTime;
        }
        auto*                 values = a_player->AsActorValueOwner();
        const float           mult = values ? values->GetActorValue(RE::ActorValue::kShoutRecoveryMult) : 1.0f;
        Protocol::PlayerShout message{};
        message.shout = shout ? shout->GetFormID() : 0;
        message.kind = Shout::KindOf(message.shout);
        message.words = Shout::Words(state.charged, cooldowns, mult, recovery);
        // Drunk, the cooldown Skyrim just started is cut short (Damage::DrunkShoutCooldown), written
        // straight to the recovery time: a PerkeleRim.esp spell on ShoutRecoveryMult lengthened it in
        // game whichever sign it was cast with.
        float cooldown = recovery;
        if (state.drunk > 0.0f && high) {
            cooldown = Damage::DrunkShoutCooldown(recovery, state.drunk);
            high->voiceRecoveryTime = cooldown;
        }
        const bool sent = LinkService::Get().Send(Protocol::MessageType::PlayerShout, &message, sizeof(message));
        SKSE::log::info("combat: the player shouted {} ({:08X}, {} words{}), cooldown {:.1f} s (shout recovery x{:.2f}), drunk {:.2f}: {:.1f} s{}",
            shout ? shout->GetFullName() : "?", message.shout, message.words, state.charged < 0 ? " by the cooldown" : "", recovery, mult, state.drunk, cooldown,
            sent ? "" : ", but the ring to MSC is full");
        state.charged = -1;
        state.charging = nullptr;
    }

    void Guard(RE::PlayerCharacter* a_player, bool a_block, bool a_draw)
    {
        auto* actorState = a_player->AsActorState();
        if (!actorState) {
            return;
        }
        // The drawn hands (fists, a weapon) would show in Skyrim's first person over MSC's own hands.
        // Checked every frame: the model is rebuilt on equipment changes. Its children are hidden,
        // not the first-person 3D itself, which holds the drink Kumpo drinks (HeldDrinkView).
        if (auto* firstPerson = a_player->Get3D(true)) {
            ShowFirstPerson(firstPerson, false);
            state.culled = true;
        }
        // Drawing takes a moment; a block asked for meanwhile starts once the hands are out.
        if (actorState->GetWeaponState() != RE::WEAPON_STATE::kDrawn) {
            if (a_draw && actorState->GetWeaponState() == RE::WEAPON_STATE::kSheathed && --state.drawWait <= 0) {
                state.drawWait = 60;
                a_player->DrawWeaponMagicHands(true);
                state.drew = true;
                SKSE::log::info("combat: drawing the player's hands for MSC's guard");
            }
            a_block = false;
        }
        if (a_block != state.blocking) {
            state.blocking = a_block;
            SetBlocking(a_player, a_block);
            SKSE::log::info("combat: the player {} blocking", a_block ? "starts" : "stops");
            return;
        }
        // Put back if anything (a new graph after an equipment change) reset it.
        bool graph = a_block;
        if (a_player->GetGraphVariableBool("IsBlocking"sv, graph) && graph != a_block) {
            SetBlocking(a_player, a_block);
        }
    }

    void Drunk(RE::PlayerCharacter* a_player, float a_drunk, float a_delta)
    {
        state.clock += a_delta;
        state.drunkAge += a_delta;
        const float level = (std::clamp)(a_drunk, 0.0f, Damage::kDrunkLevels);
        state.drunk = level;
        const bool  on = state.drunkApplied > 0.0f;
        if (level <= 0.0f && !on) {
            return;
        }
        if (on && std::abs(level - state.drunkApplied) < Damage::kDrunkBuffStep && state.drunkAge < kDrunkRecast) {
            return;
        }
        auto* caster = a_player->GetMagicCaster(RE::MagicSystem::CastingSource::kInstant);
        if (!caster || std::ranges::any_of(state.drunkSpells, [](const RE::SpellItem* a_spell) { return a_spell == nullptr; })) {
            return;
        }
        if (level <= 0.0f) {
            DrunkOff(a_player);
            SKSE::log::info("combat: sober again, the drunk bonuses are off");
            return;
        }
        // A recast adds a second copy next to the running one instead of replacing it (both stack
        // until the old one runs out), so the old ones go first.
        if (on) {
            DispelDrunk(a_player);
        }
        const auto  buffs = Damage::Drunk(level);
        const float magnitudes[] = { buffs.armor, buffs.stamina, buffs.staminaRate };
        static_assert(std::size(magnitudes) == kDrunkSpellCount);
        for (std::size_t i = 0; i < kDrunkSpellCount; i++) {
            caster->CastSpellImmediate(state.drunkSpells[i], false, a_player, 1.0f, false, magnitudes[i], a_player);
        }
        // Logged per tenth of the way to full drunk. The values read are the player's before this
        // cast lands, so they show the last one took.
        const int tenth = static_cast<int>(Damage::DrunkShare(level) * 10.0f);
        if (tenth != state.drunkLogged) {
            state.drunkLogged = tenth;
            auto* values = a_player->AsActorValueOwner();
            SKSE::log::info("combat: drunk {:.2f}: casting armor +{:.0f}, stamina +{:.0f}, stamina regen +{:.0f}% (the player has armor {:.0f}, "
                            "max stamina {:.0f}, stamina regen {:.0f}%)",
                level, buffs.armor, buffs.stamina, buffs.staminaRate, values ? values->GetActorValue(RE::ActorValue::kDamageResist) : -1.0f,
                values ? values->GetPermanentActorValue(RE::ActorValue::kStamina) + a_player->GetActorValueModifier(RE::ACTOR_VALUE_MODIFIER::kTemporary, RE::ActorValue::kStamina) : -1.0f,
                values ? values->GetActorValue(RE::ActorValue::kStaminaRateMult) : -1.0f);
        }
        state.drunkApplied = level;
        state.drunkAge = 0.0f;
    }

    void Disarm(RE::PlayerCharacter* a_player)
    {
        auto* equip = RE::ActorEquipManager::GetSingleton();
        if (!equip) {
            return;
        }
        const char* refused = nullptr;
        for (const bool left : { false, true }) {
            auto* form = a_player->GetEquippedObject(left);
            if (!form) {
                continue;
            }
            if (auto* weapon = form->As<RE::TESObjectWEAP>(); weapon && weapon->GetFormID() != kUnarmed) {
                equip->UnequipObject(a_player, weapon);
                refused = "Fists only. Kumpo is a drunk, not a swordsman.";
                SKSE::log::info("combat: unequipped {} from the player's {} hand (fists only while MSC has it)", weapon->GetName(), left ? "left" : "right");
            } else if (auto* scroll = form->As<RE::ScrollItem>()) {
                equip->UnequipObject(a_player, scroll);
                refused = "Fists only. Kumpo doesn't do magic.";
                SKSE::log::info("combat: unequipped scroll {} from the player's {} hand", scroll->GetName(), left ? "left" : "right");
            } else if (auto* spell = form->As<RE::SpellItem>()) {
                a_player->DeselectSpell(spell);
                refused = "Fists only. Kumpo doesn't do magic.";
                SKSE::log::info("combat: unequipped spell {} from the player's {} hand", spell->GetName(), left ? "left" : "right");
            }
        }
        if (refused && state.clock - state.refusedAt >= kRefuseQuiet) {
            state.refusedAt = state.clock;
            RE::SendHUDMessage::ShowHUDMessage(refused);
        }
    }

    void Stop()
    {
        if (auto* player = RE::PlayerCharacter::GetSingleton()) {
            if (state.drunkApplied > 0.0f) {
                DrunkOff(player);
                SKSE::log::info("combat: the drunk bonuses are off (Skyrim has the player back)");
            }
            if (state.blocking) {
                SetBlocking(player, false);
                SKSE::log::info("combat: the player stops blocking (Skyrim has it back)");
            }
            if (state.drew && !player->IsDead()) {
                player->DrawWeaponMagicHands(false);
            }
            if (auto* firstPerson = player->Get3D(true); firstPerson && state.culled) {
                ShowFirstPerson(firstPerson, true);
            }
        }
        if (state.muted && state.muteVoice) {
            state.muteVoice->value = 0.0f;
            SKSE::log::info("combat: Skyrim's shouts and grunts are the player's again");
        }
        state.muted = false;
        state.watching = false;
        state.charged = -1;
        state.charging = nullptr;
        state.culled = false;
        state.blocking = false;
        state.drew = false;
        state.drawWait = 0;
        state.throws.clear();
        if (!state.sent) {
            return;
        }
        Protocol::ActorList empty{};
        empty.frame = ++state.frame;
        LinkService::Get().WriteActors(empty);
        state.sent = false;
    }
}
