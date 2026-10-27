#include "safe/MetalHit.h"

#include "safe/Safe.h"
#include "vehicle/Hitbox.h"

namespace PerkeleRim::MetalHit
{
    namespace
    {
        constexpr RE::FormID kUnarmed = 0x1F4;  // Skyrim.esm's Unarmed weapon: its impacts stand in for a fist's or a bite's
        constexpr int        kLogged = 16;      // the first metal hits are logged
        int                  logged = 0;

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

        void Hit(std::uint32_t a_vehicle, RE::TESObjectREFR* a_from, std::string_view a_what, RE::BGSImpactData* a_impact, const RE::NiPoint3& a_at, const RE::NiPoint3& a_normal)
        {
            if (a_impact) {
                Play(a_impact, a_at, a_normal);
            }
            Hitbox::Struck(a_vehicle);
            if (logged < kLogged) {
                logged++;
                SKSE::log::info("metal hit: {}'s {} hit vehicle {} instead of the player at ({:.0f},{:.0f},{:.0f}), impact {:08X}, hit {} on it",
                    a_from ? a_from->GetName() : "someone", a_what, a_vehicle, a_at.x, a_at.y, a_at.z, a_impact ? a_impact->GetFormID() : 0, Hitbox::Hits(a_vehicle));
            }
        }

        // The melee hit's entry, which builds the HitData (the grunt, the blood, the shake, the hit
        // reaction and the damage all come from there): the call to it in the melee collision handler,
        // as the Elden Parry mod hooks it.
        struct MeleeHook
        {
            static void thunk(RE::Actor* a_attacker, RE::Actor* a_victim, std::int64_t a_arg3, bool a_arg4, void* a_arg5)
            {
                const auto vehicle = a_attacker && a_victim && a_victim->IsPlayerRef() && a_attacker != a_victim ? Safe::Seat() : 0;
                if (vehicle == 0) {
                    original(a_attacker, a_victim, a_arg3, a_arg4, a_arg5);
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
                Hit(vehicle, a_attacker, form ? form->GetName() : "blow", OnMetalOr(set), at, normal);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // Projectile::AddImpact (vfunc 0xBD): an arrow or a spell bolt hitting a_ref. One reaching the
        // player in a safe seat is killed instead (it would stick in it, bleed it and make it grunt).
        template <int Kind>
        struct ImpactHook
        {
            static void thunk(RE::Projectile* a_this, RE::TESObjectREFR* a_ref, const RE::NiPoint3& a_at, const RE::NiPoint3& a_velocity, RE::hkpCollidable* a_collidable, std::int32_t a_arg6, std::uint32_t a_arg7)
            {
                const auto vehicle = a_this && a_ref && a_ref->IsPlayerRef() ? Safe::Seat() : 0;
                auto*      data = a_this ? &a_this->GetProjectileRuntimeData() : nullptr;
                const auto shooter = data ? data->shooter.get() : RE::NiPointer<RE::TESObjectREFR>();
                if (vehicle == 0 || shooter.get() == a_ref) {
                    original(a_this, a_ref, a_at, a_velocity, a_collidable, a_arg6, a_arg7);
                    return;
                }
                if (data->flags.any(RE::Projectile::Flags::kDestroyed)) {
                    return;  // killed at an earlier contact point this frame
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
                Hit(vehicle, shooter.get(), base ? base->GetName() : "projectile", OnMetalOr(set), at, normal);
                a_this->Kill();
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
    }
}
