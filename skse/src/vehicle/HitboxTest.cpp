#include "vehicle/HitboxTest.h"

#include <unordered_set>

#include "Ini.h"
#include "coords.h"
#include "vehicle/Hitbox.h"

namespace PerkeleRim::HitboxTest
{
    namespace
    {
        constexpr RE::FormID kArcher = 0x000F62D8;  // Skyrim.esm LvlBanditMissileAggro1536, AI off: only the shooters of our arrows
        constexpr RE::FormID kMelee = 0x000B6FCD;   // Skyrim.esm LvlBanditMelee1HAggro1024
        constexpr RE::FormID kArrow = 0x0001397D;   // Skyrim.esm IronArrow
        constexpr RE::FormID kBow = 0x00013985;     // Skyrim.esm Hunting Bow
        // Skyrim checks the line from an arrow's shooter to where it starts first (it hit the ground on
        // the way from an archer far off), so every arrow starts at its archer: one just behind the van,
        // away from Kumpo, one beside Kumpo for the control shots.
        constexpr float      kArcherBehind = 300.0f;  // units behind the van's center, away from Kumpo
        constexpr float      kArcherSide = 600.0f;    // ...and beside Kumpo
        constexpr float      kMeleeBehind = 600.0f;
        constexpr float      kMeleeAside = 200.0f;    // off the arrows' line
        constexpr float      kChest = 90.0f;          // above Kumpo's feet, what the control arrows aim at
        constexpr float      kDropHeight = 150.0f;   // placed this far above the van's origin, to fall onto the ground
        constexpr float      kVanReach = 2500.0f;    // the van must be this close to Kumpo
        constexpr float      kWaitSeconds = 120.0f;  // for the van to be solid near Kumpo
        constexpr float      kStandSeconds = 3.0f;   // ...and to have stood there this long (a called van lands first)
        constexpr float      kRunSeconds = 60.0f;
        constexpr float      kSampleSeconds = 0.5f;
        constexpr float      kShotEvery = 2.0f;
        constexpr int        kShots = 12;             // every third one a control shot from the side
        constexpr float      kImpactRange = 1500.0f;  // impacts this close to the van are logged

        struct State
        {
            bool                checked = false;
            bool                done = false;
            bool                started = false;
            float               waited = 0.0f;
            float               stood = 0.0f;  // the van has been solid near Kumpo this long
            float               time = 0.0f;
            float               sample = 0.0f;
            float               shotAt = 0.0f;
            int                 shots = 0;
            int                 through = 0, side = 0;                   // arrows fired of each kind
            int                 throughStopped = 0, throughOnKumpo = 0;  // where they ended
            int                 sideOnKumpo = 0;
            std::unordered_set<std::uint32_t> throughShots, sideShots, seen;  // projectile handles
            RE::ObjectRefHandle archer;      // behind the van
            RE::ObjectRefHandle sideArcher;  // beside Kumpo
            RE::ObjectRefHandle melee;
            float               closest = 1e9f;  // the melee bandit's least distance to Kumpo
            float               deepest = 0.0f;  // the deepest he ever stood inside the van's box
            int                 inside = 0;      // samples he stood inside it
            int                 obstaclesAtStart = -1;
        } state;

        // How far inside a_box (in the van's frame) a world point is, 0 when outside.
        float Depth(const Protocol::VehicleState& a_van, const Hitbox::Box& a_box, const RE::NiPoint3& a_at)
        {
            float m[3][3];
            Coords::ToMatrix({ a_van.rotation[0], a_van.rotation[1], a_van.rotation[2], a_van.rotation[3] }, m);
            const float d[3] = { a_at.x - a_van.position[0], a_at.y - a_van.position[1], a_at.z - a_van.position[2] };
            float       depth = 1e9f;
            for (int k = 0; k < 3; k++) {
                const float local = m[0][k] * d[0] + m[1][k] * d[1] + m[2][k] * d[2];  // transpose: world to van
                depth = (std::min)(depth, (std::min)(local - a_box.lo[k], a_box.hi[k] - local));
            }
            return (std::max)(depth, 0.0f);
        }

