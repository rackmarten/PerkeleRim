// Play mode: players never press F10. Loading a save (or starting a new game) hands Kumpo to MSC by
// itself once the game has settled; F10 stays a debug toggle, which also cancels a handover still
// waiting here. Header-only and free of Skyrim types, so the protocol tests cover it;
// skse/src/player/PlayerSync.cpp feeds it a Frame every frame Skyrim has the player.
//
// A load arms it (Load). It then waits for:
//  - Skyrim to settle: kSettleSeconds of normal frames in a row (no loading screen, not paused, the
//    player's 3D in, its cell attached). AhShitRim took the player while Skyrim was still finishing
//    a load and crashed in a vanilla actor's AI the same second;
//  - Skyrim to let the player move: a scene with the movement controls taken away keeps the player
//    (the Helgen cart);
//  - a calm moment: not fighting, riding, swimming, knocked down or ragdolled, sitting or lying in
//    furniture, or talking (the dialogue menu), so MSC doesn't take over mid-action;
//  - the Helgen intro to be over: nothing hands over by itself while Unbound runs or Helgen is being
//    skipped (helgen_skip.h: the cart's fade to black and the move to the garage, which completes
//    Unbound), so the first handover of a new game starts at the garage. F10 is refused while a scene
//    has the player in it or Helgen is being skipped (F10Refused), so a quest that never completes
//    can't keep him;
//  - MSC: linked and in its game. If it isn't, it says so once (NoMsc, or MscLoading while MSC is
//    linked but on its menu or loading its save) and hands over once MSC has been in its game for
//    kMscSeconds (MSC applies a snapshot that waited for it 3 s after its player shows up).
// The link dropping while MSC had the player arms it again (Lost), without saying NoMsc again.

#pragma once

namespace PerkeleRim::PlayMode
{
    inline constexpr float kSettleSeconds = 2.0f;
    inline constexpr float kMscSeconds = 4.0f;

    struct Frame
    {
        bool  linkUp = false;
        bool  mscInGame = false;     // PlayerState kPlayerInGame: MSC's player exists
        bool  loading = false;       // Skyrim's loading screen is up
        bool  paused = false;        // a menu pauses the game
        bool  player3D = false;      // the player's 3D is loaded
        bool  cellAttached = false;  // the player's cell is in
        bool  controls = false;      // Skyrim lets the player move (no scene took the movement controls)
        bool  combat = false;        // the player is in combat
        bool  mounted = false;       // on a horse
        bool  swimming = false;
        bool  knocked = false;       // knocked down, staggered to the ground or ragdolled
        bool  sitting = false;       // sitting, sleeping or getting in or out of furniture
        bool  talking = false;       // the dialogue menu is open
        bool  intro = false;         // the Helgen intro (Unbound, MQ101) is running
        float delta = 0.0f;          // seconds since the last frame
    };

    enum class Phase
    {
        Off,       // nothing to do: handed over, cancelled, or no load yet
        Settling,  // waiting for Skyrim to settle and let the player move
        Intro,     // waiting for the player to get out of Helgen
        Msc,       // Skyrim is ready, MSC isn't (yet)
    };

    enum class Say
    {
        None,
        HandOver,    // hand the player to MSC now
        NoMsc,       // tell the player MSC isn't running
        MscLoading,  // tell the player MSC is still loading
    };

    // Why Skyrim isn't ready for the handover, for the log; null when it is.
    constexpr const char* NotReady(const Frame& a_frame)
    {
        if (a_frame.loading) return "loading";
        if (a_frame.paused) return "paused";
        if (!a_frame.player3D || !a_frame.cellAttached) return "the player isn't in yet";
        if (!a_frame.controls) return "a scene has the controls";
        if (a_frame.combat) return "in combat";
        if (a_frame.mounted) return "riding";
        if (a_frame.swimming) return "swimming";
        if (a_frame.knocked) return "knocked down";
        if (a_frame.sitting) return "sitting";
        if (a_frame.talking) return "talking";
        if (!(a_frame.delta >= 0.0f)) return "a broken frame time";
        return nullptr;
    }

    constexpr bool SkyrimReady(const Frame& a_frame) { return NotReady(a_frame) == nullptr; }

    // F10 in the Helgen intro: refused while its scene has the player (the cart), so nobody leaves
    // the cart by accident; once he can move it hands over, the way out if Unbound never completes
    // (another start, a mod, a stuck stage). Always refused while Helgen is being skipped (a_skipping,
    // helgen_skip.h): the black and the move to the garage have the player even with the controls back.
    constexpr bool F10Refused(bool a_intro, bool a_controls, bool a_skipping = false)
    {
        return a_skipping || (a_intro && !a_controls);
    }

    class Machine
    {
    public:
        // A save loaded or a new game started.
        void Load()
        {
            *this = Machine{};
            _phase = Phase::Settling;
        }

        // The link dropped while MSC had the player: hand over again once it's back. The caller
        // already told the player.
        void Lost()
        {
            Load();
            _said = true;
        }

        // F10 while waiting, or a handover by hand: stay as things are.
        void Cancel() { _phase = Phase::Off; }

        Say Tick(const Frame& a_frame)
        {
            if (_phase == Phase::Off) {
                return Say::None;
            }
            if (a_frame.intro) {
                _phase = Phase::Intro;
                _settled = _msc = 0.0f;
                return Say::None;
            }
            if (_phase == Phase::Intro) {
                _phase = Phase::Settling;  // out of Helgen: settle again
            }
            if (!SkyrimReady(a_frame)) {
                _settled = _msc = 0.0f;  // anything off starts the wait over
                return Say::None;
            }
            if (_settled < kSettleSeconds) {
                _settled += a_frame.delta;
                if (_settled < kSettleSeconds) {
                    return Say::None;
                }
            }
            if (!a_frame.linkUp || !a_frame.mscInGame) {
                _phase = Phase::Msc;
                _msc = 0.0f;
                if (_said) {
                    return Say::None;
                }
                _said = true;
                return a_frame.linkUp ? Say::MscLoading : Say::NoMsc;
            }
            if (_phase == Phase::Msc) {
                _msc += a_frame.delta;  // MSC just arrived: give it a moment
                if (_msc < kMscSeconds) {
                    return Say::None;
                }
            }
            _phase = Phase::Off;
            return Say::HandOver;
        }

        Phase Current() const { return _phase; }
        bool  Waiting() const { return _phase != Phase::Off; }

    private:
        Phase _phase = Phase::Off;
        float _settled = 0.0f;  // seconds Skyrim has been ready in a row
        float _msc = 0.0f;      // seconds MSC has been in its game in a row, while Phase::Msc
        bool  _said = false;    // NoMsc or MscLoading was said for this load
    };
}
