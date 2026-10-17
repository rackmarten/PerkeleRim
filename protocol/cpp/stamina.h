// Sprinting costs Skyrim stamina while MSC has the player. MSC's run key (LeftShift) comes from
// Skyrim's input, so Skyrim decides: it drains its stamina while the player runs and, once the bar
// is empty, stops sending the key until some stamina is back. Header-only and free of Skyrim types,
// so the protocol tests cover it.

#pragma once

namespace PerkeleRim::Stamina
{
    // Skyrim's sprint costs about 7 a second.
    inline constexpr float kDrainPerSecond = 7.0f;
    // MSC's boosted walk is about 5.5 m/s and its run about 9.6 m/s (MotorBoost); above this it runs.
    inline constexpr float kSprintSpeed = 7.5f;
    // At or below this the bar counts as empty.
    inline constexpr float kEmpty = 0.5f;
    // An exhausted player runs again at this share of max stamina.
    inline constexpr float kRecoverShare = 0.2f;

    struct Result
    {
        float drain = 0.0f;      // stamina to take off now
        bool  blockRun = false;  // do not send the run key to MSC
    };

    class Sprint
    {
    public:
        // a_runKey: the player holds the run key (as pressed, not as sent). a_speed: ground speed in m/s.
        // a_stamina, a_max: Skyrim's current and maximum stamina. a_onFoot: not seated in a vehicle.
        Result Step(float a_delta, bool a_runKey, float a_speed, float a_stamina, float a_max, bool a_onFoot)
        {
            Result r;
            if (exhausted_ && a_stamina >= a_max * kRecoverShare) {
                exhausted_ = false;
            }
            if (!exhausted_ && a_runKey && a_onFoot && a_stamina <= kEmpty) {
                exhausted_ = true;
            }
            r.blockRun = exhausted_;
            if (!exhausted_ && a_runKey && a_onFoot && a_speed >= kSprintSpeed) {
                r.drain = kDrainPerSecond * a_delta;
            }
            return r;
        }

        void Reset() { exhausted_ = false; }

        bool Exhausted() const { return exhausted_; }

    private:
        bool exhausted_ = false;
    };
}
