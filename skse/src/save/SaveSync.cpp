#include "save/SaveSync.h"

#include <chrono>
#include <random>

#include "link/LinkService.h"
#include "msc_save.h"
#include "player/PlayerSync.h"

namespace PerkeleRim::SaveSync
{
    namespace
    {
        constexpr std::uint32_t kRecord = 'MSCS';
        constexpr std::uint32_t kVersion = 1;
        constexpr std::uint32_t kNameMax = 260;

        struct State
        {
            std::uint64_t key = 0;     // this game's MSC snapshot, 0 = none
            std::string   name;        // its save's name (the snapshot folder's)
            bool          owned = false;  // the save was taken while MSC had the player
        } state;

        // Documents/My Games/Skyrim Special Edition/Saves, like the game's own default.
        std::filesystem::path SavesFolder()
        {
            const auto logs = SKSE::log::log_directory();  // .../Skyrim Special Edition/SKSE
            return logs ? logs->parent_path() / "Saves" : std::filesystem::path();
        }

        std::filesystem::path SnapshotsFolder()
        {
            return SavesFolder() / "PerkeleRim";
        }

        std::string Utf8(const std::filesystem::path& a_path)
        {
            const auto text = a_path.u8string();
            return std::string(reinterpret_cast<const char*>(text.data()), text.size());
        }

        std::uint64_t NewKey()
        {
            static std::mt19937_64 random{ std::random_device{}() ^
                                           static_cast<std::uint64_t>(std::chrono::steady_clock::now().time_since_epoch().count()) };
            std::uint64_t key = 0;
            while (key == 0) {
                key = random();
            }
            return key;
        }

        bool Send(Protocol::MessageType a_type, std::uint64_t a_key, const std::filesystem::path& a_folder)
        {
            auto& link = LinkService::Get();
            const auto payload = MscSave::Encode(a_key, Utf8(a_folder));
            return payload && link.IsUp() && link.Send(a_type, payload->data(), static_cast<std::uint32_t>(payload->size()));
        }
    }

    void Save(SKSE::SerializationInterface* a_intfc)
    {
        if (state.key == 0 && !state.owned) {
            return;
        }
        const auto length = static_cast<std::uint32_t>(state.name.size());
        const std::uint8_t owned = state.owned ? 1 : 0;
        if (!a_intfc->OpenRecord(kRecord, kVersion) || !a_intfc->WriteRecordData(state.key) || !a_intfc->WriteRecordData(owned) ||
            !a_intfc->WriteRecordData(length) || !a_intfc->WriteRecordData(state.name.data(), length)) {
            SKSE::log::warn("save sync: could not write the MSC snapshot key");
        }
    }

    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version)
    {
        if (a_type != kRecord) {
            return false;
        }
        std::uint64_t key = 0;
        std::uint8_t  owned = 0;
        std::uint32_t length = 0;
        if (a_version != kVersion || !a_intfc->ReadRecordData(key) || !a_intfc->ReadRecordData(owned) ||
            !a_intfc->ReadRecordData(length) || length > kNameMax) {
            SKSE::log::warn("save sync: unreadable MSC snapshot record (v{})", a_version);
            return true;
        }
        std::string name(length, '\0');
        if (!a_intfc->ReadRecordData(name.data(), length)) {
            return true;
        }
        state.key = key;
        state.name = std::move(name);
        state.owned = owned != 0;
        return true;
    }

    void Revert()
    {
        state = {};
    }

    void OnSaveGame(const char* a_name)
    {
        const std::string name = a_name ? a_name : "";
        state.owned = PlayerSync::Owned();
        if (name.empty()) {
            state.key = 0;
            return;
        }
        const auto      snapshots = SnapshotsFolder();
        std::error_code error;
        std::filesystem::create_directories(snapshots, error);
        state.key = NewKey();
        state.name = name;
        if (!Send(Protocol::MessageType::MscSave, state.key, snapshots / name)) {
            SKSE::log::info("save sync: {} saved without MSC (not linked)", name);
            state.key = 0;
        } else {
            SKSE::log::info("save sync: {} saved, MSC snapshot key {:016X}", name, state.key);
        }
        for (const auto& removed : MscSave::Prune(SavesFolder(), snapshots, name)) {
            SKSE::log::info("save sync: removed the MSC snapshot of deleted save {}", removed);
        }
    }

    void OnGameLoaded()
    {
        if (state.owned) {
            PlayerSync::RestoreControls();  // a save keeps the controls handing over took
        }
        if (state.key == 0) {
            SKSE::log::info("save sync: this save has no MSC snapshot, MSC stays as it is");
        } else if (!Send(Protocol::MessageType::MscLoad, state.key, SnapshotsFolder() / state.name)) {
            SKSE::log::info("save sync: MSC isn't linked, it doesn't go back to {}", state.name);
        } else {
            SKSE::log::info("save sync: MSC goes back to {} (key {:016X})", state.name, state.key);
        }
        // Play mode hands over after this (PlayerSync::OnGameLoaded), so MSC has the snapshot when the anchor comes.
    }
}
