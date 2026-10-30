#include "safe/MetalHit.h"

#include "safe/Safe.h"
#include "vehicle/Hitbox.h"

namespace PerkeleRim::MetalHit
{
    namespace
    {
        constexpr RE::FormID kUnarmed = 0x1F4;  // Skyrim.esm's Unarmed weapon: its impacts stand in for a fist's or a bite's
        constexpr int        kLogged = 16;      // the first metal hits are logged
        std::atomic<int>     logged = 0;        // projectiles and spells land on Skyrim's job threads too

        // The last spell effect that sparked, so a spell's other effects in the same cast don't spark again.
        struct LastSpell
        {
            std::mutex                            lock;
            const RE::TESObjectREFR*              caster = nullptr;
            const RE::MagicItem*                  item = nullptr;
            std::chrono::steady_clock::time_point at;
        } lastSpell;
        constexpr auto kSameCast = std::chrono::milliseconds(100);

        // What the impact data set a_set plays on metal (the hitbox capsules' material), null for none.
        RE::BGSImpactData* OnMetal(RE::BGSImpactDataSet* a_set)
        {
            if (!a_set) {
                return nullptr;
            }
            for (const auto id : { RE::MATERIAL_ID::kMetalSolid, RE::MATERIAL_ID::kMetalHeavy, RE::MATERIAL_ID::kMetalLight }) {
                const auto* material = RE::BGSMaterialType::GetMaterialType(id);
                if (const auto it = material ? a_set->impactMap.find(material) : a_set->impactMap.end(); it != a_set->impactMap.end() && it->second) {
                    return it->second;
                }
            }
            return nullptr;
        }

        // a_set's impact on metal, or a fist's when it has none.
        RE::BGSImpactData* OnMetalOr(RE::BGSImpactDataSet* a_set)
        {
            if (auto* impact = OnMetal(a_set)) {
                return impact;
            }
            const auto* unarmed = RE::TESForm::LookupByID<RE::TESObjectWEAP>(kUnarmed);
            return unarmed ? OnMetal(unarmed->impactDataSet) : nullptr;
        }

        RE::BGSImpactDataSet* WeaponSet(const RE::TESForm* a_form)
        {
            const auto* weapon = a_form ? a_form->As<RE::TESObjectWEAP>() : nullptr;
            return weapon ? weapon->impactDataSet : nullptr;
        }

        // Where the hit lands on vehicle a_vehicle, coming from a_from: its hitbox's surface, facing out.
        // Without the hitbox in the world (driving fast), at a_from, facing back along a_fallback.
        void Land(std::uint32_t a_vehicle, const RE::NiPoint3& a_from, const RE::NiPoint3& a_fallback, RE::NiPoint3& a_at, RE::NiPoint3& a_normal)
        {
            Protocol::VehicleState pose{};
            Hitbox::Box            box{};
            if (Hitbox::Where(a_vehicle, pose, box)) {
                const float from[3] = { a_from.x, a_from.y, a_from.z };
                float       point[3];
                float       normal[3];
                Hitbox::Surface(pose.position, pose.rotation, box, from, point, normal);
                a_at = RE::NiPoint3(point[0], point[1], point[2]);
                a_normal = RE::NiPoint3(normal[0], normal[1], normal[2]);
                return;
            }
            a_at = a_from;
            const float length = a_fallback.Length();
            a_normal = length > 0.0f ? a_fallback / length : RE::NiPoint3(0.0f, 0.0f, 1.0f);
        }

        // a_impact's model (the sparks) and sounds at a_at, the model's +Y along a_normal.
        void Play(RE::BGSImpactData* a_impact, const RE::NiPoint3& a_at, const RE::NiPoint3& a_normal)
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* cell = player ? player->GetParentCell() : nullptr;
            if (const char* model = a_impact->GetModel(); cell && model && *model) {
                const RE::NiPoint3 angles(-std::asin(std::clamp(a_normal.z, -1.0f, 1.0f)), 0.0f, std::atan2(a_normal.x, a_normal.y));
                const float        duration = a_impact->data.effectDuration > 0.0f ? a_impact->data.effectDuration : 1.0f;
                RE::BSTempEffectParticle::Spawn(cell, duration, model, angles, a_at, 1.0f, 7, nullptr);
            }
            auto* audio = RE::BSAudioManager::GetSingleton();
            for (auto* sound : { a_impact->sound1, a_impact->sound2 }) {
                RE::BSSoundHandle handle;
                if (audio && sound && audio->GetSoundHandle(handle, sound)) {
                    handle.SetPosition(a_at);
                    handle.Play();
                }
            }
        }

