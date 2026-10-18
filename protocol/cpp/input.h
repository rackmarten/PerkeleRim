// Builds the InputState Skyrim sends to MSC from Skyrim's raw input events. Header-only and free of
// Skyrim types, so the protocol tests cover it; the SKSE plugin feeds it from its input sink.
//
// Keyboard events carry DirectInput scan codes (DIK_*), mouse buttons are 0..7. Both map to Unity
// KeyCode values, which is what MSC's Input calls ask for (docs/PROTOCOL.md#input).

#pragma once

#include <cstdint>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Input
{
    // Unity KeyCode values used below (UnityEngine.KeyCode).
    namespace KeyCode
    {
        inline constexpr std::uint16_t None = 0;
        inline constexpr std::uint16_t Space = 32;
        inline constexpr std::uint16_t A = 97;
        inline constexpr std::uint16_t D = 100;
        inline constexpr std::uint16_t S = 115;
        inline constexpr std::uint16_t W = 119;
        inline constexpr std::uint16_t UpArrow = 273;
        inline constexpr std::uint16_t DownArrow = 274;
        inline constexpr std::uint16_t RightArrow = 275;
        inline constexpr std::uint16_t LeftArrow = 276;
        inline constexpr std::uint16_t Mouse0 = 323;
    }

    // Scan codes that stay with Skyrim and never reach MSC: Escape and Tab open Skyrim's menus,
    // ~ opens the console, F5 and F9 quicksave and quickload (MSC's chase camera moves to F8, see
    // below), F10 toggles who owns the player, F11 saves a screenshot of both games, M opens Skyrim's
    // map (MSC's finger moves to V, see KeyRemap.cs).
    inline constexpr std::uint32_t kDikEscape = 0x01;
    inline constexpr std::uint32_t kDikTab = 0x0F;
    inline constexpr std::uint32_t kDikGrave = 0x29;
    inline constexpr std::uint32_t kDikF5 = 0x3F;
    inline constexpr std::uint32_t kDikF8 = 0x42;
    inline constexpr std::uint32_t kDikF9 = 0x43;
    inline constexpr std::uint32_t kDikF10 = 0x44;
    inline constexpr std::uint32_t kDikF11 = 0x57;
    inline constexpr std::uint32_t kDikM = 0x32;
    inline constexpr std::uint32_t kDikT = 0x14;  // Skyrim's Wait menu on foot (MSC has nothing on it there)
    inline constexpr std::uint32_t kDikH = 0x23;  // MSC's hit
    inline constexpr std::uint32_t kDikLeftShift = 0x2A;  // MSC's run

    // DirectInput scan code -> Unity KeyCode, None for keys MSC doesn't get.
    constexpr std::uint16_t KeyFromScanCode(std::uint32_t a_dik)
    {
        // Letters by row, as DirectInput lays them out.
        constexpr const char* kRowQ = "qwertyuiop";  // 0x10..0x19
        constexpr const char* kRowA = "asdfghjkl";   // 0x1E..0x26
        constexpr const char* kRowZ = "zxcvbnm";     // 0x2C..0x32
        if (a_dik >= 0x10 && a_dik <= 0x19) return static_cast<std::uint16_t>(kRowQ[a_dik - 0x10]);
        if (a_dik >= 0x1E && a_dik <= 0x26) return static_cast<std::uint16_t>(kRowA[a_dik - 0x1E]);
        if (a_dik == kDikM) return KeyCode::None;
        if (a_dik >= 0x2C && a_dik <= 0x32) return static_cast<std::uint16_t>(kRowZ[a_dik - 0x2C]);
        if (a_dik >= 0x02 && a_dik <= 0x0A) return static_cast<std::uint16_t>(49 + (a_dik - 0x02));  // Alpha1..Alpha9
        if (a_dik == kDikF5 || a_dik == kDikF9) return KeyCode::None;
        if (a_dik == kDikF8) return 290;  // MSC's F9: its chase camera while in a vehicle
        if (a_dik >= 0x3B && a_dik <= 0x43) return static_cast<std::uint16_t>(282 + (a_dik - 0x3B));  // F1..F9

        switch (a_dik) {
        case 0x0B: return 48;   // Alpha0
        case 0x0C: return 45;   // Minus
        case 0x0D: return 61;   // Equals
        case 0x0E: return 8;    // Backspace
        case 0x1A: return 91;   // LeftBracket
        case 0x1B: return 93;   // RightBracket
        case 0x1C: return 13;   // Return
        case 0x1D: return 306;  // LeftControl
        case 0x27: return 59;   // Semicolon
        case 0x28: return 39;   // Quote
        case 0x2A: return 304;  // LeftShift
        case 0x2B: return 92;   // Backslash
        case 0x33: return 44;   // Comma
        case 0x34: return 46;   // Period
        case 0x35: return 47;   // Slash
        case 0x36: return 303;  // RightShift
        case 0x37: return 268;  // KeypadMultiply
        case 0x38: return 308;  // LeftAlt
        case 0x39: return KeyCode::Space;
        case 0x3A: return 301;  // CapsLock
        case 0x47: return 263;  // Keypad7
        case 0x48: return 264;  // Keypad8
        case 0x49: return 265;  // Keypad9
        case 0x4A: return 269;  // KeypadMinus
        case 0x4B: return 260;  // Keypad4
        case 0x4C: return 261;  // Keypad5
        case 0x4D: return 262;  // Keypad6
        case 0x4E: return 270;  // KeypadPlus
        case 0x4F: return 257;  // Keypad1
        case 0x50: return 258;  // Keypad2
        case 0x51: return 259;  // Keypad3
        case 0x52: return 256;  // Keypad0
        case 0x53: return 266;  // KeypadPeriod
        case 0x58: return 293;  // F12
        case 0x9C: return 271;  // KeypadEnter
        case 0x9D: return 305;  // RightControl
        case 0xB5: return 267;  // KeypadDivide
        case 0xB8: return 307;  // RightAlt
        case 0xC7: return 278;  // Home
        case 0xC8: return KeyCode::UpArrow;
        case 0xC9: return 280;  // PageUp
        case 0xCB: return KeyCode::LeftArrow;
        case 0xCD: return KeyCode::RightArrow;
        case 0xCF: return 279;  // End
        case 0xD0: return KeyCode::DownArrow;
        case 0xD1: return 281;  // PageDown
        case 0xD2: return 277;  // Insert
        case 0xD3: return 127;  // Delete
        default: return KeyCode::None;  // Escape, Tab, ~, F10 and anything unmapped
        }
    }

    // Mouse button 0..6 -> Mouse0..Mouse6; the wheel (8, 9) and anything else -> None.
    constexpr std::uint16_t KeyFromMouseButton(std::uint32_t a_button)
    {
        return a_button <= 6 ? static_cast<std::uint16_t>(KeyCode::Mouse0 + a_button) : KeyCode::None;
    }

    // The wheel arrives as mouse buttons 8 (up) and 9 (down), one press per notch: +1, -1, else 0.
    constexpr int WheelNotches(std::uint32_t a_button)
    {
        return a_button == 8 ? 1 : a_button == 9 ? -1 : 0;
    }

    // Mouse counts to "Mouse X"/"Mouse Y" axis units. Unity's default is 0.1; on top of MSC's own
    // mouse look sensitivity that turned too fast in play, so it's lower.
    inline constexpr double kMouseAxisPerCount = 0.06;

    // One wheel notch in "Mouse ScrollWheel" units, Unity's default.
    inline constexpr double kWheelAxisPerNotch = 0.1;

    // Accumulates held keys and mouse movement and produces the InputState to publish.
    class Builder
    {
    public:
        void SetKey(std::uint16_t a_keyCode, bool a_down)
        {
            if (a_keyCode == KeyCode::None || a_keyCode >= 512) {
                return;
            }
            const std::uint64_t bit = 1ull << (a_keyCode & 63);
            if (a_down) {
                _keys[a_keyCode >> 6] |= bit;
            } else {
                _keys[a_keyCode >> 6] &= ~bit;
            }
        }

        bool IsKeyDown(std::uint16_t a_keyCode) const
        {
            return a_keyCode < 512 && (_keys[a_keyCode >> 6] & (1ull << (a_keyCode & 63))) != 0;
        }

        // Holds a key back from Build while it stays down here (the run key of an exhausted player).
        void Block(std::uint16_t a_keyCode, bool a_blocked)
        {
            if (a_keyCode == KeyCode::None || a_keyCode >= 512) {
                return;
            }
            const std::uint64_t bit = 1ull << (a_keyCode & 63);
            if (a_blocked) {
                _blocked[a_keyCode >> 6] |= bit;
            } else {
                _blocked[a_keyCode >> 6] &= ~bit;
            }
        }

        // Raw counts from a mouse move event; DirectInput's y grows downwards, Unity's "Mouse Y" up.
        void AddMouse(std::int32_t a_dx, std::int32_t a_dy)
        {
            _mouseX += a_dx * kMouseAxisPerCount;
            _mouseY -= a_dy * kMouseAxisPerCount;
        }

        // Wheel notches, positive away from the user (WheelNotches).
        void AddWheel(int a_notches)
        {
            _mouseWheel += a_notches * kWheelAxisPerNotch;
        }

        // Everything released, e.g. when ownership ends. Mouse totals keep accumulating.
        void ReleaseAll()
        {
            for (auto& k : _keys) {
                k = 0;
            }
        }

        // a_live false (a Skyrim menu has the input): no keys or axes, so MSC stands still.
        // a_paused (that menu pauses the game, e.g. Escape or the map): MSC freezes its time too.
        Protocol::InputState Build(std::uint32_t a_frame, bool a_ownsPlayer, bool a_live, bool a_paused = false) const
        {
            Protocol::InputState s{};
            s.frame = a_frame;
            s.flags = a_ownsPlayer ? Protocol::kInputOwnsPlayer : 0;
            if (a_ownsPlayer && a_paused) {
                s.flags |= Protocol::kInputPaused;
            }
            s.mouseX = _mouseX;
            s.mouseY = _mouseY;
            s.mouseWheel = _mouseWheel;
            if (a_live) {
                for (int i = 0; i < 8; ++i) {
                    s.keys[i] = _keys[i] & ~_blocked[i];
                }
                s.horizontal = Axis(KeyCode::D, KeyCode::RightArrow, KeyCode::A, KeyCode::LeftArrow);
                s.vertical = Axis(KeyCode::W, KeyCode::UpArrow, KeyCode::S, KeyCode::DownArrow);
            }
            return s;
        }

    private:
        float Axis(std::uint16_t a_pos, std::uint16_t a_pos2, std::uint16_t a_neg, std::uint16_t a_neg2) const
        {
            float v = 0.0f;
            if (IsKeyDown(a_pos) || IsKeyDown(a_pos2)) v += 1.0f;
            if (IsKeyDown(a_neg) || IsKeyDown(a_neg2)) v -= 1.0f;
            return v;
        }

        std::uint64_t _keys[8]{};
        std::uint64_t _blocked[8]{};
        double        _mouseX = 0.0;
        double        _mouseY = 0.0;
        double        _mouseWheel = 0.0;
    };
}
