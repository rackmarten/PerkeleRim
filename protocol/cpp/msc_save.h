// The MscSave and MscLoad messages (Skyrim -> MSC): a snapshot key and the folder MSC keeps its
// snapshot in for that Skyrim save. Header-only, mirrored by msc/src/Link/Snapshot.cs; the
// protocol tests cover both directions. Prune is Skyrim's half of the folder housekeeping.

#pragma once

#include <chrono>
#include <cstdint>
#include <cstring>
#include <filesystem>
#include <optional>
#include <set>
#include <string>
#include <string_view>
#include <vector>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::MscSave
{
    struct Snapshot
    {
        std::uint64_t    key;
        std::string_view path;  // UTF-8, points into the payload
    };

    // Payload for a_key and a_path (UTF-8); nothing when the path is longer than kMscSnapshotPathMax
    // bytes or the key is 0.
    inline std::optional<std::vector<std::uint8_t>> Encode(std::uint64_t a_key, std::string_view a_path)
    {
        if (a_key == 0 || a_path.size() > Protocol::kMscSnapshotPathMax) {
            return std::nullopt;
        }
        Protocol::MscSnapshot header{};
        header.key = a_key;
        header.length = static_cast<std::uint32_t>(a_path.size());
        std::vector<std::uint8_t> payload(sizeof(header) + a_path.size());
        std::memcpy(payload.data(), &header, sizeof(header));
        std::memcpy(payload.data() + sizeof(header), a_path.data(), a_path.size());
        return payload;
    }

    // The snapshot in a payload, nothing when it's malformed.
    inline std::optional<Snapshot> Decode(const std::uint8_t* a_payload, std::uint32_t a_size)
    {
        Protocol::MscSnapshot header{};
        if (!a_payload || a_size < sizeof(header)) {
            return std::nullopt;
        }
        std::memcpy(&header, a_payload, sizeof(header));
        if (header.key == 0 || header.length > Protocol::kMscSnapshotPathMax || sizeof(header) + header.length > a_size) {
            return std::nullopt;
        }
        return Snapshot{ header.key, std::string_view(reinterpret_cast<const char*>(a_payload + sizeof(header)), header.length) };
    }

    // The names of every save (<name>.ess) anywhere under a_saves: save managers keep them in
    // subfolders per character. a_skip (the snapshots) isn't searched.
    inline std::set<std::string> SaveNames(const std::filesystem::path& a_saves, const std::filesystem::path& a_skip)
    {
        std::set<std::string> names;
        std::error_code       error;
        const auto            options = std::filesystem::directory_options::skip_permission_denied;
        for (auto it = std::filesystem::recursive_directory_iterator(a_saves, options, error);
             !error && it != std::filesystem::recursive_directory_iterator(); it.increment(error)) {
            std::error_code ignored;
            if (it->is_directory(ignored)) {
                if (std::filesystem::equivalent(it->path(), a_skip, ignored)) {
                    it.disable_recursion_pending();
                }
            } else if (it->path().extension() == ".ess") {
                names.insert(it->path().stem().string());
            }
        }
        return names;
    }

    // Snapshots live in a_snapshots/<save name>, for a <save name>.ess anywhere under a_saves.
    // Removes every snapshot folder whose save is gone, except a_keep (the save being written right
    // now, whose .ess may not exist yet) and folders changed in the last a_grace (MSC may still be
    // writing one). Returns the names removed.
    inline std::vector<std::string> Prune(const std::filesystem::path& a_saves, const std::filesystem::path& a_snapshots,
        std::string_view a_keep, std::chrono::seconds a_grace = std::chrono::minutes(5))
    {
        std::vector<std::string> removed;
        std::error_code          error;
        const auto               now = std::filesystem::file_time_type::clock::now();
        if (!std::filesystem::is_directory(a_snapshots, error)) {
            return removed;
        }
        const auto saves = SaveNames(a_saves, a_snapshots);
        for (const auto& entry : std::filesystem::directory_iterator(a_snapshots, error)) {
            if (!entry.is_directory(error)) {
                continue;
            }
            const auto name = entry.path().filename().string();
            if (name == a_keep || saves.contains(name)) {
                continue;
            }
            const auto changed = entry.last_write_time(error);
            if (error || now - changed < a_grace) {
                error.clear();
                continue;
            }
            if (std::filesystem::remove_all(entry.path(), error) != static_cast<std::uintmax_t>(-1) && !error) {
                removed.push_back(name);
            }
            error.clear();
        }
        return removed;
    }
}
