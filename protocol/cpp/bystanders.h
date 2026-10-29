// Skyrim's people around Kumpo's vehicles: nobody starts a dialogue with him while he sits in one,
// and civilians run from a vehicle driving at them (Damage::InVehicleWay) or honking at them from
// further than its dive reaches (Damage::HornReactionOf). Header-only and free of Skyrim types, so
// the protocol tests cover it; Skyrim's half is skse/src/people/People.cpp.

#pragma once

#include <cstddef>
#include <cstdint>
#include <unordered_map>
#include <utility>
#include <vector>

namespace PerkeleRim::Bystanders
{
    // While MSC has the player, nobody starts a dialogue with Kumpo (neither he with E nor someone
    // walking up to him or force-greeting him) while he sits in a vehicle or has climbed into one: the
    // dialogue menu takes the controls and the vehicle runs on without him. On foot he may talk at
    // any speed (he runs fast).
    constexpr bool MayTalk(bool a_inVehicle)
    {
        return !a_inVehicle;
    }

    struct Person
    {
        bool npc = true;        // a person (ActorTypeNPC), not a creature or a dragon
        bool dead = false;
        bool follower = false;  // the player's teammate, or commanded (a summon, a thrall)
        bool guard = false;     // Skyrim's IsGuard: in a hold's guard faction
        bool hostile = false;   // hostile to the player
        bool fighting = false;  // in combat already, with anyone
        bool huge = false;      // Damage::Bulk::kHuge
    };

    // Civilians run from the vehicle. Guards stand their ground, followers, enemies and anyone already
    // fighting carry on as Skyrim has them, creatures are left to their own AI.
    constexpr bool Flees(const Person& a_person)
    {
        return a_person.npc && !a_person.dead && !a_person.follower && !a_person.guard && !a_person.hostile && !a_person.fighting &&
               !a_person.huge;
    }

    inline constexpr float  kFleeUnits = 1000.0f;  // a flee runs this far from the vehicle's line (about 14 m)
    inline constexpr double kFleeLasts = 6.0;      // a flee is let go this long after the vehicle last came at them
    inline constexpr double kFleeRenew = 3.0;      // and started over at most this often while it keeps coming, from where it is now

    // Who flees and until when, by form id. Start says whether Skyrim has to start (or restart) the
    // flee now; Expired hands back the ones to let go.
    class Fleeing
    {
    public:
        bool Start(std::uint32_t a_id, double a_now)
        {
            auto [it, added] = _entries.try_emplace(a_id, Entry{ a_now + kFleeLasts, a_now });
            if (added) {
                return true;
            }
            it->second.until = a_now + kFleeLasts;
            if (a_now - it->second.started < kFleeRenew) {
                return false;
            }
            it->second.started = a_now;
            return true;
        }

        bool Has(std::uint32_t a_id) const { return _entries.contains(a_id); }
        std::size_t Count() const { return _entries.size(); }

        // The ones whose time ran out, forgotten.
        std::vector<std::uint32_t> Expired(double a_now)
        {
            std::vector<std::uint32_t> out;
            for (auto it = _entries.begin(); it != _entries.end();) {
                if (a_now >= it->second.until) {
                    out.push_back(it->first);
                    it = _entries.erase(it);
                } else {
                    ++it;
                }
            }
            return out;
        }

        // Everyone, forgotten (MSC lets go of the player).
        std::vector<std::uint32_t> All()
        {
            std::vector<std::uint32_t> out;
            for (const auto& [id, entry] : _entries) {
                out.push_back(id);
            }
            _entries.clear();
            return out;
        }

    private:
        struct Entry
        {
            double until = 0.0;    // let go after this
            double started = 0.0;  // last time Skyrim started it
        };
        std::unordered_map<std::uint32_t, Entry> _entries;
    };
}
