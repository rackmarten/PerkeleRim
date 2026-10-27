// MSC's options menu drawn in Skyrim (F1). Skyrim runs fullscreen, so MSC's own menu (graphics,
// controls, sliders) is used from Skyrim: Skyrim opens a pausing menu of its own with its cursor and
// MSC's menu drawn over it (the hand layer's texture, kHandLayerMenu), and sends the cursor and the
// mouse buttons and keys on. Header-only and free of Skyrim types, so the protocol tests cover it;
// skse/src/menu/MscMenu.cpp feeds it every frame.
//
// Skyrim asks with kInputMscMenu while it wants the menu up; MSC opens it, keeps it open (PlayerLink
// otherwise closes it while handed over) and reports kPlayerMscMenu while it is. Either side closes
// it: F1 or Escape in Skyrim drops the request, MSC's Resume button drops the flag.

#pragma once

#include <algorithm>
#include <cstdint>

#include "input.h"

namespace PerkeleRim::MscMenu
{
    inline constexpr std::uint32_t kDikF1 = 0x3B;
    inline constexpr float         kOpenSeconds = 3.0f;   // MSC didn't open its menu by then: give up
    inline constexpr float         kCloseSeconds = 3.0f;  // MSC didn't close it by then: stop waiting

    // The keys that close the menu from Skyrim; they never reach MSC while it's up.
    constexpr bool ClosesMenu(std::uint32_t a_dik) { return a_dik == kDikF1 || a_dik == Input::kDikEscape; }

    // Skyrim's cursor (screen pixels from the top left) as InputState.cursorX/Y: 0..1, clamped.
    constexpr float Normalize(float a_pixels, float a_size)
    {
        if (!(a_size > 0.0f) || !(a_pixels == a_pixels)) {  // no screen yet, or NaN
            return 0.5f;
        }
        return std::clamp(a_pixels / a_size, 0.0f, 1.0f);
    }

    // What Skyrim's cursor position (MenuCursor cursorPosX/Y, back buffer pixels) is a share of:
    // the back buffer, which MSC's menu is drawn over edge to edge. MenuCursor's screenWidthX can be
    // smaller (about 1800 of 1920, windowed), which put MSC's picks up to 6% right of the cursor;
    // it only stands in before the renderer knows its size.
    constexpr float CursorSpan(float a_backBuffer, float a_cursorScreen) { return a_backBuffer > 0.0f ? a_backBuffer : a_cursorScreen; }

    struct Frame
    {
        bool  owned = false;    // MSC has the player
        bool  linkUp = false;
        bool  mscOpen = false;  // PlayerState kPlayerMscMenu
        float delta = 0.0f;     // seconds since the last frame
    };

    enum class Phase
    {
        Closed,
        Opening,  // asked, MSC's menu isn't up yet
        Open,     // MSC's menu is up
        Closing,  // Skyrim closed it, MSC's menu is still up
    };

    enum class Say
    {
        None,
        Opened,     // MSC's menu came up
        Closed,     // closed, by either side
        NoAnswer,   // MSC never opened it (kOpenSeconds)
        Abandoned,  // the player went back to Skyrim or the link dropped
    };

    class Machine
    {
    public:
        // F1 while closed: true if the menu starts opening (MSC has the player and the link is up).
        bool Press(const Frame& a_frame)
        {
            if (_phase != Phase::Closed || !a_frame.owned || !a_frame.linkUp) {
                return false;
            }
            _phase = Phase::Opening;
            _timer = 0.0f;
            return true;
        }

        // F1 or Escape while it's up (or still opening): Skyrim closes its side at once.
        void Close()
        {
            if (_phase == Phase::Opening || _phase == Phase::Open) {
                _phase = Phase::Closing;
                _timer = 0.0f;
            }
        }

        Say Tick(const Frame& a_frame)
        {
            if (_phase == Phase::Closed) {
                return Say::None;
            }
            if (!a_frame.owned || !a_frame.linkUp) {
                _phase = Phase::Closed;
                return Say::Abandoned;
            }
            _timer += (std::max)(a_frame.delta, 0.0f);  // parenthesized: Windows.h defines max
            switch (_phase) {
            case Phase::Opening:
                if (a_frame.mscOpen) {
                    _phase = Phase::Open;
                    return Say::Opened;
                }
                if (_timer >= kOpenSeconds) {
                    _phase = Phase::Closed;
                    return Say::NoAnswer;
                }
                return Say::None;
            case Phase::Open:
                if (!a_frame.mscOpen) {  // Resume in MSC's menu
                    _phase = Phase::Closed;
                    return Say::Closed;
                }
                return Say::None;
            case Phase::Closing:
                if (!a_frame.mscOpen || _timer >= kCloseSeconds) {
                    _phase = Phase::Closed;
                    return Say::Closed;
                }
                return Say::None;
            default:
                return Say::None;
            }
        }

        Phase Current() const { return _phase; }

        // InputState kInputMscMenu: MSC should have its menu up.
        bool Requested() const { return _phase == Phase::Opening || _phase == Phase::Open; }

        // Skyrim's own menu (the pause, the cursor, MSC's menu drawn) is up.
        bool Shown() const { return Requested(); }

    private:
        Phase _phase = Phase::Closed;
        float _timer = 0.0f;
    };
}
