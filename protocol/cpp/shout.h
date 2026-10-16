// Noticing the player's shouts while MSC has it. Skyrim's shout handler runs as usual (Z), so the
// shout's own recovery time is the signal: it only ever counts down, and jumps up when a shout is
// released. Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

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
