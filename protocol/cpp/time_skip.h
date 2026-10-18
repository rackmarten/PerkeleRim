// Tells how many game hours the player waited or slept in Skyrim, from the calendar frame by frame.
// Header-only and free of Skyrim types, so the protocol tests cover it; skse/src/rest feeds it.
//
// Skyrim's Wait and Sleep menu closes first and the hours pass afterwards, in a time lapse that runs
// the clock far faster than play does (or, on some paths, the hours are already added when the menu
// closes). So the tracker is armed when the menu opens, remembers the calendar there, and once the
// menu has closed watches for frames whose clock moved more than the game's timescale explains.
// When those stop for a moment, everything since the menu opened is the skip.

#pragma once

namespace PerkeleRim::Rest
{
    // A frame's clock move beyond what the timescale explains that counts as skipping, game hours.
    inline constexpr double kSkipHours = 0.002;
    // Seconds of frames with no skipping that end a skip that was seen.
    inline constexpr double kSettleSeconds = 1.0;
    // Seconds after the menu closed that a skip may take to start (a fade first), else it was cancelled.
    inline constexpr double kStartSeconds = 6.0;
    // Skips shorter than this don't count (a cancelled menu, calendar noise).
    inline constexpr double kMinHours = 0.05;

    class SkipTracker
    {
    public:
        // The Wait or Sleep menu opened. a_hours: the calendar's hours passed, a_bed: the player is
        // lying in a bed.
        void Open(double a_hours, bool a_bed)
        {
            armed = true;
            closed = false;
            sawSkip = false;
            bed = a_bed;
            start = last = a_hours;
            quiet = 0.0;
            waited = 0.0;
        }

        // The menu closed. The hours may already have been added.
        void Close(double a_hours)
        {
            if (!armed) {
                return;
            }
            closed = true;
            if (a_hours - start >= kMinHours) {
                sawSkip = true;
            }
            last = a_hours;
            quiet = 0.0;
            waited = 0.0;
        }

        void Reset()
        {
            armed = closed = sawSkip = bed = false;
        }

        bool Armed() const { return armed; }

        // Every unpaused frame. a_hours: the calendar's hours passed, a_dt: real seconds since the last
        // frame, a_timescale: game seconds per real second, a_bed: the player lies in a bed now.
        // Returns the hours skipped once the skip is over (and whether it was in a bed in a_bed), else 0.
        double Frame(double a_hours, double a_dt, double a_timescale, bool a_bed, bool& a_slept)
        {
            if (!armed) {
                return 0.0;
            }
            bed = bed || a_bed;
            const double expected = a_dt * a_timescale / 3600.0;
            const bool   skipping = a_hours - last - expected > kSkipHours;
            last = a_hours;
            if (!closed) {
                return 0.0;
            }
            if (skipping) {
                sawSkip = true;
                quiet = 0.0;
                return 0.0;
            }
            if (!sawSkip) {
                waited += a_dt;
                if (waited > kStartSeconds) {
                    armed = false;  // the menu was closed without waiting
                }
                return 0.0;
            }
            quiet += a_dt;
            if (quiet < kSettleSeconds) {
                return 0.0;
            }
            armed = false;
            const double hours = a_hours - start;
            a_slept = bed;
            return hours >= kMinHours ? hours : 0.0;
        }

    private:
        bool   armed = false;
        bool   closed = false;
        bool   sawSkip = false;
        bool   bed = false;
        double start = 0.0;
        double last = 0.0;
        double quiet = 0.0;
        double waited = 0.0;
    };
}
