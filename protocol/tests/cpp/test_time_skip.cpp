#include <doctest/doctest.h>

#include "../../cpp/time_skip.h"

using namespace PerkeleRim::Rest;

namespace
{
    // Runs normal frames (60 per second, timescale 20) from a_hours for a_seconds; returns the end hours.
    double Play(SkipTracker& a_tracker, double a_hours, double a_seconds, bool a_bed, double& a_result, bool& a_slept)
    {
        constexpr double dt = 1.0 / 60.0;
        for (double t = 0.0; t < a_seconds; t += dt) {
            a_hours += dt * 20.0 / 3600.0;
            if (const double h = a_tracker.Frame(a_hours, dt, 20.0, a_bed, a_slept); h > 0.0) {
                a_result = h;
            }
        }
        return a_hours;
    }

    // A skip: the clock runs 2 game hours a second for a_hours.
    double Skip(SkipTracker& a_tracker, double a_hours, double a_skip, bool a_bed, double& a_result, bool& a_slept)
    {
        constexpr double dt = 1.0 / 60.0;
        const double     end = a_hours + a_skip;
        while (a_hours < end) {
            a_hours = (a_hours + 2.0 * dt > end) ? end : a_hours + 2.0 * dt;
            if (const double h = a_tracker.Frame(a_hours, dt, 20.0, a_bed, a_slept); h > 0.0) {
                a_result = h;
            }
        }
        return a_hours;
    }
}

TEST_CASE("a wait after the menu closed is counted once it settles")
{
    SkipTracker tracker;
    double      result = 0.0;
    bool        slept = false;
    tracker.Open(1000.0, false);
    tracker.Close(1000.0);
    double hours = Play(tracker, 1000.0, 1.5, false, result, slept);  // the fade
    CHECK(result == 0.0);
    hours = Skip(tracker, hours, 8.0, false, result, slept);
    CHECK(result == 0.0);  // not yet settled
    Play(tracker, hours, 2.0, false, result, slept);
    CHECK(result == doctest::Approx(8.0).epsilon(0.01));
    CHECK_FALSE(slept);
    CHECK_FALSE(tracker.Armed());
}

TEST_CASE("sleeping in a bed says so")
{
    SkipTracker tracker;
    double      result = 0.0;
    bool        slept = false;
    tracker.Open(50.0, true);
    tracker.Close(50.0);
    const double hours = Skip(tracker, 50.0, 6.0, true, result, slept);
    Play(tracker, hours, 2.0, false, result, slept);
    CHECK(result == doctest::Approx(6.0).epsilon(0.01));
    CHECK(slept);
}

TEST_CASE("a bed seen only during the skip still counts")
{
    SkipTracker tracker;
    double      result = 0.0;
    bool        slept = false;
    tracker.Open(50.0, false);
    tracker.Close(50.0);
    const double hours = Skip(tracker, 50.0, 4.0, true, result, slept);
    Play(tracker, hours, 2.0, false, result, slept);
    CHECK(result == doctest::Approx(4.0).epsilon(0.01));
    CHECK(slept);
}

TEST_CASE("hours already added when the menu closes are counted")
{
    SkipTracker tracker;
    double      result = 0.0;
    bool        slept = false;
    tracker.Open(100.0, false);
    tracker.Close(103.0);
    Play(tracker, 103.0, 2.0, false, result, slept);
    CHECK(result == doctest::Approx(3.0).epsilon(0.01));
}

TEST_CASE("a menu closed without waiting is nothing")
{
    SkipTracker tracker;
    double      result = 0.0;
    bool        slept = false;
    tracker.Open(100.0, false);
    tracker.Close(100.0);
    Play(tracker, 100.0, 8.0, false, result, slept);
    CHECK(result == 0.0);
    CHECK_FALSE(tracker.Armed());
}

TEST_CASE("play without a menu is never a skip")
{
    SkipTracker tracker;
    double      result = 0.0;
    bool        slept = false;
    Play(tracker, 100.0, 10.0, false, result, slept);
    Skip(tracker, 100.0, 5.0, false, result, slept);
    CHECK(result == 0.0);
}

TEST_CASE("a load drops a skip being tracked")
{
    SkipTracker tracker;
    double      result = 0.0;
    bool        slept = false;
    tracker.Open(100.0, false);
    tracker.Close(100.0);
    tracker.Reset();
    Skip(tracker, 100.0, 5.0, false, result, slept);
    Play(tracker, 105.0, 3.0, false, result, slept);
    CHECK(result == 0.0);
}
