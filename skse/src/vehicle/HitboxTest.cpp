#include "vehicle/HitboxTest.h"

#include "coords.h"
#include "vehicle/Hitbox.h"

namespace PerkeleRim::HitboxTest
{
    namespace
    {
        constexpr auto        kFlagFile = "PerkeleRim_hitboxtest.txt";
        constexpr RE::FormID  kArcher = 0x000F62D8;  // Skyrim.esm LvlBanditMissileAggro1536
        constexpr RE::FormID  kMelee = 0x000B6FCD;   // Skyrim.esm LvlBanditMelee1HAggro1024
        constexpr float       kArcherBehind = 1400.0f;  // units behind the van's center, away from Kumpo
        constexpr float       kMeleeBehind = 400.0f;
        constexpr float       kDropHeight = 150.0f;     // placed this far above the van's origin, to fall onto the ground
        constexpr float       kVanReach = 2500.0f;      // the van must be this close to Kumpo
        constexpr float       kWaitSeconds = 120.0f;    // for the van to be solid near Kumpo
        constexpr float       kStandSeconds = 3.0f;     // ...and to have stood there this long (a called van lands first)
        constexpr float       kRunSeconds = 60.0f;
        constexpr float       kSampleSeconds = 0.5f;

        struct State
        {
            bool                 checked = false;
            bool                 done = false;
            bool                 started = false;
            float                waited = 0.0f;
            float                stood = 0.0f;  // the van has been solid near Kumpo this long
            float                time = 0.0f;
            float                sample = 0.0f;
            RE::ObjectRefHandle  archer;
            RE::ObjectRefHandle  melee;
            float                closest = 1e9f;  // the melee bandit's least distance to Kumpo
            float                deepest = 0.0f;  // the deepest he ever stood inside the van's box
            int                  inside = 0;      // samples he stood inside it
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
            SKSE::log::info("hitbox test: armed, the van at ({:.0f},{:.0f},{:.0f}) {:.0f} units from Kumpo at ({:.0f},{:.0f},{:.0f})", at.x, at.y, at.z, distance,
                kumpo.x, kumpo.y, kumpo.z);
            state.archer = Place(a_player, kArcher, at + away * kArcherBehind + RE::NiPoint3(0.0f, 0.0f, kDropHeight), "archer");
            state.melee = Place(a_player, kMelee, at + away * kMeleeBehind + RE::NiPoint3(0.0f, 0.0f, kDropHeight), "melee bandit");
            return true;
        }

        void Finish()
        {
            state.done = true;
            Remove(state.archer);
            Remove(state.melee);
            SKSE::log::info("hitbox test: done: {} projectile(s) hit the van; the melee bandit came within {:.0f} units of Kumpo and stood inside the van's box in {} of {} samples, at most {:.0f} units deep",
                Hitbox::Hits(Protocol::kVehicleHayosiko), state.closest, state.inside, static_cast<int>(kRunSeconds / kSampleSeconds), state.deepest);
        }
    }

    void Update(RE::PlayerCharacter* a_player, float a_delta)
    {
        if (state.done || !a_player) {
            return;
        }
        if (!state.checked) {
            state.checked = true;
            state.done = !std::filesystem::exists(kFlagFile);
            if (state.done) {
                return;
            }
            SKSE::log::info("hitbox test: {} found, waiting for the van", kFlagFile);
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
        auto* values = a_player->AsActorValueOwner();
        values->RestoreActorValue(RE::ActorValue::kHealth, 10000.0f);

        state.time += a_delta;
        state.sample += a_delta;
        if (state.sample >= kSampleSeconds) {
            state.sample = 0.0f;
            Protocol::VehicleState van{};
            Hitbox::Box            box{};
            auto                   melee = state.melee.get();
            if (melee && Hitbox::Where(Protocol::kVehicleHayosiko, van, box)) {
                const auto  at = melee->GetPosition();
                const float depth = Depth(van, box, at);
                const float apart = at.GetDistance(a_player->GetPosition());
                state.closest = (std::min)(state.closest, apart);
                state.deepest = (std::max)(state.deepest, depth);
                state.inside += depth > 0.0f ? 1 : 0;
                SKSE::log::info("hitbox test: {:.1f} s: melee bandit at ({:.0f},{:.0f},{:.0f}), {:.0f} from Kumpo, {:.0f} inside the van's box; {} hit(s) on the van",
                    state.time, at.x, at.y, at.z, apart, depth, Hitbox::Hits(Protocol::kVehicleHayosiko));
            }
        }
        if (state.time >= kRunSeconds) {
            Finish();
        }
    }
}
