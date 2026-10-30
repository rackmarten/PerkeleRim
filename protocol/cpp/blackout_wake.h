// A blackout while MSC has the player: Kumpo drank past MSC's pass-out line or was knocked out
// (Blackout, MSC -> Skyrim). Skyrim fades to black, moves him to a random map marker in Tamriel
// of a kind that stands on open, peaceful ground (a town, a farm, a mill, a stable, the docks; no
// caves, camps, ruins or lairs), at least kMinDistance from where he fell, and moves its clock on
// by the blackout's hours. Moving the player while MSC has it is a teleport, so PlayerSync sends a
// new anchor there and MSC puts him down once the ground has arrived. Once he stands there for
// kArrivedSeconds Skyrim fades back in and sends TimePassed with kTimeBed | kTimeBlackout, from
// which MSC takes its needs, its clock and its hangover. A fast-travel destination is never in
// the void: the game puts players on map markers itself.
// Header-only and free of Skyrim types, so the protocol tests cover it; skse/src/blackout/Blackout.cpp
// feeds it a Frame a frame and does each Step.

#pragma once

#include <cstddef>
#include <cstdint>
#include <span>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Blackout
{
    inline constexpr std::uint32_t kTamriel = 0x0000003C;  // Skyrim.esm: the Tamriel worldspace

    // Map marker types (RE::MARKER_TYPE, the MapMarkerData's type) a drunk can wake up at: open
    // ground people live and work on. Caves, camps, forts, ruins, lairs, giant camps, shrines up
    // mountains and the like are left out.
    inline constexpr std::uint16_t kCity = 1;
    inline constexpr std::uint16_t kTown = 2;
    inline constexpr std::uint16_t kSettlement = 3;
    inline constexpr std::uint16_t kFarm = 13;
    inline constexpr std::uint16_t kWoodMill = 14;
    inline constexpr std::uint16_t kWheatMill = 19;
    inline constexpr std::uint16_t kStable = 21;
    inline constexpr std::uint16_t kLighthouse = 27;
    inline constexpr std::uint16_t kDocks = 33;

    constexpr bool WakeType(std::uint16_t a_type)
    {
        switch (a_type) {
        case kCity:
        case kTown:
        case kSettlement:
        case kFarm:
        case kWoodMill:
        case kWheatMill:
        case kStable:
        case kLighthouse:
        case kDocks:
            return true;
        default:
            return false;
        }
    }

    // He wakes up at least this distant from where he fell (units, on the map; about 1.4 km), so it's
    // somewhere else, and a teleport for PlayerSync (more than 2048).
    inline constexpr float kMinDistance = 100000.0f;

    struct Marker
    {
        float         position[3];  // Skyrim world units
        std::uint16_t type;         // RE::MARKER_TYPE
    };

    // The marker he wakes up at: a_roll (0..1) picks one of the wake types far enough from a_from,
    // or of all wake types when none is that far. -1 when there's none at all (he stays where he is).
    constexpr std::ptrdiff_t Pick(std::span<const Marker> a_markers, const float (&a_from)[3], float a_roll)
    {
        std::size_t distant = 0;
        std::size_t all = 0;
        for (const auto& m : a_markers) {
            if (!WakeType(m.type)) {
                continue;
            }
            all++;
            const float dx = m.position[0] - a_from[0];
            const float dy = m.position[1] - a_from[1];
            if (dx * dx + dy * dy >= kMinDistance * kMinDistance) {
                distant++;
            }
        }
        if (all == 0) {
            return -1;
        }
        const bool        onlyFar = distant > 0;
        const std::size_t count = onlyFar ? distant : all;
        const float       roll = a_roll < 0.0f ? 0.0f : a_roll;
        std::size_t       nth = static_cast<std::size_t>(roll * static_cast<float>(count));
        if (nth >= count) {
            nth = count - 1;
        }
        for (std::size_t i = 0; i < a_markers.size(); i++) {
            const auto& m = a_markers[i];
            if (!WakeType(m.type)) {
                continue;
            }
            const float dx = m.position[0] - a_from[0];
            const float dy = m.position[1] - a_from[1];
            if (onlyFar && dx * dx + dy * dy < kMinDistance * kMinDistance) {
                continue;
            }
            if (nth-- == 0) {
                return static_cast<std::ptrdiff_t>(i);
            }
        }
        return -1;
    }

    // Game hours a blackout lasts, a_roll (0..1) between the kind's shortest and longest: a drunk
    // sleeps it off through the night, a knockout is out for an hour or two.
    inline constexpr float kDrunkHoursMin = 6.0f;
    inline constexpr float kDrunkHoursMax = 10.0f;
    inline constexpr float kKnockoutHoursMin = 1.0f;
    inline constexpr float kKnockoutHoursMax = 2.0f;

    constexpr float Hours(std::uint32_t a_kind, float a_roll)
    {
        const float roll = a_roll < 0.0f ? 0.0f : a_roll > 1.0f ? 1.0f : a_roll;
        return a_kind == Protocol::kBlackoutKnockout ? kKnockoutHoursMin + roll * (kKnockoutHoursMax - kKnockoutHoursMin) :
                                                       kDrunkHoursMin + roll * (kDrunkHoursMax - kDrunkHoursMin);
    }

    inline constexpr float kFadeOutSeconds = 2.0f;   // he goes down
    inline constexpr float kArrivedSeconds = 2.0f;   // standing at the marker this long before the fade in
    inline constexpr float kArriveTimeout = 60.0f;   // fades in anyway, so the screen never stays black
    inline constexpr float kFadeInSeconds = 3.0f;
    inline constexpr float kLandingSeconds = 10.0f;  // after the fade in the move still counts no fall

    struct Frame
    {
        bool  loading = false;  // Skyrim's loading screen is up
        bool  arrived = false;  // he stands at the marker: MSC put him down on its new island, or, with
                                // the link gone, Skyrim's player is there with its cell and 3D in
        float delta = 0.0f;     // seconds since the last frame
    };

    enum class Step
    {
        None,
        FadeOut,  // start the fade to black
        Move,     // black: pick the marker, move him there, move the clock on
        Wake,     // fade back in, TimePassed to MSC, his hangover line
    };

    enum class Phase
    {
        Off,
        Starting,   // MSC's Blackout came in: fade out on the next frame
        FadingOut,  // kFadeOutSeconds
        Moving,     // until he stands at the marker for kArrivedSeconds
        Landing,    // faded in: kLandingSeconds without fall damage
    };

    class Machine
    {
    public:
        // MSC's Blackout. Refused (false) while one is still going.
        bool Start(std::uint32_t a_kind)
        {
            if (Busy()) {
                return false;
            }
            *this = Machine{};
            _kind = a_kind;
            _phase = Phase::Starting;
            return true;
        }

        // A game loaded: whatever was going on is over.
        void Loaded() { *this = Machine{}; }

        Step Tick(const Frame& a_frame)
        {
            const float delta = a_frame.delta > 0.0f ? a_frame.delta : 0.0f;
            switch (_phase) {
            case Phase::Starting:
                _phase = Phase::FadingOut;
                _timer = kFadeOutSeconds;
                return Step::FadeOut;
            case Phase::FadingOut:
                if ((_timer -= delta) > 0.0f) {
                    return Step::None;
                }
                _phase = Phase::Moving;
                _timer = kArriveTimeout;
                _arrived = 0.0f;
                return Step::Move;
            case Phase::Moving:
                _timer -= delta;
                if (a_frame.arrived && !a_frame.loading) {
                    _arrived += delta;
                    if (_arrived >= kArrivedSeconds) {
                        return Wake();
                    }
                    return Step::None;
                }
                _arrived = 0.0f;
                if (_timer <= 0.0f) {
                    _gaveUp = true;
                    return Wake();
                }
                return Step::None;
            case Phase::Landing:
                if ((_timer -= delta) <= 0.0f) {
                    _phase = Phase::Off;
                }
                return Step::None;
            default:
                return Step::None;
            }
        }

        Phase         Current() const { return _phase; }
        std::uint32_t Kind() const { return _kind; }
        // From MSC's Blackout until he has woken up: the screen is going or is black.
        bool Busy() const { return _phase == Phase::Starting || _phase == Phase::FadingOut || _phase == Phase::Moving; }
        // Moved and not long woken up: a fall now is the move's.
        bool NoFall() const { return _phase == Phase::Moving || _phase == Phase::Landing; }
        bool GaveUp() const { return _gaveUp; }  // faded in without him standing at the marker

    private:
        Step Wake()
        {
            _phase = Phase::Landing;
            _timer = kLandingSeconds;
            return Step::Wake;
        }

        Phase         _phase = Phase::Off;
        std::uint32_t _kind = 0;
        float         _timer = 0.0f;    // seconds left in this phase (Moving: to kArriveTimeout)
        float         _arrived = 0.0f;  // Moving: seconds at the marker in a row
        bool          _gaveUp = false;
    };
}
