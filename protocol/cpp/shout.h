// Noticing the player's shouts while MSC has it. Skyrim's shout handler runs as usual (Z), so the
// shout's own recovery time is the signal: it only ever counts down, and jumps up when a shout is
// released. Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <cmath>
#include <cstdint>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Shout
{
    inline constexpr std::uint32_t kUnrelentingForce = 0x13E07;  // Skyrim.esm's shout
    // A rise smaller than this is noise, not a shout (the shortest vanilla recovery is 5 s).
    inline constexpr float kMinJump = 0.5f;

    inline std::uint32_t KindOf(std::uint32_t a_shout)
    {
        return a_shout == kUnrelentingForce ? Protocol::kShoutUnrelentingForce : Protocol::kShoutOther;
    }

    // How many words a released shout had (1..3). Skyrim says which while it charges (the high
    // process's variation, 0..2), but a quick tap can be released before a frame sees it: then the
    // word count whose cooldown (a_cooldowns, the shout's three recovery times, times a_mult, the
    // player's ShoutRecoveryMult) is nearest the cooldown that started (a_recovery). 0 without either.
    inline std::uint32_t Words(int a_variation, const float (&a_cooldowns)[3], float a_mult, float a_recovery)
    {
        if (a_variation >= 0 && a_variation < 3) {
            return static_cast<std::uint32_t>(a_variation) + 1;
        }
        std::uint32_t words = 0;
        float         nearest = 0.0f;
        for (std::uint32_t i = 0; i < 3; i++) {
            const float off = std::fabs(a_cooldowns[i] * a_mult - a_recovery);
            if (a_cooldowns[i] > 0.0f && (words == 0 || off < nearest)) {
                words = i + 1;
                nearest = off;
            }
        }
        return words;
    }

    class Watch
    {
    public:
        // This frame's recovery time in seconds. True when a shout was released since the last call.
        bool Step(float a_recovery)
        {
            const bool shouted = started_ && a_recovery > last_ + kMinJump;
            last_ = a_recovery;
            started_ = true;
            return shouted;
        }

        // The first Step after this only takes the current value: a cooldown already running when
        // MSC takes the player isn't a new shout.
        void Reset() { started_ = false; }

    private:
        float last_ = 0.0f;
        bool  started_ = false;
    };
}
