#include "garage/Garage.h"

namespace PerkeleRim::Garage
{
    namespace
    {
        constexpr auto kPlugin = "PerkeleRim.esp"sv;
        // Fixed local form ids, assigned by tools/esp-gen.
        constexpr RE::FormID kSpawnMarker = 0x800;
        constexpr RE::FormID kVanMarker = 0x801;

        constexpr std::uint32_t kVisitedRecord = 'GRGE';
        constexpr std::uint32_t kVisitedVersion = 1;

        struct State
        {
            const RE::TESFile*  file = nullptr;
            RE::TESObjectREFR*  spawn = nullptr;
            RE::TESObjectREFR*  van = nullptr;
            bool                visited = false;  // this save's first handover already went to the garage
        } state;
    }

    void Save(SKSE::SerializationInterface* a_intfc)
    {
        if (!a_intfc->OpenRecord(kVisitedRecord, kVisitedVersion) || !a_intfc->WriteRecordData(state.visited)) {
            SKSE::log::warn("garage: could not save the visited flag");
        }
    }

    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version)
    {
        if (a_type != kVisitedRecord) {
            return false;
        }
        if (a_version == kVisitedVersion) {
            a_intfc->ReadRecordData(state.visited);
        }
        SKSE::log::info("garage: save loaded, {}", state.visited ? "already been to the garage" : "first handover goes to the garage");
        return true;
    }

    // New game, or before a save loads: a save without our record hasn't been there.
    void Revert()
    {
        state.visited = false;
    }

    void OnDataLoaded()
    {
        auto* data = RE::TESDataHandler::GetSingleton();
        state.file = data ? data->LookupLoadedModByName(kPlugin) : nullptr;
        if (!state.file) {
            SKSE::log::info("garage: {} isn't loaded, handovers start where the player stands", kPlugin);
            return;
        }
        state.spawn = data->LookupForm<RE::TESObjectREFR>(kSpawnMarker, kPlugin);
        state.van = data->LookupForm<RE::TESObjectREFR>(kVanMarker, kPlugin);
        if (!state.spawn || !state.van) {
            SKSE::log::warn("garage: {} has no spawn or van marker, rebuild it with scripts/build-esp.ps1", kPlugin);
            state.spawn = state.van = nullptr;
            return;
        }
        const auto at = state.spawn->GetPosition();
        SKSE::log::info("garage: spawn marker {:08X} at ({:.0f},{:.0f},{:.0f})", state.spawn->GetFormID(), at.x, at.y, at.z);
    }

    RE::TESObjectREFR* FirstVisit()
    {
        return state.visited ? nullptr : state.spawn;
    }

    void Visited()
    {
        state.visited = true;
    }

    std::optional<Spot> Van()
    {
        if (!state.van) {
            return std::nullopt;
        }
        return Spot{ state.van->GetPosition(), state.van->GetAngleZ() };
    }

    bool Owns(const RE::TESObjectREFR* a_ref)
    {
        return state.file && a_ref && a_ref->GetFile(0) == state.file;
    }
}
