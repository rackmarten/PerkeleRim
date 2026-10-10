// Damage rules for the player while MSC has it: what an MSC death costs Skyrim's health, and
// Skyrim's own fall damage, which doesn't run while MSC places the player. Header-only and free of
// Skyrim types, so the protocol tests cover it.

#pragma once

#include <algorithm>
#include <cmath>

namespace PerkeleRim::Damage
{
    // Share of Skyrim's max health one MSC death (PlayerDamage severity 1) costs. MSC kills on bumps
    // a Skyrim road is full of, so a whole death would make driving impossible.
    inline constexpr float kCrashShare = 0.5f;

    inline float Crash(float a_maxHealth, float a_severity)
    {
        return a_maxHealth * kCrashShare * (std::max)(0.0f, a_severity);
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
