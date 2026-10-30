#include "garage/Garage.h"

namespace PerkeleRim::Garage
{
    namespace
    {
        constexpr auto kPlugin = "PerkeleRim.esp"sv;
        // Fixed local form ids, assigned by tools/esp-gen.
        constexpr RE::FormID kSpawnMarker = 0x800;
        constexpr RE::FormID kVanMarker = 0x801;
        constexpr RE::FormID kWakeMarker = 0x80B;
        constexpr RE::FormID kShelfSpots = 0x910;  // FormList of the shelf spot markers

        // Skyrim's own fade up from black (the cart at the start of a new game), about 8 s.
        constexpr auto       kFadePlugin = "Skyrim.esm"sv;
        constexpr RE::FormID kWakeFade = 0x0591A3;  // MQ101FadeUpFromBlackImod

        // A handover this close to the van's spot stocks the shelves (units; the island reaches
        // about this far once its first chunks are in).
        constexpr float kStockRadius = 2048.0f;

        constexpr std::uint32_t kRecord = 'GRGE';
        constexpr std::uint32_t kVersion = 2;  // 1 had only the visited flag

        struct State
        {
            const RE::TESFile*        file = nullptr;
            RE::TESObjectREFR*        spawn = nullptr;
            RE::TESObjectREFR*        van = nullptr;
            RE::TESObjectREFR*        wake = nullptr;  // null with an ESP from before the shack
            RE::TESImageSpaceModifier* fade = nullptr;
            std::vector<Spot>         shelves;
            bool                      visited = false;  // this save's first handover already went to the garage
            bool                      stocked = false;  // this save's shelves got MSC's items
            bool                      skipped = false;  // this new game skipped Helgen to the spawn marker (not saved)
        } state;
    }

    void Save(SKSE::SerializationInterface* a_intfc)
    {
        if (!a_intfc->OpenRecord(kRecord, kVersion) || !a_intfc->WriteRecordData(state.visited) ||
            !a_intfc->WriteRecordData(state.stocked)) {
            SKSE::log::warn("garage: could not save the visited and stocked flags");
        }
    }

    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version)
    {
        if (a_type != kRecord) {
            return false;
        }
        if (a_version >= 1) {
            a_intfc->ReadRecordData(state.visited);
        }
        if (a_version >= 2) {
            a_intfc->ReadRecordData(state.stocked);
        }
        SKSE::log::info("garage: save loaded, {}, shelves {}", state.visited ? "already been to the garage" : "first handover goes to the garage",
            state.stocked ? "stocked" : "not stocked yet");
        return true;
    }

    // New game, or before a save loads: a save without our record hasn't been there.
    void Revert()
    {
        state.visited = false;
        state.stocked = false;
        state.skipped = false;
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

        state.wake = data->LookupForm<RE::TESObjectREFR>(kWakeMarker, kPlugin);
        state.fade = data->LookupForm<RE::TESImageSpaceModifier>(kWakeFade, kFadePlugin);
        state.shelves.clear();
        if (auto* list = data->LookupForm<RE::BGSListForm>(kShelfSpots, kPlugin)) {
            list->ForEachForm([](RE::TESForm* a_form) {
                if (auto* ref = a_form ? a_form->As<RE::TESObjectREFR>() : nullptr) {
                    state.shelves.push_back({ ref->GetPosition(), ref->GetAngleZ() });
                }
                return RE::BSContainer::ForEachResult::kContinue;
            });
        }
        if (!state.wake || state.shelves.empty()) {
            SKSE::log::warn("garage: {} has no shack or shelf spots, rebuild it with scripts/build-esp.ps1", kPlugin);
        }
        SKSE::log::info("garage: wake-up marker {}, {} shelf spots, fade {}", state.wake ? "found" : "missing", state.shelves.size(),
            state.fade ? "found" : "missing");
    }

    RE::TESObjectREFR* Spawn()
    {
        return state.spawn;
    }

    RE::TESObjectREFR* FirstVisit()
    {
        if (state.visited) {
            return nullptr;
        }
        return state.wake && !state.skipped ? state.wake : state.spawn;
    }

    void Visited()
    {
        state.visited = true;
    }

    void CartStart()
    {
        if (!state.visited && !state.skipped) {
            state.visited = true;
            SKSE::log::info("garage: this save started in the Helgen cart, its first handover starts where Kumpo stands");
        }
    }

    // Unbound is still running for a moment after the skip (Papyrus completes it): CartStart stays
    // out of it from here.
    void SkippedHelgen()
    {
        state.skipped = true;
        state.visited = false;
        SKSE::log::info("garage: Helgen skipped, the first handover starts at the spawn marker with the van on the pit");
    }

    bool WakeUp(RE::TESObjectREFR* a_marker)
    {
        if (!a_marker || a_marker != state.wake || !state.fade) {
            return false;
        }
        RE::ImageSpaceModifierInstanceForm::Trigger(state.fade, 1.0f, nullptr);
        SKSE::log::info("garage: waking up in the shack");
        return true;
    }

    std::optional<Spot> Van()
    {
        if (!state.van) {
            return std::nullopt;
        }
        return Spot{ state.van->GetPosition(), state.van->GetAngleZ() };
    }

    bool StockDue(const RE::NiPoint3& a_player)
    {
        return !state.stocked && !state.shelves.empty() && state.van &&
               a_player.GetDistance(state.van->GetPosition()) < kStockRadius;
    }

    const std::vector<Spot>& ShelfSpots()
    {
        return state.shelves;
    }

    void Stocked()
    {
        state.stocked = true;
    }
}
