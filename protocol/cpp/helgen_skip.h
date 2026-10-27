// Skipping Helgen: a new game's cart ride (intro_cues.h) ends a few seconds after Kumpo's Perkele
// with a slow fade to black; Skyrim then lets him off the cart, Unbound (MQ101) completes the way
// it does after the keep, Helgen is left in ruins, Before the Storm (MQ102) starts on the side the
// player picked (HelgenSide in the launcher's PerkeleRim.ini, Imperial by default), and Kumpo, a
// Nord, wakes up at the garage's spawn marker with a fade back in. Play mode hands him over from
// there as usual (play_mode.h). Without MSC the Perkele isn't heard, but the skip comes at the same
// moment; if Ralof's line never ends (no cue at all), the cart reaching Helgen's gate skips it.
// Header-only and free of Skyrim types, so the protocol tests cover it; skse/src/intro/HelgenSkip.cpp
// feeds it a Frame a frame and does each Step.

#pragma once

#include <cstddef>
#include <cstdint>
#include <string_view>

namespace PerkeleRim::HelgenSkip
{
    inline constexpr std::uint32_t kBeforeTheStorm = 0x0004E50D;  // Skyrim.esm: MQ102
    inline constexpr std::uint32_t kDragonAttack = 0x000D0593;    // Skyrim.esm: MQ101DragonAttack, Helgen burning
    // MQ101DragonAttack's last stage: the fires and rubble go, Helgen's ruins (PostCG*) come in, and it stops.
    inline constexpr std::uint32_t kRuinsStage = 200;
    // Unbound's stage when the cart rolls through Helgen's gate (the fallback trigger).
    inline constexpr std::uint32_t kCartAtGate = 20;

    inline constexpr float kPerkeleSeconds = 2.5f;   // swear09, as long as its caption (intro_cues.h kWakeLines)
    inline constexpr float kAfterPerkele = 3.0f;     // the fade starts this long after the Perkele is over
    inline constexpr float kFadeOutSeconds = 4.0f;   // the slow fade to black
    inline constexpr float kReleaseSeconds = 1.0f;   // off the cart before the move (Papyrus runs the release)
    inline constexpr float kArrivedSeconds = 1.0f;   // at the garage and settled this long before fading in
    inline constexpr float kMoveRetrySeconds = 5.0f; // not there yet (the cart pulled him back): move again
    inline constexpr float kArriveTimeout = 60.0f;   // fades in anyway, so the screen never stays black
    inline constexpr float kFadeInSeconds = 3.0f;

    // Who Before the Storm sends Kumpo to. Vanilla's own skip stages of MQ102 set up its friend
    // quest (MQ102A Hadvar, MQ102B Ralof), point the journal at his relative in Riverwood and
    // complete Unbound (MQ101 stage 900, which stops it at 1000).
    enum class Side
    {
        Imperial,    // Hadvar, then Alvor
        Stormcloak,  // Ralof, then Gerdur
    };

    constexpr bool SameText(std::string_view a_a, std::string_view a_b)
    {
        if (a_a.size() != a_b.size()) {
            return false;
        }
        for (std::size_t i = 0; i < a_a.size(); ++i) {
            const char x = a_a[i] >= 'A' && a_a[i] <= 'Z' ? static_cast<char>(a_a[i] - 'A' + 'a') : a_a[i];
            const char y = a_b[i] >= 'A' && a_b[i] <= 'Z' ? static_cast<char>(a_b[i] - 'A' + 'a') : a_b[i];
            if (x != y) {
                return false;
            }
        }
        return true;
    }

    // The ini's HelgenSide value: Stormcloak (or Ralof) picks Ralof's side, anything else Hadvar's.
    constexpr Side ParseSide(std::string_view a_value)
    {
        return SameText(a_value, "Stormcloak") || SameText(a_value, "Stormcloaks") || SameText(a_value, "Ralof") ? Side::Stormcloak : Side::Imperial;
    }

    constexpr std::uint32_t BeforeTheStormStage(Side a_side) { return a_side == Side::Stormcloak ? 2u : 1u; }
    constexpr std::string_view Friend(Side a_side) { return a_side == Side::Stormcloak ? "Ralof" : "Hadvar"; }

