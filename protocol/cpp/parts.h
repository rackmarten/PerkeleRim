// The PartState message (MSC -> Skyrim, v58): the Satsuma's parts as MSC's own database has them, fitted or not
// and how tight their bolts are. Header-only, mirrored by msc/src/Link/PartReport.cs; the protocol tests cover both.
// Skyrim keeps the latest state per part (Table) for the bolt-state probe (pr.part) and its log.

#pragma once

#include <algorithm>
#include <cctype>
#include <cstdint>
#include <cstring>
#include <map>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Parts
{
    struct Part
    {
        std::string   name;            // MSC's database entry: Door_Left, Radiator, WheelFL
        std::uint32_t flags = 0;       // kPart*
        std::uint32_t generation = 0;  // anchor the position is for
        float         position[3]{};   // Skyrim world units, with kPartPlaced
        std::uint16_t bolts = 0;
        std::uint16_t boltsTight = 0;
        std::uint16_t stages = 0;
        float         tightness = 0.0f;
        float         tightnessFull = 0.0f;
        std::uint32_t object = 0;      // the part's object as AttachedPart.part has it, 0 = none

        bool Installed() const { return (flags & Protocol::kPartInstalled) != 0; }
        bool Bolted() const { return (flags & Protocol::kPartBolted) != 0; }
        bool Placed() const { return (flags & Protocol::kPartPlaced) != 0; }
        // Every bolt screwed all the way in (MSC's own Bolted can come a little before that).
        bool AllTight() const { return bolts > 0 && boltsTight == bolts; }
        // No bolt in at all: what MSC asks before a part comes off.
        bool BoltsOut() const { return stages == 0; }
    };

    // Payload for a part; a name longer than kPartNameMax bytes is cut at a UTF-8 character boundary.
    inline std::vector<std::uint8_t> Encode(const Part& a_part)
    {
        Protocol::PartState m{};
        std::memcpy(m.position, a_part.position, sizeof(m.position));
        m.flags = a_part.flags;
        m.generation = a_part.generation;
        m.bolts = a_part.bolts;
        m.boltsTight = a_part.boltsTight;
        m.stages = a_part.stages;
        m.tightness = a_part.tightness;
        m.tightnessFull = a_part.tightnessFull;
        m.object = a_part.object;
        std::size_t length = std::min<std::size_t>(a_part.name.size(), Protocol::kPartNameMax);
        while (length > 0 && length < a_part.name.size() && (static_cast<std::uint8_t>(a_part.name[length]) & 0xC0) == 0x80) {
            length--;
        }
        m.length = static_cast<std::uint32_t>(length);
        std::memcpy(m.name, a_part.name.data(), length);
        std::vector<std::uint8_t> payload(sizeof(m));
        std::memcpy(payload.data(), &m, sizeof(m));
        return payload;
    }

    // The part in a payload, nothing when it's malformed (short, no name, a name past its field, bolts tighter
    // than there are bolts or a stage count past 8 a bolt).
    inline std::optional<Part> Decode(const std::uint8_t* a_payload, std::uint32_t a_size)
    {
        Protocol::PartState m{};
        if (!a_payload || a_size < sizeof(m)) {
            return std::nullopt;
        }
        std::memcpy(&m, a_payload, sizeof(m));
        if (m.length == 0 || m.length > Protocol::kPartNameMax || m.boltsTight > m.bolts || m.stages > 8u * m.bolts) {
            return std::nullopt;
        }
        Part p;
        p.name.assign(m.name, m.length);
        p.flags = m.flags;
        p.generation = m.generation;
        std::memcpy(p.position, m.position, sizeof(p.position));
        p.bolts = m.bolts;
        p.boltsTight = m.boltsTight;
        p.stages = m.stages;
        p.tightness = m.tightness;
        p.tightnessFull = m.tightnessFull;
        p.object = m.object;
        return p;
    }

    // A name as a test or a person types it: letters and digits only, lower case, so "door left", "door_left" and
    // "Door_Left" are the same part.
    inline std::string Key(std::string_view a_name)
    {
        std::string key;
        for (const char c : a_name) {
            const auto u = static_cast<unsigned char>(c);
            if (std::isalnum(u) || u >= 0x80) {
                key.push_back(static_cast<char>(std::tolower(u)));
            }
        }
        return key;
    }

    // The latest state of each part MSC reported.
    class Table
    {
    public:
        // Takes a part's new state; kPartGone forgets it. False when nothing about it changed but its position.
        bool Apply(const Part& a_part)
        {
            const auto key = Key(a_part.name);
            if ((a_part.flags & Protocol::kPartGone) != 0) {
                return _parts.erase(key) > 0;
            }
            auto       it = _parts.find(key);
            const bool changed = it == _parts.end() || it->second.flags != a_part.flags || it->second.bolts != a_part.bolts ||
                                 it->second.boltsTight != a_part.boltsTight || it->second.stages != a_part.stages;
            _parts[key] = a_part;
            return changed;
        }

        // By any spelling of its name (Key), else the only part whose name starts with it ("door l"), else nothing.
        const Part* Find(std::string_view a_name) const
        {
            const auto key = Key(a_name);
            if (key.empty()) {
                return nullptr;
            }
            if (const auto it = _parts.find(key); it != _parts.end()) {
                return &it->second;
            }
            const Part* only = nullptr;
            for (const auto& [k, part] : _parts) {
                if (k.starts_with(key)) {
                    if (only) {
                        return nullptr;  // more than one: say which
                    }
                    only = &part;
                }
            }
            return only;
        }

        std::size_t Size() const { return _parts.size(); }
        std::size_t Installed() const
        {
            return static_cast<std::size_t>(std::count_if(_parts.begin(), _parts.end(), [](const auto& a_kv) { return a_kv.second.Installed(); }));
        }
        void Clear() { _parts.clear(); }
        const std::map<std::string, Part>& All() const { return _parts; }

    private:
        std::map<std::string, Part> _parts;  // by Key
    };
}