        // Navmesh obstacles (cuts) in the loaded cells: Skyrim's own way of walking around things.
        int Obstacles()
        {
            int   count = 0;
            auto* tes = RE::TES::GetSingleton();
            if (!tes) {
                return 0;
            }
            tes->ForEachCell([&](RE::TESObjectCELL* a_cell) {
                auto* array = a_cell ? a_cell->GetRuntimeData().navMeshes : nullptr;
                if (array) {
                    for (const auto& navmesh : array->navMeshes) {
                        count += navmesh ? static_cast<int>(navmesh->obstacles.size()) : 0;
                    }
                }
            });
            return count;
        }

        RE::ObjectRefHandle Place(RE::PlayerCharacter* a_player, RE::FormID a_base, const RE::NiPoint3& a_at, const char* a_what)
        {
            auto* base = RE::TESForm::LookupByID<RE::TESBoundObject>(a_base);
            auto  ref = base ? a_player->PlaceObjectAtMe(base, false) : nullptr;
            if (!ref) {
                SKSE::log::error("hitbox test: couldn't place the {} ({:08X})", a_what, a_base);
                return {};
            }
            ref->SetPosition(a_at);
            SKSE::log::info("hitbox test: {} {} ({:08X}) placed at ({:.0f},{:.0f},{:.0f})", a_what, ref->GetName(), ref->GetFormID(), a_at.x, a_at.y, a_at.z);
            return ref->GetHandle();
        }

        void Remove(RE::ObjectRefHandle a_handle)
        {
            if (auto ref = a_handle.get()) {
                ref->Disable();
                ref->SetDelete(true);
            }
        }

        bool Start(RE::PlayerCharacter* a_player, float a_delta)
        {
            Protocol::VehicleState van{};
            Hitbox::Box            box{};
            if (!Hitbox::Where(Protocol::kVehicleHayosiko, van, box)) {
                return false;
            }
            const RE::NiPoint3 at(van.position[0], van.position[1], van.position[2]);
            const auto         kumpo = a_player->GetPosition();
            RE::NiPoint3       away(at.x - kumpo.x, at.y - kumpo.y, 0.0f);
            const float        distance = away.Length();
            if (distance > kVanReach || distance < 1.0f) {
                state.stood = 0.0f;
                return false;  // until it is called (]) or the wait runs out
            }
            state.stood += a_delta;
            if (state.stood < kStandSeconds) {
                return false;
            }
            away /= distance;
            state.obstaclesAtStart = Obstacles();
            SKSE::log::info("hitbox test: armed, the van at ({:.0f},{:.0f},{:.0f}) {:.0f} units from Kumpo at ({:.0f},{:.0f},{:.0f}), {} navmesh obstacles loaded", at.x,
                at.y, at.z, distance, kumpo.x, kumpo.y, kumpo.z, state.obstaclesAtStart);
            const RE::NiPoint3 aside(-away.y, away.x, 0.0f);
            const RE::NiPoint3 drop(0.0f, 0.0f, kDropHeight);
            state.archer = Place(a_player, kArcher, at + away * kArcherBehind + drop, "archer behind the van");
            state.sideArcher = Place(a_player, kArcher, kumpo + aside * kArcherSide + drop, "archer beside Kumpo");
            for (auto handle : { state.archer, state.sideArcher }) {
                if (auto archer = handle.get(); archer && archer->As<RE::Actor>()) {
                    archer->As<RE::Actor>()->EnableAI(false);
                }
            }
            state.melee = Place(a_player, kMelee, at + away * kMeleeBehind + aside * kMeleeAside + drop, "melee bandit");
            return true;
        }