    struct Frame
    {
        bool          intro = false;     // Unbound runs (the cart is there)
        std::uint32_t unboundStage = 0;  // its current stage
        bool          loading = false;   // Skyrim's loading screen is up
        bool          arrived = false;   // the player is at the garage's spawn marker, 3D and cell in
        float         delta = 0.0f;      // seconds since the last frame
    };

    enum class Step
    {
        None,
        FadeOut,  // start the slow fade to black
        Release,  // the screen is black: off the cart, Kumpo the Nord, the quests
        Move,     // move him to the garage's spawn marker
        FadeIn,   // fade back in at the garage
    };

    enum class Phase
    {
        Off,        // not a new game, or done
        Cart,       // a new game's cart: waiting for the Perkele
        Waiting,    // the Perkele went out (or would have): kPerkeleSeconds + kAfterPerkele
        FadingOut,  // kFadeOutSeconds
        Releasing,  // kReleaseSeconds
        Moving,     // until he's at the garage for kArrivedSeconds
    };

    class Machine
    {
    public:
        void NewGame()
        {
            *this = Machine{};
            _phase = Phase::Cart;
        }

        // A save loaded: the cart can't be saved in (Unbound turns saving off), so nothing to skip.
        void Loaded() { *this = Machine{}; }

        // Kumpo's Perkele went to MSC, or was dropped because MSC wasn't there.
        void Woke()
        {
            if (_phase == Phase::Cart) {
                _phase = Phase::Waiting;
                _timer = kPerkeleSeconds + kAfterPerkele;
            }
        }

        Step Tick(const Frame& a_frame)
        {
            const float delta = a_frame.delta > 0.0f ? a_frame.delta : 0.0f;
            switch (_phase) {
            case Phase::Cart:
            case Phase::Waiting:
                if (a_frame.intro && a_frame.unboundStage >= kCartAtGate) {
                    return FadeOut();  // never heard Ralof's line: don't let the cart reach the block
                }
                if (_phase == Phase::Waiting && (_timer -= delta) <= 0.0f) {
                    if (!a_frame.intro) {
                        _phase = Phase::Off;  // no cart (a mod skipped it): nothing to skip
                        return Step::None;
                    }
                    return FadeOut();
                }
                return Step::None;
            case Phase::FadingOut:
                if ((_timer -= delta) > 0.0f) {
                    return Step::None;
                }
                _phase = Phase::Releasing;
                _timer = kReleaseSeconds;
                return Step::Release;
            case Phase::Releasing:
                if ((_timer -= delta) > 0.0f) {
                    return Step::None;
                }
                _phase = Phase::Moving;
                _timer = kArriveTimeout;
                _retry = kMoveRetrySeconds;
                _arrived = 0.0f;
                return Step::Move;
            case Phase::Moving:
                _timer -= delta;
                if (a_frame.arrived && !a_frame.loading) {
                    _arrived += delta;
                    if (_arrived >= kArrivedSeconds) {
                        _phase = Phase::Off;
                        return Step::FadeIn;
                    }
                    return Step::None;
                }
                _arrived = 0.0f;
                if (_timer <= 0.0f) {
                    _phase = Phase::Off;
                    _gaveUp = true;
                    return Step::FadeIn;
                }
                if (!a_frame.loading && (_retry -= delta) <= 0.0f) {
                    _retry = kMoveRetrySeconds;
                    return Step::Move;
                }
                return Step::None;
            default:
                return Step::None;
            }
        }

        Phase Current() const { return _phase; }
        // The fade has started and he isn't at the garage yet: play mode waits.
        bool Skipping() const { return _phase == Phase::FadingOut || _phase == Phase::Releasing || _phase == Phase::Moving; }
        bool GaveUp() const { return _gaveUp; }  // faded in without him at the garage

    private:
        Step FadeOut()
        {
            _phase = Phase::FadingOut;
            _timer = kFadeOutSeconds;
            return Step::FadeOut;
        }

        Phase _phase = Phase::Off;
        float _timer = 0.0f;    // seconds left in this phase (Moving: to kArriveTimeout)
        float _retry = 0.0f;    // Moving: seconds to the next move
        float _arrived = 0.0f;  // Moving: seconds at the garage in a row
        bool  _gaveUp = false;
    };
}
