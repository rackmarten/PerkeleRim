// Kumpo's Helgen intro: he drinks himself unconscious in MSC and wakes up in Skyrim's Helgen cart.
// A new game is the intro (Skyrim's own cart ride, untouched). As it opens, MSC plays Kumpo
// drinking one beer too many and passing out (IntroCue kIntroBlackout) while Skyrim fades up from
// black again and captions it; right after Ralof's "Hey, you. You're finally awake." (Skyrim's
// topic event for MQ101's INFO 00101AEA, AhShitRim's trigger for CJ's line) Kumpo answers with a
// groan and a Perkele (kIntroWake). Nothing hands over during the intro (play_mode.h). Header-only
// and free of Skyrim types, so the protocol tests cover it; skse/src/intro/Intro.cpp feeds it.

#pragma once

#include <cstdint>
#include <string_view>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Intro
{
    inline constexpr std::uint32_t kUnbound = 0x0003372B;            // Skyrim.esm: MQ101, the Helgen intro
    inline constexpr std::uint32_t kRalofFinallyAwake = 0x00101AEA;  // Skyrim.esm: MQ101's INFO for Ralof's first line in the cart

    inline constexpr float kBlackoutWindow = 10.0f;  // seconds into a new game the blackout may still start (MSC must be in its game)
    inline constexpr float kWakeDelay = 0.3f;        // Kumpo answers this long after Ralof's line ends

    inline constexpr std::string_view kBlackoutCaption = "[Kumpo drinks one beer too many and passes out]";
    inline constexpr float            kBlackoutCaptionSeconds = 7.0f;
    inline constexpr std::string_view kWakeCaption = "Kumpo: Perkele...";
    inline constexpr float            kWakeCaptionSeconds = 3.0f;

    // When each cue goes to MSC. A cue MSC isn't there for is dropped, not kept for later.
    class Cues
    {
    public:
        // A new game: the blackout plays as soon as MSC is in its game, within kBlackoutWindow.
        void NewGame()
        {
            *this = Cues{};
            _blackout = 0.0f;
        }

        // A save loaded: no blackout; a save from before Ralof's line hears Kumpo's answer again.
        void Loaded() { *this = Cues{}; }

        void OnTopicEnd(std::uint32_t a_info)
        {
            if (a_info == kRalofFinallyAwake && !_woke && _wake < 0.0f) {
                _wake = kWakeDelay;
            }
        }

        // Once a frame; a_msc: MSC is linked and in its game. Returns the cue to send this frame,
        // 0 for none (kIntroBlackout first if both are due).
        std::uint32_t Tick(float a_delta, bool a_msc)
        {
            if (_blackout >= 0.0f) {
                _blackout += a_delta;
                if (a_msc) {
                    _blackout = -1.0f;
                    return Protocol::kIntroBlackout;
                }
                if (_blackout > kBlackoutWindow) {
                    _blackout = -1.0f;  // MSC wasn't there in time
                }
            }
            if (_wake >= 0.0f) {
                _wake -= a_delta;
                if (_wake <= 0.0f) {
                    _wake = -1.0f;
                    _woke = true;
                    return a_msc ? Protocol::kIntroWake : 0;
                }
            }
            return 0;
        }

    private:
        float _blackout = -1.0f;  // seconds since the new game started, -1 while it isn't waiting
        float _wake = -1.0f;      // seconds to Kumpo's answer, -1 while nothing waits
        bool  _woke = false;      // answered (or dropped) since the game loaded
    };

    constexpr std::string_view Caption(std::uint32_t a_cue)
    {
        return a_cue == Protocol::kIntroBlackout ? kBlackoutCaption : a_cue == Protocol::kIntroWake ? kWakeCaption : std::string_view{};
    }

    constexpr float CaptionSeconds(std::uint32_t a_cue)
    {
        return a_cue == Protocol::kIntroBlackout ? kBlackoutCaptionSeconds : a_cue == Protocol::kIntroWake ? kWakeCaptionSeconds : 0.0f;
    }
}
