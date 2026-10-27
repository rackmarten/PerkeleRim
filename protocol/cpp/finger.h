// MSC's middle finger (V) at the law: a guard or a Jarl the player shows it to, close by and in front,
// puts a small bounty on the player in that one's hold, once per person per minute. Header-only and
// free of Skyrim types, so the protocol tests cover it; Skyrim's half is Combat::OnMiddleFinger.

#pragma once

#include <cmath>
#include <cstdint>
#include <iterator>
#include <unordered_map>

namespace PerkeleRim::Finger
{
    inline constexpr std::int32_t kBounty = 5;            // gold, non-violent
    inline constexpr float        kRange = 350.0f;        // units, about 5 m
    inline constexpr float        kConeCos = 0.7071068f;  // within 45 degrees of where the finger points
    inline constexpr double       kPerPerson = 60.0;      // seconds before the same person minds again

    // Whether someone a_dx, a_dy units away (Skyrim space, from the player) sees the finger pointed
    // along a_dirX, a_dirY (the camera's forward, flattened; it needn't be a unit vector): within
    // kRange and kConeCos. Height is left out: a guard on the steps above still counts.
    inline bool InView(float a_dx, float a_dy, float a_dirX, float a_dirY)
    {
        const float distance = std::sqrt(a_dx * a_dx + a_dy * a_dy);
        const float way = std::sqrt(a_dirX * a_dirX + a_dirY * a_dirY);
        if (distance > kRange || way <= 0.0f) {
            return false;
        }
        if (distance < 1.0f) {
            return true;  // standing in each other
        }
        return (a_dx * a_dirX + a_dy * a_dirY) / (distance * way) >= kConeCos;
    }

    // Who minded the finger lately, by form id.
    class Bounties
    {
    public:
        // True when a_person (a form id) adds a bounty at a_now seconds: not within kPerPerson of
        // the last one it added.
        bool Take(std::uint32_t a_person, double a_now)
        {
            for (auto it = last_.begin(); it != last_.end();) {
                it = a_now - it->second >= kPerPerson || a_now < it->second ? last_.erase(it) : std::next(it);
            }
            if (last_.contains(a_person)) {
                return false;
            }
            last_[a_person] = a_now;
            return true;
        }

    private:
        std::unordered_map<std::uint32_t, double> last_;
    };
}
