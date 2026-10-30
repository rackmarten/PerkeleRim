// An empty Flash movie for Skyrim's Scaleform: a custom Skyrim menu needs a movie file to load, and
// PerkeleRim fills its menus from code (ActionScript calls, img:// textures), so the movie itself is
// only a stage. Built with the plugin (skse/tools/swfgen.cpp) and installed as
// Data/Interface/PerkeleRim/MscMenu.swf. Header-only, so the protocol tests cover it.
//
// SWF 10, uncompressed: header, FileAttributes (ActionScript 2), a black background, one frame.

#pragma once

#include <cstdint>
#include <vector>

namespace PerkeleRim::Swf
{
    inline constexpr std::uint8_t kVersion = 10;
    inline constexpr std::uint16_t kTagEnd = 0;
    inline constexpr std::uint16_t kTagShowFrame = 1;
    inline constexpr std::uint16_t kTagSetBackgroundColor = 9;
    inline constexpr std::uint16_t kTagFileAttributes = 69;

    namespace Detail
    {
        inline void U16(std::vector<std::uint8_t>& a_out, std::uint32_t a_value)
        {
            a_out.push_back(static_cast<std::uint8_t>(a_value & 0xFF));
            a_out.push_back(static_cast<std::uint8_t>((a_value >> 8) & 0xFF));
        }

        inline void U32(std::vector<std::uint8_t>& a_out, std::uint32_t a_value)
        {
            U16(a_out, a_value & 0xFFFF);
            U16(a_out, a_value >> 16);
        }

        // A short tag header: code in the top 10 bits, length (under 63) in the low 6.
        inline void Tag(std::vector<std::uint8_t>& a_out, std::uint16_t a_code, std::uint32_t a_length)
        {
            U16(a_out, (static_cast<std::uint32_t>(a_code) << 6) | a_length);
        }

        // RECT: 5 bits of field width, then xmin, xmax, ymin, ymax as signed fields, MSB first,
        // padded to a byte.
        inline void Rect(std::vector<std::uint8_t>& a_out, std::int32_t a_xMax, std::int32_t a_yMax)
        {
            constexpr std::uint32_t kBits = 16;  // up to 32767 twips (1638 px), past any stage here
            std::uint64_t           acc = 0;
            int                     used = 0;
            auto                    put = [&](std::uint32_t a_value, int a_bits) {
                for (int i = a_bits - 1; i >= 0; --i) {
                    acc = (acc << 1) | ((a_value >> i) & 1u);
                    if (++used == 8) {
                        a_out.push_back(static_cast<std::uint8_t>(acc));
                        acc = 0;
                        used = 0;
                    }
                }
            };
            put(kBits, 5);
            put(0, kBits);
            put(static_cast<std::uint32_t>(a_xMax), kBits);
            put(0, kBits);
            put(static_cast<std::uint32_t>(a_yMax), kBits);
            if (used > 0) {
                a_out.push_back(static_cast<std::uint8_t>(acc << (8 - used)));
            }
        }
    }

    // a_width x a_height pixels (20 twips each, at most 1638), a_fps frames a second.
    inline std::vector<std::uint8_t> EmptyMovie(std::uint32_t a_width = 1280, std::uint32_t a_height = 720, std::uint8_t a_fps = 30)
    {
        std::vector<std::uint8_t> out{ 'F', 'W', 'S', kVersion, 0, 0, 0, 0 };  // length patched below
        Detail::Rect(out, static_cast<std::int32_t>(a_width * 20), static_cast<std::int32_t>(a_height * 20));
        Detail::U16(out, static_cast<std::uint32_t>(a_fps) << 8);  // 8.8 fixed point
        Detail::U16(out, 1);                                         // frames
        Detail::Tag(out, kTagFileAttributes, 4);
        Detail::U32(out, 0);  // ActionScript 2, no network access, no metadata
        Detail::Tag(out, kTagSetBackgroundColor, 3);
        out.insert(out.end(), { 0, 0, 0 });
        Detail::Tag(out, kTagShowFrame, 0);
        Detail::Tag(out, kTagEnd, 0);
        const auto size = static_cast<std::uint32_t>(out.size());
        for (int i = 0; i < 4; ++i) {
            out[4 + i] = static_cast<std::uint8_t>((size >> (8 * i)) & 0xFF);
        }
        return out;
    }
}