        // What a projectile's ray from a_from to a_to meets first in the player's world: a check of
        // the capsules that doesn't depend on how an arrow flies.
        void Probe(RE::PlayerCharacter* a_player, const RE::NiPoint3& a_from, const RE::NiPoint3& a_to)
        {
            auto* cell = a_player->GetParentCell();
            auto* world = cell ? cell->GetbhkWorld() : nullptr;
            if (!world) {
                return;
            }
            const float     scale = RE::bhkWorld::GetWorldScale();
            RE::bhkPickData pick;
            pick.rayInput.from = RE::hkVector4(a_from.x * scale, a_from.y * scale, a_from.z * scale, 0.0f);
            pick.rayInput.to = RE::hkVector4(a_to.x * scale, a_to.y * scale, a_to.z * scale, 0.0f);
            pick.rayInput.filterInfo.SetCollisionLayer(RE::COL_LAYER::kProjectile);
            {
                RE::BSReadLockGuard lock(world->worldLock);
                world->PickObject(pick);
            }
            const auto* hit = pick.rayOutput.rootCollidable;
            const float at = a_from.GetDistance(a_to) * (hit ? pick.rayOutput.hitFraction : 1.0f);
            SKSE::log::info("hitbox test: a projectile's ray from ({:.0f},{:.0f},{:.0f}) meets {} after {:.0f} units", a_from.x, a_from.y, a_from.z,
                !hit              ? std::string("nothing") :
                Hitbox::Owns(hit) ? std::string("the van's hitbox") :
                                    fmt::format("layer {}", static_cast<int>(hit->GetCollisionLayer())),
                at);
        }

        // One arrow from an archer at Kumpo: through the van from the one behind it, level at the
        // middle of the van's box; or at his chest from the one beside him.
        void Shoot(RE::PlayerCharacter* a_player, const Protocol::VehicleState& a_van, const Hitbox::Box& a_box, bool a_through)
        {
            auto  archer = (a_through ? state.archer : state.sideArcher).get();
            auto* shooter = archer ? archer->As<RE::Actor>() : nullptr;
            auto* ammo = RE::TESForm::LookupByID<RE::TESAmmo>(kArrow);
            auto* bow = RE::TESForm::LookupByID<RE::TESObjectWEAP>(kBow);
            if (!shooter || !ammo || !bow) {
                SKSE::log::warn("hitbox test: no shooter, arrow or bow for a shot");
                return;
            }
            auto from = shooter->GetPosition();
            auto target = a_player->GetPosition();
            if (a_through) {
                from.z = target.z = a_van.position[2] + (a_box.lo[2] + a_box.hi[2]) * 0.5f;
                Probe(a_player, from, target);
            } else {
                from.z += kChest;
                target.z += kChest;
            }
            const auto                          d = target - from;
            const float                         flat = std::sqrt(d.x * d.x + d.y * d.y);
            const RE::Projectile::ProjectileRot angles{ -std::atan2(d.z, flat), std::atan2(d.x, d.y) };
            RE::ProjectileHandle                handle;
            RE::Projectile::LaunchArrow(&handle, shooter, ammo, bow, from, angles);
            const auto id = handle.native_handle();
            (a_through ? state.throughShots : state.sideShots).insert(id);
            (a_through ? state.through : state.side)++;
            SKSE::log::info("hitbox test: {} arrow {:08X} from ({:.0f},{:.0f},{:.0f}) at Kumpo", a_through ? "through-the-van" : "control", id, from.x,
                from.y, from.z);
        }

        // Every new impact of a projectile near the van: what it hit (one of the van's capsules,
        // Kumpo, anything else) and on which layer.
        void Impacts(RE::PlayerCharacter* a_player, const RE::NiPoint3& a_van)
        {
            auto* manager = RE::Projectile::Manager::GetSingleton();
            if (!manager) {
                return;
            }
            RE::BSSpinLockGuard guard(manager->projectileLock);
            for (auto* list : { &manager->limited, &manager->unlimited }) {
                for (auto& handle : *list) {
                    auto  ptr = handle.get();
                    auto* projectile = ptr.get();
                    if (!projectile || projectile->GetProjectileRuntimeData().impacts.empty()) {
                        continue;
                    }
                    const std::uint32_t id = handle.native_handle();
                    if (state.seen.contains(id)) {
                        continue;
                    }
                    const auto at = projectile->GetPosition();
                    if (at.GetDistance(a_van) > kImpactRange) {
                        continue;
                    }
                    state.seen.insert(id);
                    const bool through = state.throughShots.contains(id);
                    const bool side = state.sideShots.contains(id);
                    for (auto* impact : projectile->GetProjectileRuntimeData().impacts) {
                        if (!impact) {
                            continue;
                        }
                        const bool ours = impact->colObj && Hitbox::Owns(impact->colObj.get());
                        const auto collidee = impact->collidee.get();
                        const bool kumpo = collidee.get() == a_player;
                        state.throughStopped += through && ours ? 1 : 0;
                        state.throughOnKumpo += through && kumpo ? 1 : 0;
                        state.sideOnKumpo += side && kumpo ? 1 : 0;
                        SKSE::log::info("hitbox test: projectile {:08X}{} hit {} on layer {} at ({:.0f},{:.0f},{:.0f}), {:.0f} from the van", id,
                            through ? " (through-the-van)" : side ? " (control)" : "", ours ? "the van's hitbox" : kumpo ? "Kumpo" : collidee ? collidee->GetName() : "no reference",
                            static_cast<int>(impact->collidedLayer.get()), at.x, at.y, at.z, at.GetDistance(a_van));
                    }
                }
            }
        }

