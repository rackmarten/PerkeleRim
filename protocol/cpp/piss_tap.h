// Alt pisses on a clean tap while MSC has the player. Sent to MSC as it was pressed, Alt started
// the stream on every Alt+Tab, so Skyrim decides when it is let go: held no longer than kMaxTap,
// with no other key or button pressed meanwhile and Skyrim's window still in front, MSC gets Alt
// for kPulse seconds (its KeyRemap makes that MSC's Urinate key). The stream then runs until the
// bladder is empty, as in MSC; nothing stops it. Holding Alt does nothing. Header-only and free of
// Skyrim types, so the protocol tests cover it.

#pragma once

namespace PerkeleRim::PissTap
{
    inline constexpr float kMaxTap = 0.35f;  // seconds Alt may be held for a tap
    inline constexpr float kPulse = 0.25f;   // seconds MSC sees Alt held after a tap

    class Tap
    {
    public:
        void Press()
        {
            down_ = true;
            spoiled_ = false;
        }

        // Another key or button went down: Alt is part of a combination (Alt+Tab), not a tap.
        void OtherKey()
        {
            if (down_) {
                spoiled_ = true;
            }
        }

        // a_held: seconds Alt was held. a_focused: Skyrim's window is the foreground one. True when
        // this was a tap and the pulse starts.
        bool Release(float a_held, bool a_focused)
        {
            const bool tap = down_ && !spoiled_ && a_focused && a_held <= kMaxTap;
            down_ = false;
            if (tap) {
                pulse_ = kPulse;
            }
            return tap;
        }

        // Once a frame: true while MSC should see Alt held.
        bool Tick(float a_delta)
        {
            if (pulse_ <= 0.0f) {
                return false;
            }
            pulse_ -= a_delta;
            return true;
        }

        void Reset() { *this = Tap{}; }

    private:
        bool  down_ = false;
        bool  spoiled_ = false;
        float pulse_ = 0.0f;
    };
}