        // a_via names the hook that caught it (melee, projectile, spell effect), for the log.
        void Hit(std::uint32_t a_vehicle, RE::TESObjectREFR* a_from, std::string_view a_what, std::string_view a_via, RE::BGSImpactData* a_impact, const RE::NiPoint3& a_at, const RE::NiPoint3& a_normal)
        {
            if (a_impact) {
                Play(a_impact, a_at, a_normal);
            }
            Hitbox::Struck(a_vehicle);
            if (logged.fetch_add(1) < kLogged) {
                SKSE::log::info("metal hit: {}'s {} ({}) hit vehicle {} instead of the player at ({:.0f},{:.0f},{:.0f}), impact {:08X}, hit {} on it",
                    a_from ? a_from->GetName() : "someone", a_what, a_via, a_vehicle, a_at.x, a_at.y, a_at.z, a_impact ? a_impact->GetFormID() : 0, Hitbox::Hits(a_vehicle));
            }
        }

        // The melee hit's entry, which builds the HitData (the grunt, the blood, the shake, the hit
        // reaction and the damage all come from there): the call to it in the melee collision handler,
        // as the Elden Parry mod hooks it. The caller passes four arguments and ignores the result.
        struct MeleeHook
        {
            static void thunk(RE::Actor* a_attacker, RE::Actor* a_victim, std::int64_t a_arg3, bool a_arg4)
            {
                const auto vehicle = a_attacker && a_victim && a_victim->IsPlayerRef() && a_attacker != a_victim ? Safe::Seat() : 0;
                if (vehicle == 0) {
                    original(a_attacker, a_victim, a_arg3, a_arg4);
                    return;
                }
                auto* form = a_attacker->GetEquippedObject(false);
                auto* set = WeaponSet(form);
                if (!set) {
                    form = a_attacker->GetEquippedObject(true);
                    set = WeaponSet(form);
                }
                // From the blade, or for a creature from about its mouth.
                RE::NiPoint3 from = a_attacker->GetPosition() + RE::NiPoint3(0.0f, 0.0f, a_attacker->GetHeight() * 0.6f);
                if (auto* root = a_attacker->Get3D(); root) {
                    if (auto* weapon = root->GetObjectByName("WEAPON"); weapon) {
                        from = weapon->world.translate;
                    }
                }
                RE::NiPoint3 at;
                RE::NiPoint3 normal;
                Land(vehicle, from, a_attacker->GetPosition() - a_victim->GetPosition(), at, normal);
                Hit(vehicle, a_attacker, form ? form->GetName() : "blow", "melee", OnMetalOr(set), at, normal);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // Projectile::AddImpact (vfunc 0xBD): an arrow, a spell bolt, flames, a beam or a cone hitting
        // a_ref. One reaching the player in a safe seat is killed instead (it would stick in it, burn
        // or bleed it and make it grunt). A flames spell streams short-lived projectiles, so killing
        // each just stops that one.
        //
        // It returns the impact it added (CommonLib declares it void): its callers mark it handled,
        // and returning garbage there crashed. All seven call sites in Skyrim 1.7.104 take null as "no
        // impact". The arrow collision handler (44204) then still hands the hit to the actor when
        // the projectile's weaponDamage is above 0, so a killed one gets 0.
        template <int Kind>
        struct ImpactHook
        {
            static RE::Projectile::ImpactData* thunk(RE::Projectile* a_this, RE::TESObjectREFR* a_ref, const RE::NiPoint3& a_at, const RE::NiPoint3& a_velocity, RE::hkpCollidable* a_collidable, std::int32_t a_arg6, std::uint32_t a_arg7)
            {
                const auto vehicle = a_this && a_ref && a_ref->IsPlayerRef() ? Safe::Seat() : 0;
                auto*      data = vehicle != 0 ? &a_this->GetProjectileRuntimeData() : nullptr;
                const auto shooter = data ? data->shooter.get() : RE::NiPointer<RE::TESObjectREFR>();
                if (vehicle == 0 || shooter.get() == a_ref) {
                    return original(a_this, a_ref, a_at, a_velocity, a_collidable, a_arg6, a_arg7);
                }
                data->weaponDamage = 0.0f;
                if (data->flags.any(RE::Projectile::Flags::kDestroyed)) {
                    return nullptr;  // killed at an earlier contact point this frame
                }
                RE::BGSImpactDataSet* set = nullptr;
                if (auto* spell = data->spell) {
                    const auto* effect = spell->GetCostliestEffectItem();
                    set = effect && effect->baseEffect ? effect->baseEffect->data.impactDataSet : nullptr;
                }
                if (!set) {
                    set = data->weaponSource ? data->weaponSource->impactDataSet : nullptr;
                }
                auto* base = a_this->GetProjectileBase();
                RE::NiPoint3 at;
                RE::NiPoint3 normal;
                Land(vehicle, a_at, RE::NiPoint3() - a_velocity, at, normal);
                Hit(vehicle, shooter.get(), base ? base->GetName() : "projectile", "projectile", OnMetalOr(set), at, normal);
                a_this->Kill();
                return nullptr;
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // MagicTarget::AddTarget (vfunc 1 of the player's MagicTarget vtable): a spell effect landing
        // on the player, whatever brought it (a projectile the hook above missed, an explosion, a
        // touch spell). A hostile one from someone else in a safe seat is dropped (no burn, no grunt,
        // no damage) and sparks on the vehicle once per cast. The plugin's own spells on the player
        // (drunk, punch) are cast by the player and pass.
        struct SpellHook
        {
            static bool thunk(RE::MagicTarget* a_this, RE::MagicTarget::AddTargetData& a_data)
            {
                auto*      caster = a_data.caster;
                const auto vehicle = caster && !caster->IsPlayerRef() && a_data.effect && a_data.effect->IsHostile() ? Safe::Seat() : 0;
                if (vehicle == 0) {
                    return original(a_this, a_data);
                }
                {
                    const auto            now = std::chrono::steady_clock::now();
                    const std::lock_guard guard(lastSpell.lock);
                    if (lastSpell.caster == caster && lastSpell.item == a_data.magicItem && now - lastSpell.at < kSameCast) {
                        return false;
                    }
                    lastSpell.caster = caster;
                    lastSpell.item = a_data.magicItem;
                    lastSpell.at = now;
                }
                auto*              player = RE::PlayerCharacter::GetSingleton();
                auto*              base = a_data.effect->baseEffect;
                const RE::NiPoint3 from = a_data.areaTarget ? a_data.explosionPoint : caster->GetPosition() + RE::NiPoint3(0.0f, 0.0f, caster->GetHeight() * 0.6f);
                RE::NiPoint3       at;
                RE::NiPoint3       normal;
                Land(vehicle, from, player ? caster->GetPosition() - player->GetPosition() : RE::NiPoint3(), at, normal);
                Hit(vehicle, caster, a_data.magicItem ? a_data.magicItem->GetName() : "spell", "spell effect", OnMetalOr(base ? base->data.impactDataSet : nullptr), at, normal);
                return false;
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };
    }

    void Install()
    {
        // SE 1.5.97 and AE: a call (E8) to the hit's entry. Anything else is another build: no hook,
        // and hits on a seated player just stay harmless as Safe makes them.
        REL::Relocation<std::uintptr_t> melee{ RELOCATION_ID(37650, 38603), REL::Relocate(0x38B, 0x45A) };
        if (*reinterpret_cast<const std::uint8_t*>(melee.address()) == 0xE8) {
            MeleeHook::original = SKSE::GetTrampoline().write_call<5>(melee.address(), MeleeHook::thunk);
        } else {
            SKSE::log::warn("metal hit: no call at the melee hit's entry in this Skyrim build, melee hits on a seated player grunt and bleed");
        }
        REL::Relocation<std::uintptr_t> arrow{ RE::VTABLE_ArrowProjectile[0] };
        ImpactHook<0>::original = arrow.write_vfunc(0xBD, ImpactHook<0>::thunk);
        REL::Relocation<std::uintptr_t> missile{ RE::VTABLE_MissileProjectile[0] };
        ImpactHook<1>::original = missile.write_vfunc(0xBD, ImpactHook<1>::thunk);
        REL::Relocation<std::uintptr_t> flame{ RE::VTABLE_FlameProjectile[0] };
        ImpactHook<2>::original = flame.write_vfunc(0xBD, ImpactHook<2>::thunk);
        REL::Relocation<std::uintptr_t> beam{ RE::VTABLE_BeamProjectile[0] };
        ImpactHook<3>::original = beam.write_vfunc(0xBD, ImpactHook<3>::thunk);
        REL::Relocation<std::uintptr_t> cone{ RE::VTABLE_ConeProjectile[0] };
        ImpactHook<4>::original = cone.write_vfunc(0xBD, ImpactHook<4>::thunk);
        REL::Relocation<std::uintptr_t> target{ RE::VTABLE_PlayerCharacter[4] };  // its MagicTarget
        SpellHook::original = target.write_vfunc(0x1, SpellHook::thunk);
    }
}