        void Finish()
        {
            state.done = true;
            Remove(state.archer);
            Remove(state.sideArcher);
            Remove(state.melee);
            SKSE::log::info("hitbox test: done: {} through-the-van arrows, {} stopped by the van, {} on Kumpo; {} control arrows, {} on Kumpo; {} projectile(s) counted on the van; "
                            "the melee bandit came within {:.0f} units of Kumpo and stood inside the van's box in {} of {} samples, at most {:.0f} units deep; navmesh obstacles {} at the start, {} now",
                state.through, state.throughStopped, state.throughOnKumpo, state.side, state.sideOnKumpo, Hitbox::Hits(Protocol::kVehicleHayosiko), state.closest,
                state.inside, static_cast<int>(kRunSeconds / kSampleSeconds), state.deepest, state.obstaclesAtStart, Obstacles());
        }
    }

    void Update(RE::PlayerCharacter* a_player, float a_delta)
    {
        if (state.done || !a_player) {
            return;
        }
        if (!state.checked) {
            state.checked = true;
            state.done = Ini::Value("HitboxTest") != "1";
            if (state.done) {
                return;
            }
            SKSE::log::info("hitbox test: HitboxTest=1 in PerkeleRim.ini, waiting for the van");
        }
        if (!state.started) {
            state.waited += a_delta;
            if (Start(a_player, a_delta)) {
                state.started = true;
            } else if (state.waited > kWaitSeconds) {
                SKSE::log::warn("hitbox test: the van wasn't solid within {:.0f} units of Kumpo within {:.0f} s, not run", kVanReach, kWaitSeconds);
                state.done = true;
            }
            return;
        }

        // Kumpo stays alive whatever lands on him.
        a_player->AsActorValueOwner()->RestoreActorValue(RE::ActorValue::kHealth, 10000.0f);

        Protocol::VehicleState van{};
        Hitbox::Box            box{};
        const bool             solid = Hitbox::Where(Protocol::kVehicleHayosiko, van, box);
        const RE::NiPoint3     at(van.position[0], van.position[1], van.position[2]);
        state.time += a_delta;
        if (solid) {
            Impacts(a_player, at);
        }

        state.shotAt += a_delta;
        if (solid && state.shots < kShots && state.time > 3.0f && state.shotAt >= kShotEvery) {
            state.shotAt = 0.0f;
            Shoot(a_player, van, box, state.shots % 3 != 2);
            state.shots++;
        }

        state.sample += a_delta;
        if (state.sample >= kSampleSeconds) {
            state.sample = 0.0f;
            auto melee = state.melee.get();
            if (melee && solid) {
                const auto  where = melee->GetPosition();
                const float depth = Depth(van, box, where);
                const float apart = where.GetDistance(a_player->GetPosition());
                state.closest = (std::min)(state.closest, apart);
                state.deepest = (std::max)(state.deepest, depth);
                state.inside += depth > 0.0f ? 1 : 0;
                if (static_cast<int>(state.time * 2.0f) % 4 == 0) {  // every 2 s
                    SKSE::log::info("hitbox test: {:.1f} s: melee bandit at ({:.0f},{:.0f},{:.0f}), {:.0f} from Kumpo, {:.0f} inside the van's box; {} navmesh obstacles",
                        state.time, where.x, where.y, where.z, apart, depth, Obstacles());
                }
            }
        }
        if (state.time >= kRunSeconds) {
            Finish();
        }
    }
}
