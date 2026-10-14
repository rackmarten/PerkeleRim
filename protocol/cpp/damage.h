// Damage rules while MSC has the player: what an MSC death costs Skyrim's health, Skyrim's own fall
// damage, which doesn't run while MSC places the player, what MSC's attacks cost Skyrim actors, and
// what MSC's drinks, cigarettes and food heal. Header-only and free of Skyrim types, so the protocol
// tests cover it.

#pragma once

#include <algorithm>
#include <cmath>
#include <cstdint>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Damage
{
    // Share of Skyrim's max health one MSC death (PlayerDamage severity 1) costs. MSC kills on bumps
    // a Skyrim road is full of, so a whole death would make driving impossible.
    inline constexpr float kCrashShare = 0.5f;

    inline float Crash(float a_maxHealth, float a_severity)
    {
        return a_maxHealth * kCrashShare * (std::max)(0.0f, a_severity);
    }

    // What the player's MSC attacks cost Skyrim actors: fun over RPG, so a share of the target's max
    // health (no skills or perks), the same at any level. Dragons take a fraction of it.
    inline constexpr float kPunchShare = 0.25f;  // a bandit goes down in four punches
    inline constexpr float kDragonFactor = 0.25f;
    // Drinking pays off in a fight (MSC's drunk camera wobble makes aiming hard): each MSC drunk level
    // (0..8) adds this much, up to kDrunkPunchMax times the sober punch.
    inline constexpr float kDrunkPunchPerLevel = 0.25f;
    inline constexpr float kDrunkPunchMax = 2.0f;  // drunk 4: two punches drop a bandit

    inline float DrunkPunch(float a_drunk)
    {
        return (std::min)(kDrunkPunchMax, 1.0f + kDrunkPunchPerLevel * (std::max)(0.0f, a_drunk));
    }

    inline float Punch(float a_targetMaxHealth, bool a_dragon, float a_drunk = 0.0f)
    {
        return (std::max)(0.0f, a_targetMaxHealth) * kPunchShare * (a_dragon ? kDragonFactor : 1.0f) * DrunkPunch(a_drunk);
    }

    // A thrown item hits by its speed: nothing at kThrownSpeedMin, a sober punch from kThrownSpeedFull
    // on. MSC's own throw lands in between; a bottle, thrown twice as fast (msc/src/Link/Throwing.cs),
    // hits like a punch from range.
    inline constexpr float kThrownSpeedMin = 2.0f;    // meters per second
    inline constexpr float kThrownSpeedFull = 15.0f;

    inline float Thrown(float a_targetMaxHealth, bool a_dragon, float a_speed)
    {
        const float scale = (std::clamp)((a_speed - kThrownSpeedMin) / (kThrownSpeedFull - kThrownSpeedMin), 0.0f, 1.0f);
        return Punch(a_targetMaxHealth, a_dragon) * scale;
    }

    // Piss is poison: while the stream stays on an actor it gets a damage over time, a share of its
    // max health per second for kPissSeconds after the last hit. Dragons take a fraction of it.
    inline constexpr float kPissSharePerSecond = 0.02f;
    inline constexpr float kPissSeconds = 5.0f;

    inline float PissPerSecond(float a_targetMaxHealth, bool a_dragon)
    {
        return (std::max)(0.0f, a_targetMaxHealth) * kPissSharePerSecond * (a_dragon ? kDragonFactor : 1.0f);
    }

    // What one Consumed heals, as a share of the player's max health: fun over RPG, like the damage.
    inline float HealShare(std::uint32_t a_kind)
    {
        switch (a_kind) {
        case Protocol::kConsumedBeer:
            return 0.10f;
        case Protocol::kConsumedShot:
            return 0.10f;
        case Protocol::kConsumedKilju:
            return 0.15f;
        case Protocol::kConsumedBooze:
            return 0.20f;
        case Protocol::kConsumedSpirit:
            return 0.25f;
        case Protocol::kConsumedCigarette:
            return 0.05f;
        case Protocol::kConsumedFood:
            return 0.25f;
        default:
            return 0.0f;
        }
    }

    // Health one Consumed restores, never past the max.
    inline float Heal(std::uint32_t a_kind, float a_maxHealth, float a_health)
    {
        const float max = (std::max)(0.0f, a_maxHealth);
        return (std::max)(0.0f, (std::min)(max * HealShare(a_kind), max - a_health));
    }

    // Skyrim's player fall damage: ((height - fJumpFallHeightMin) * fJumpFallHeightMult) ^
    // fJumpFallHeightExponent, nothing below the minimum. Game units.
    struct FallSettings
    {
        float min = 600.0f;
        float mult = 0.1f;
        float exponent = 1.45f;
    };

    inline float Fall(float a_height, const FallSettings& a_settings = {})
    {
        if (a_height <= a_settings.min) {
            return 0.0f;
        }
        return std::pow((a_height - a_settings.min) * a_settings.mult, a_settings.exponent);
    }

    // Follows MSC's player off the ground and back: the height fallen is from the highest point
    // while airborne to where it lands. Reset whenever the pose can't be trusted for a fall (a new
    // anchor, seated, no pose).
    class FallTracker
    {
    public:
        // One pose: its feet height and whether MSC says it stands on something. Returns the
        // height fallen when this pose lands, else 0.
        float Update(float a_z, bool a_grounded)
        {
            if (!_known) {
                _known = true;
                _airborne = !a_grounded;
                _top = _last = a_z;
                return 0.0f;
            }
            if (!a_grounded) {
                // Leaving the ground starts from where the player last stood.
                _top = (std::max)(_airborne ? _top : _last, a_z);
                _airborne = true;
                _last = a_z;
                return 0.0f;
            }
            const float fallen = _airborne ? _top - a_z : 0.0f;
            _airborne = false;
            _last = a_z;
            return (std::max)(0.0f, fallen);
        }

        void Reset() { _known = false; }

    private:
        bool  _known = false;
        bool  _airborne = false;
        float _top = 0.0f;
        float _last = 0.0f;
    };
}
