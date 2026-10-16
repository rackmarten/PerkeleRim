// When the player's health first drops low while MSC has it (PlayerHurt kHurtLow, MSC swears).
// Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

namespace PerkeleRim::Hurt
{
    inline constexpr float kLow = 0.25f;    // share of max health that counts as low
    inline constexpr float kRearm = 0.5f;   // healed back above this, the next drop counts again

    class LowHealth
    {
    public:
        // This frame's health as a share of max health. True on the frame it first drops low.
        bool Step(float a_share)
        {
            if (!started_) {
                started_ = true;
                armed_ = a_share >= kLow;  // already low at the handover: no swear for that
                return false;
            }
            if (armed_ && a_share < kLow) {
                armed_ = false;
                return true;
            }
            if (!armed_ && a_share > kRearm) {
                armed_ = true;
            }
            return false;
        }

        // At each handover: the next Step only takes the current health.
        void Reset() { started_ = false; }

    private:
        bool started_ = false;
        bool armed_ = false;
    };
}
