// What MSC's needs (PlayerState.needs) do to Kumpo in Skyrim while MSC has the player. MSC's own
// HUD turns a need red at 100 and kills at 200 (docs/FSM_MAP.md, "Needs and stats"); PerkeleRim's
// bars run to 200 with a mark at 100. Each effect below starts at the mark and is full at 200:
// starving or dehydrated drains health (from 150, and can kill), fatigue slows stamina regen,
// stress hits a little harder, dirt makes traders dearer. A full bladder slows Kumpo's walk, which
// is MSC's, so that one is msc/src/Link/Needs.cs. Also Kumpo's shake: his hands tremble the longer
// he has been dry, and a beer or two steadies them. Header-only and free of Skyrim types, so the
// protocol tests cover it.

#pragma once

#include <algorithm>
#include <cmath>
#include <limits>

namespace PerkeleRim::Needs
{
    inline constexpr float kMark = 100.0f;  // MSC's red line, the bars' mark
    inline constexpr float kFull = 200.0f;  // MSC's death line, the bars' end

    // How far a need is from a_from to kFull, 0..1.
    inline float Share(float a_need, float a_from = kMark)
    {
        return (std::clamp)((a_need - a_from) / (kFull - a_from), 0.0f, 1.0f);
    }

    // Starving or dehydrated: health drains from kStarveFrom, up to kStarveRate of max health a second
    // at 200, whichever of hunger and thirst is worse. Skyrim's own regen (0.7% a second) wins below
    // about 170; at 200 it loses by about 1.3% a second, about 80 s from full health to death.
    // MSC's own need death at 200 is kept from resetting the need (msc/src/Game/DeathGuard.cs), so
    // the drain goes on until Kumpo eats or drinks.
    inline constexpr float kStarveFrom = 150.0f;
    inline constexpr float kStarveRate = 0.02f;

    // Health a second, a_maxHealth Skyrim's max health.
    inline float StarveDrain(float a_hunger, float a_thirst, float a_maxHealth)
    {
        return kStarveRate * (std::max)(0.0f, a_maxHealth) * Share((std::max)(a_hunger, a_thirst), kStarveFrom);
    }

    // Tired: Skyrim's stamina regen (StaminaRateMult, percent) down by up to kFatigueRegenCut.
    inline constexpr float kFatigueRegenCut = 75.0f;

    inline float FatigueRegenCut(float a_fatigue)
    {
        return kFatigueRegenCut * Share(a_fatigue);
    }

    // Stressed: a punch hits up to kStressPunchMax harder (on top of drunk, Damage::Punch). MSC has
    // him swear above 100 too.
    inline constexpr float kStressPunchMax = 0.2f;

    inline float StressPunch(float a_stress)
    {
        return 1.0f + kStressPunchMax * Share(a_stress);
    }

    // Dirty: traders take up to kDirtPriceMax more. Skyrim's barter factor (fBarterMin, fBarterMax)
    // scales by this, so buying costs that much more and selling pays that much less.
    inline constexpr float kDirtPriceMax = 0.3f;

    inline float DirtPrice(float a_dirtiness)
    {
        return 1.0f + kDirtPriceMax * Share(a_dirtiness);
    }

    // Recast or rewrite an effect once its value moves this far from what was put on.
    inline constexpr float kRegenStep = 2.0f;    // percent
    inline constexpr float kPriceStep = 0.01f;

    // Kumpo's shake (#931). His hands are steady for kShakeGrace Skyrim hours after he was last
    // drunk (MSC's drunk level at kSteadyDrunk or more, a beer or two), then shake more and more
    // until kShakeFull hours dry. Being a little drunk steadies them in proportion. A game without a
    // record of his last drink (a new game, a save from before the shake) counts from its first
    // frame, as if he'd just had one: steady, shaky only after kShakeGrace.
    inline constexpr float kSteadyDrunk = 0.05f;  // one beer is about 0.08 for MSC's 79 kg Kumpo
    inline constexpr float kShakeGrace = 1.0f;    // Skyrim hours
    inline constexpr float kShakeFull = 24.0f;
    inline constexpr float kNever = -std::numeric_limits<float>::infinity();

    // 0..1, how hard his hands shake. a_dryHours: Skyrim hours since he was last drunk.
    inline float Shake(float a_dryHours, float a_drunk)
    {
        const float dry = (std::clamp)((a_dryHours - kShakeGrace) / (kShakeFull - kShakeGrace), 0.0f, 1.0f);
        const float steady = (std::clamp)(a_drunk / kSteadyDrunk, 0.0f, 1.0f);
        return dry * (1.0f - steady);
    }

    // When he was last drunk, in Skyrim's hours passed (Calendar::GetHoursPassed). The cosave keeps it.
    class DryClock
    {
    public:
        void Step(float a_hoursNow, float a_drunk)
        {
            if (a_drunk >= kSteadyDrunk || lastDrunk_ == kNever) {
                lastDrunk_ = a_hoursNow;
            }
        }

        // Hours dry, none before the first Step without a record.
        float DryHours(float a_hoursNow) const
        {
            return lastDrunk_ == kNever ? 0.0f : (std::max)(0.0f, a_hoursNow - lastDrunk_);
        }

        float LastDrunk() const { return lastDrunk_; }
        void  SetLastDrunk(float a_hours) { lastDrunk_ = a_hours; }
        void  Reset() { lastDrunk_ = kNever; }

    private:
        float lastDrunk_ = kNever;
    };

    // The hand layer's offset in pixels at a_seconds, for a shake of a_shake (0..1) on a screen
    // a_height pixels tall: up to kShakeMax of the height, two waves an axis at a tremor's 5 to 12 Hz
    // so it never repeats visibly.
    inline constexpr float kShakeMax = 0.006f;  // about 6 px at 1080p

    struct Offset
    {
        float x = 0.0f;
        float y = 0.0f;
    };

    inline Offset ShakeOffset(float a_seconds, float a_shake, float a_height)
    {
        constexpr float kTau = 6.2831853f;
        const float     amplitude = kShakeMax * (std::max)(0.0f, a_height) * (std::clamp)(a_shake, 0.0f, 1.0f);
        const float     t = a_seconds * kTau;
        return { amplitude * (0.6f * std::sin(t * 7.3f) + 0.4f * std::sin(t * 11.1f + 1.3f)),
            amplitude * (0.6f * std::sin(t * 8.7f + 0.5f) + 0.4f * std::sin(t * 5.9f + 2.1f)) };
    }
}
