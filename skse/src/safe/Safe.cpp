#include "safe/Safe.h"

#include "damage.h"

namespace PerkeleRim::Safe
{
    namespace
    {
        struct State
        {
            std::uint32_t   vehicle = 0;
            Damage::SeatGod god;
            int           own = 0;             // OwnDamage scopes alive
            std::uint32_t dropped = 0;         // hits and events dropped since the last log line
        } state;

        bool Safe() { return state.vehicle != 0 && Damage::SafeSeat(state.vehicle) && state.own == 0; }

        // Actor::HandleHealthDamage (vfunc 0x104): the entry of weapon, bite and arrow hits.
        struct DamageHook
        {
            static void thunk(RE::Actor* a_actor, RE::Actor* a_attacker, float a_damage)
            {
                if (a_attacker && a_actor && a_actor->IsPlayerRef() && Safe() && Damage::Incoming(state.vehicle, a_damage) <= 0.0f) {
                    state.dropped++;
                    return;
                }
                original(a_actor, a_attacker, a_damage);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // IAnimationGraphManagerHolder::NotifyAnimationGraph (vfunc 1 of the holder's vtable): Skyrim
        // sends staggerStart, recoilStart, knockdown and ragdoll events through it.
        struct NotifyHook
        {
            static bool thunk(RE::IAnimationGraphManagerHolder* a_holder, const RE::BSFixedString& a_event)
            {
                if (Safe() && Damage::PullsOutOfSeat(a_event.c_str())) {
                    state.dropped++;
                    return false;
                }
                return original(a_holder, a_event);
            }
            static inline REL::Relocation<decltype(thunk)> original;
        };

        // The game's god mode byte (what PlayerCharacter::IsGodMode reads), written directly:
        // PlayerCharacter::SetGodMode(false) left it on after the first time out of a seat.
        bool* GodByte()
        {
            static REL::Relocation<bool*> god{ RELOCATION_ID(517711, 404238) };
            return reinterpret_cast<bool*>(god.address());
        }
    }

    void Install()
    {
        REL::Relocation<std::uintptr_t> actor{ RE::VTABLE_PlayerCharacter[0] };
        DamageHook::original = actor.write_vfunc(0x104, DamageHook::thunk);
        REL::Relocation<std::uintptr_t> holder{ RE::VTABLE_PlayerCharacter[3] };
        NotifyHook::original = holder.write_vfunc(0x1, NotifyHook::thunk);
    }

    void Set(RE::PlayerCharacter* a_player, std::uint32_t a_vehicle)
    {
        if (!a_player) {
            return;
        }
        const bool was = state.vehicle != 0 && Damage::SafeSeat(state.vehicle);
        const bool now = Damage::SafeSeat(a_vehicle);
        state.vehicle = a_vehicle;
        switch (state.god.Step(a_vehicle, *GodByte())) {
        case Damage::SeatGod::Action::Enable:
            *GodByte() = true;
            break;
        case Damage::SeatGod::Action::Disable:
            *GodByte() = false;
            break;
        default:
            break;
        }
        if (now && !was) {
            SKSE::log::info("safe: seated in vehicle {}, enemies can't hurt the player (god mode {})", a_vehicle, *GodByte() ? "on" : "off");
        } else if (!now && was) {
            SKSE::log::info("safe: out of the safe seat, {} hits and events were dropped, god mode {}", state.dropped, *GodByte() ? "still on" : "off");
            state.dropped = 0;
        }
    }

    std::uint32_t Seat()
    {
        return Safe() ? state.vehicle : 0;
    }

    OwnDamage::OwnDamage()
    {
        state.own++;
        if (state.god.Ours() && *GodByte()) {
            *GodByte() = false;
            _lifted = true;
        }
    }

    OwnDamage::~OwnDamage()
    {
        state.own--;
        if (_lifted && state.god.Ours()) {
            *GodByte() = true;
        }
    }
}
