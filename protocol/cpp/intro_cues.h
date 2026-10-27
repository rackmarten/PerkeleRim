// Kumpo's Helgen intro: he drinks himself unconscious in MSC and wakes up in Skyrim's Helgen cart.
// A new game is the intro (Skyrim's own cart ride, untouched). As it opens, MSC plays Kumpo, who
// drank one beer too many, snoring and then groaning his hangover line (IntroCue kIntroBlackout)
// while Skyrim fades up from black again and captions it; right after Ralof's "Hey, you. You're
// finally awake." (Skyrim's topic event for MQ101's INFO 00101AEA, AhShitRim's trigger for CJ's
// line), and not before the blackout's sounds are over, Kumpo says Perkele (kIntroWake). A save's
// first handover, which wakes him up in the shack by the garage, plays the hangover line alone
// (kIntroHangover), so both stories start with him waking up after a night out. Nothing hands over
// during the intro (play_mode.h). Header-only and free of Skyrim types, so the protocol tests cover
// it; skse/src/intro/Intro.cpp feeds it. MSC's half (which clips, when) is msc/src/Link/IntroLine.cs,
// whose times the captions below follow.

#pragma once

#include <cstdint>
#include <span>
#include <string_view>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Intro
{
    inline constexpr std::uint32_t kUnbound = 0x0003372B;            // Skyrim.esm: MQ101, the Helgen intro
    inline constexpr std::uint32_t kRalofFinallyAwake = 0x00101AEA;  // Skyrim.esm: MQ101's INFO for Ralof's first line in the cart

    inline constexpr float kBlackoutWindow = 10.0f;  // seconds into a new game the blackout may still start (MSC must be in its game)
    inline constexpr float kWakeDelay = 0.3f;        // Kumpo answers this long after Ralof's line ends
    // MSC's hangover line, hangover05 ("Ei hittoa, toi on kyllä viimeinen kerta, kun mä otan mitä.
    // Ai saatana, mikä olo.", 9.65 s), starts this long into the blackout (IntroLine.Blackout).
    inline constexpr float kHangoverAt = 14.0f;
    inline constexpr float kHangoverSeconds = 9.65f;
    inline constexpr float kBlackoutSeconds = kHangoverAt + kHangoverSeconds;  // the blackout's sounds are over

    // A caption on Skyrim's HUD: a_at seconds after its cue, for a_seconds.
    struct Line
    {
        float            at;
        std::string_view text;
        float            seconds;
    };

    inline constexpr Line kBlackoutLines[] = {
        { 0.0f, "[Kumpo drank one beer too many and passed out]", 13.5f },
        { kHangoverAt, "Kumpo: Damn, that was definitely the last time I drink anything.", 5.0f },
        { kHangoverAt + 5.0f, "Kumpo: Oh satan, what a feeling.", kHangoverSeconds - 5.0f },
    };
    inline constexpr Line kHangoverLines[] = {
        { 0.0f, "Kumpo: Damn, that was definitely the last time I drink anything.", 5.0f },
        { 5.0f, "Kumpo: Oh satan, what a feeling.", kHangoverSeconds - 5.0f },
    };
    inline constexpr Line kWakeLines[] = {
        { 0.0f, "Kumpo: Perkele!", 2.5f },
    };

    constexpr std::span<const Line> Captions(std::uint32_t a_cue)
    {
        switch (a_cue) {
        case Protocol::kIntroBlackout:
            return kBlackoutLines;
        case Protocol::kIntroWake:
            return kWakeLines;
        case Protocol::kIntroHangover:
            return kHangoverLines;
        default:
            return {};
        }
    }

    // The caption showing a_since seconds after a cue, or empty.
    constexpr std::string_view CaptionAt(std::uint32_t a_cue, float a_since)
    {
        std::string_view text;
        for (const auto& line : Captions(a_cue)) {
            if (a_since >= line.at && a_since < line.at + line.seconds) {
                text = line.text;
            }
        }
        return text;
    }

    constexpr float CaptionsSeconds(std::uint32_t a_cue)
    {
        float end = 0.0f;
        for (const auto& line : Captions(a_cue)) {
            end = end > line.at + line.seconds ? end : line.at + line.seconds;
        }
        return end;
    }

    // When each cue of the cart goes to MSC. A cue MSC isn't there for is dropped, not kept for later.
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

        // Once a frame; a_msc: MSC is linked and in its game (and, for Skyrim, the cart is there:
        // Unbound runs). Returns the cue to send this frame, 0 for none (kIntroBlackout first if
        // both are due). The answer waits for the blackout's sounds to be over.
        std::uint32_t Tick(float a_delta, bool a_msc)
        {
            if (_sinceBlackout >= 0.0f) {
                _sinceBlackout += a_delta;
            }
            if (_blackout >= 0.0f) {
                _blackout += a_delta;
                if (a_msc) {
                    _blackout = -1.0f;
                    _sinceBlackout = 0.0f;
                    return Protocol::kIntroBlackout;
                }
                if (_blackout > kBlackoutWindow) {
                    _blackout = -1.0f;  // MSC wasn't there in time
                }
            }
            if (_wake >= 0.0f) {
                _wake = _wake > a_delta ? _wake - a_delta : 0.0f;  // holds at 0 while the blackout still runs
                if (_wake <= 0.0f &&(_sinceBlackout < 0.0f || _sinceBlackout >= kBlackoutSeconds)) {
                    _wake = -1.0f;
                    _woke = true;
                    return a_msc ? Protocol::kIntroWake : 0;
                }
            }
            return 0;
        }

        // Kumpo answered Ralof, or would have if MSC had been there (helgen_skip.h starts from it).
        bool Woke() const { return _woke; }

    private:
        float _blackout = -1.0f;       // seconds since the new game started, -1 while it isn't waiting
        float _sinceBlackout = -1.0f;  // seconds since the blackout went out, -1 if it didn't
        float _wake = -1.0f;           // seconds to Kumpo's answer, -1 while nothing waits
        bool  _woke = false;           // answered (or dropped) since the game loaded
    };
}
