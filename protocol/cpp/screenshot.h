// The ScreenshotEvent message (Skyrim -> MSC): where MSC saves its view for the F11 pair. Header-only,
// mirrored by msc/src/Link/Screenshot.cs; the protocol tests cover both directions. Name is how a
// pair is called: <name>_skyrim.png and <name>_msc.png.

#pragma once

#include <cstdint>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Screenshot
{
    // Payload for a_path (UTF-8); nothing when it's empty or longer than kScreenshotPathMax bytes.
    inline std::optional<std::vector<std::uint8_t>> Encode(std::string_view a_path)
    {
        if (a_path.empty() || a_path.size() > Protocol::kScreenshotPathMax) {
            return std::nullopt;
        }
        Protocol::ScreenshotEvent header{};
        header.length = static_cast<std::uint32_t>(a_path.size());
        std::vector<std::uint8_t> payload(sizeof(header) + a_path.size());
        std::memcpy(payload.data(), &header, sizeof(header));
        std::memcpy(payload.data() + sizeof(header), a_path.data(), a_path.size());
        return payload;
    }

    // The path in a payload (points into it), nothing when it's malformed.
    inline std::optional<std::string_view> Decode(const std::uint8_t* a_payload, std::uint32_t a_size)
    {
        Protocol::ScreenshotEvent header{};
        if (!a_payload || a_size < sizeof(header)) {
            return std::nullopt;
        }
        std::memcpy(&header, a_payload, sizeof(header));
        if (header.length == 0 || header.length > Protocol::kScreenshotPathMax || sizeof(header) + header.length > a_size) {
            return std::nullopt;
        }
        return std::string_view(reinterpret_cast<const char*>(a_payload + sizeof(header)), header.length);
    }

    // "2026-10-17_14-03-22" for local time a_time; a_take > 1 (a second pair in the same second)
    // adds "_2", "_3".
    inline std::string Name(const std::tm& a_time, int a_take = 1)
    {
        char text[40];
        std::snprintf(text, sizeof(text), "%04d-%02d-%02d_%02d-%02d-%02d", a_time.tm_year + 1900, a_time.tm_mon + 1, a_time.tm_mday,
            a_time.tm_hour, a_time.tm_min, a_time.tm_sec);
        std::string name = text;
        if (a_take > 1) {
            name += "_" + std::to_string(a_take);
        }
        return name;
    }
}
