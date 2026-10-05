// The LookAtText message (MSC -> Skyrim): MSC's interaction prompt for what its crosshair is on
// (text and hand icon). Header-only, mirrored by msc/src/Link/LookAt.cs; the protocol tests cover
// both directions.

#pragma once

#include <cstdint>
#include <cstring>
#include <optional>
#include <string_view>
#include <vector>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::LookAt
{
    struct Prompt
    {
        std::string_view text;   // points into the payload
        std::uint32_t    flags;  // kLookAt*
    };

    // Payload for a_text and a_flags; text longer than kLookAtTextMax bytes is cut at a UTF-8
    // character boundary.
    inline std::vector<std::uint8_t> Encode(std::string_view a_text, std::uint32_t a_flags = 0)
    {
        std::size_t length = a_text.size() < Protocol::kLookAtTextMax ? a_text.size() : Protocol::kLookAtTextMax;
        while (length > 0 && length < a_text.size() && (static_cast<std::uint8_t>(a_text[length]) & 0xC0) == 0x80) {
            length--;
        }
        Protocol::LookAtText header{};
        header.length = static_cast<std::uint32_t>(length);
        header.flags = a_flags;
        std::vector<std::uint8_t> payload(sizeof(header) + length);
        std::memcpy(payload.data(), &header, sizeof(header));
        std::memcpy(payload.data() + sizeof(header), a_text.data(), length);
        return payload;
    }

    // The prompt in a payload, nothing when it's malformed.
    inline std::optional<Prompt> Decode(const std::uint8_t* a_payload, std::uint32_t a_size)
    {
        Protocol::LookAtText header{};
        if (!a_payload || a_size < sizeof(header)) {
            return std::nullopt;
        }
        std::memcpy(&header, a_payload, sizeof(header));
        if (header.length > Protocol::kLookAtTextMax || sizeof(header) + header.length > a_size) {
            return std::nullopt;
        }
        return Prompt{ std::string_view(reinterpret_cast<const char*>(a_payload + sizeof(header)), header.length), header.flags };
    }
}
