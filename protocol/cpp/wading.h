// Wading washes Kumpo. Skyrim reads how much of him is under water (0 to 1, GetSubmergeLevel at
// his feet) and tells MSC once when it reaches kWash, about his waist. Leaving the water re-arms
// it: the level has to drop below kRearm (a little lower, so waves at the edge don't wash him
// again and again). Not on foot (seated, MSC without the player) counts as out of the water.
// Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

namespace PerkeleRim::Wading
{
    inline constexpr float kWash = 0.5f;   // submerged fraction that washes
    inline constexpr float kRearm = 0.4f;  // below this he is out of the water again

    class Edge
    {
    public:
        // Once a frame. a_level: submerged fraction (0..1). a_onFoot: MSC has the player and he is
        // not seated. True on the frame he walks in up to the waist.
        bool Step(float a_level, bool a_onFoot)
        {
            if (!a_onFoot || a_level < kRearm) {
                armed_ = true;
                return false;
            }
            if (armed_ && a_level >= kWash) {
                armed_ = false;
                return true;
            }
            return false;
        }

        // A load, a new world: the next time he is in the water counts.
        void Reset() { armed_ = true; }

    private:
        bool armed_ = true;
    };
}
