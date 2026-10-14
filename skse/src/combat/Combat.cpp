#include "combat/Combat.h"

#include "damage.h"
#include "link/LinkService.h"
#include "player/PlayerSync.h"

#include <unordered_set>

namespace PerkeleRim::Combat
{
    namespace
    {
        constexpr auto          kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID    kPunchSpell = 0x904;   // PerkeleRimPunch: damage, magnitude set per cast
        constexpr RE::FormID    kStaggerSpell = 0x905;  // PerkeleRimPunchStagger
        constexpr RE::FormID    kPissSpell = 0x90E;     // PerkeleRimPiss: poison, magnitude (per second) set per cast
        constexpr float         kListUnits = 2800.0f;   // actors within 40 m go to MSC
        constexpr float         kPunchMaxUnits = 400.0f;  // a punch further than this away is stale
        constexpr float         kWatchUnits = 1000.0f;  // attackers the per-second log follows
        // Capsule size when an actor's bounds look wrong (a person).
        constexpr float kDefaultRadius = 20.0f;
        constexpr float kDefaultHeight = 128.0f;

        struct State
        {
            RE::SpellItem*                punch = nullptr;
            RE::SpellItem*                stagger = nullptr;
            RE::SpellItem*                piss = nullptr;
            std::uint32_t                 frame = 0;
            bool                          sent = false;  // a non-empty list went out
            float                         statTime = 0.0f;
            std::unordered_set<RE::FormID> logged;  // actors whose capsule size was logged
        } state;

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
            if (!(height > 40.0f && height < 2000.0f)) {
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
            if (state.logged.insert(a_pose.id).second) {
                SKSE::log::info("combat: {} ({:08X}) as a capsule r {:.0f} h {:.0f} (bounds {:.0f}x{:.0f}x{:.0f}, scale {:.2f})",
                    NameOf(a_actor), a_pose.id, radius, height, max.x - min.x, max.y - min.y, max.z - min.z, scale);
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

        class HitSink : public RE::BSTEventSink<RE::TESHitEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::TESHitEvent* a_event, RE::BSTEventSource<RE::TESHitEvent>*) override
            {
                if (a_event && PlayerSync::Owned() && a_event->target && a_event->target->IsPlayerRef()) {
                    auto* source = RE::TESForm::LookupByID(a_event->source);
                    SKSE::log::info("combat: {} hit the player with {} ({:08X}){}", NameOf(a_event->cause.get()),
                        source ? source->GetName() : "?", a_event->source,
                        a_event->flags.any(RE::TESHitEvent::Flag::kHitBlocked) ? ", blocked" : "");
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void OnDataLoaded()
    {
        auto* data = RE::TESDataHandler::GetSingleton();
        state.punch = data ? data->LookupForm<RE::SpellItem>(kPunchSpell, kPlugin) : nullptr;
        state.stagger = data ? data->LookupForm<RE::SpellItem>(kStaggerSpell, kPlugin) : nullptr;
        state.piss = data ? data->LookupForm<RE::SpellItem>(kPissSpell, kPlugin) : nullptr;
        static HitSink hits;
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESHitEvent>(&hits);
        }
        SKSE::log::info("combat: punch spells {}", state.punch && state.stagger ? "found" : "missing (PerkeleRim.esp not loaded or too old), punches and throws do nothing");
        SKSE::log::info("combat: piss spell {}", state.piss ? "found" : "missing (PerkeleRim.esp not loaded or too old), piss does nothing");
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

        state.statTime += a_delta;
        if (state.statTime >= 1.0f) {
            state.statTime = 0.0f;
            LogAttackers(a_player, nearby);
        }
    }

    void OnPunch(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size, float a_drunk)
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
        const float damage = Damage::Punch(max, IsDragon(target), a_drunk);
        const float health = values ? values->GetActorValue(RE::ActorValue::kHealth) : 0.0f;  // the spell lands next frame
        caster->CastSpellImmediate(state.punch, false, target, 1.0f, false, damage, a_player);
        caster->CastSpellImmediate(state.stagger, false, target, 1.0f, false, 0.0f, a_player);
        SKSE::log::info("combat: the player punched {} for {:.0f} of {:.0f} max health (drunk {:.1f}), it had {:.0f}", NameOf(target), damage, max,
            a_drunk, health);
    }

    void OnImpact(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        if (a_size < sizeof(Protocol::ImpactEvent)) {
            SKSE::log::warn("combat: bad impact message ({} bytes)", a_size);
            return;
        }
        Protocol::ImpactEvent impact;
        std::memcpy(&impact, a_data, sizeof(impact));
        const bool piss = impact.kind == Protocol::kImpactPiss;
        auto*      target = RE::TESForm::LookupByID<RE::Actor>(impact.actor);
        // A little past the listed range: the target may have moved on since the list.
        if (!target || !Standing(target) || target->GetPosition().GetDistance(a_player->GetPosition()) > kListUnits + kPunchMaxUnits) {
            SKSE::log::info("combat: {} on {:08X} dropped (gone, down or too far)", piss ? "piss" : "throw", impact.actor);
            return;
        }
        auto* caster = a_player->GetMagicCaster(RE::MagicSystem::CastingSource::kInstant);
        auto* values = target->AsActorValueOwner();
        if (!caster || !values) {
            return;
        }
        const float max = values->GetPermanentActorValue(RE::ActorValue::kHealth);
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
        if (impact.kind != Protocol::kImpactThrown || !state.punch || !state.stagger) {
            return;
        }
        const float damage = Damage::Thrown(max, IsDragon(target), impact.speed);
        if (damage <= 0.0f) {
            SKSE::log::info("combat: a throw at {:.1f} m/s bounced off {} harmlessly", impact.speed, NameOf(target));
            return;
        }
        caster->CastSpellImmediate(state.punch, false, target, 1.0f, false, damage, a_player);
        caster->CastSpellImmediate(state.stagger, false, target, 1.0f, false, 0.0f, a_player);
        SKSE::log::info("combat: the player's throw at {:.1f} m/s hit {} for {:.0f} of {:.0f} max health", impact.speed, NameOf(target), damage, max);
    }

    void Stop()
    {
        if (!state.sent) {
            return;
        }
        Protocol::ActorList empty{};
        empty.frame = ++state.frame;
        LinkService::Get().WriteActors(empty);
        state.sent = false;
    }
}
