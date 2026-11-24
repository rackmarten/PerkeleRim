#include "garage/Garage.h"

#include "link/LinkService.h"

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
        constexpr RE::FormID kBayMarker = 0xD00;   // the Satsuma bay's (tools/esp-gen, SatsumaBay.cs)

        // MSC has Kumpo this close to the bay marker: the move (units; 30 m, well inside the island).
        constexpr float  kBayRadius = 2100.0f;
        constexpr double kBayRetry = 30.0;  // seconds before a move MSC couldn't do is asked again

        // Skyrim's own fade up from black (the cart at the start of a new game), about 8 s.
        constexpr auto       kFadePlugin = "Skyrim.esm"sv;
        constexpr RE::FormID kWakeFade = 0x0591A3;  // MQ101FadeUpFromBlackImod

        // A handover this close to the van's spot stocks the shelves (units; the island reaches
        // about this far once its first chunks are in).
        constexpr float kStockRadius = 2048.0f;

        constexpr std::uint32_t kRecord = 'GRGE';
        constexpr std::uint32_t kVersion = 3;  // 1 had only the visited flag, 2 the stocked one too

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
            RE::TESObjectREFR*        bay = nullptr;      // null with an ESP from before the Satsuma bay
            bool                      moved = false;      // this save had the Satsuma's move (saved)
            std::uint32_t             bayGeneration = 0;  // the anchor the bay was last asked for (not saved)
            double                    retryAt = 0.0;
            std::uint32_t             requests = 0;
            BayResult                 last;
        } state;

        double Now()
        {
            return std::chrono::duration<double>(std::chrono::steady_clock::now().time_since_epoch()).count();
        }
    }

    void Save(SKSE::SerializationInterface* a_intfc)
    {
        if (!a_intfc->OpenRecord(kRecord, kVersion) || !a_intfc->WriteRecordData(state.visited) ||
            !a_intfc->WriteRecordData(state.stocked) || !a_intfc->WriteRecordData(state.moved)) {
            SKSE::log::warn("garage: could not save the visited, stocked and Satsuma moved flags");
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
        if (a_version >= 3) {
            a_intfc->ReadRecordData(state.moved);
        }
        SKSE::log::info("garage: save loaded, {}, shelves {}, the Satsuma {}", state.visited ? "already been to the garage" : "first handover goes to the garage",
            state.stocked ? "stocked" : "not stocked yet", state.moved ? "in its bay" : "not moved over yet");
        return true;
    }

    // New game, or before a save loads: a save without our record hasn't been there.
    void Revert()
    {
        state.visited = false;
        state.stocked = false;
        state.skipped = false;
        state.moved = false;
        state.bayGeneration = 0;
        state.retryAt = 0.0;
        state.requests = 0;
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
        state.bay = data->LookupForm<RE::TESObjectREFR>(kBayMarker, kPlugin);
        SKSE::log::info("garage: Satsuma bay marker {}", state.bay ? "found" : "missing (an ESP from before 0.3: no Satsuma bay)");
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

    std::optional<Spot> Bay()
    {
        if (!state.bay) {
            return std::nullopt;
        }
        return Spot{ state.bay->GetPosition(), state.bay->GetAngleZ() };
    }

    bool SatsumaMoved()
    {
        return state.moved;
    }

    bool RequestBay(std::uint32_t a_what, std::uint32_t a_generation, const char* a_why)
    {
        const auto bay = Bay();
        if (!bay) {
            return false;
        }
        Protocol::GarageBay message{};
        message.generation = a_generation;
        message.request = a_what;
        message.position[0] = bay->position.x;
        message.position[1] = bay->position.y;
        message.position[2] = bay->position.z;
        message.heading = bay->heading;
        if (!LinkService::Get().Send(Protocol::MessageType::GarageBay, &message, sizeof(message))) {
            SKSE::log::warn("garage: could not send the bay to MSC ({})", a_why);
            return false;
        }
        state.requests++;
        SKSE::log::info("garage: bay request {:X} to MSC for gen {} ({})", a_what, a_generation, a_why);
        return true;
    }

    void UpdateBay(RE::TESObjectREFR* a_player, bool a_owned, std::uint32_t a_generation)
    {
        if (!a_player || !a_owned || !state.bay || a_generation == 0 || a_generation == state.bayGeneration || Now() < state.retryAt) {
            return;
        }
        // Outside, in the bay's own worldspace: an interior's (or another worldspace's) coordinates can
        // fall within reach of the marker's too.
        const auto* cell = a_player->GetParentCell();
        if (!cell || cell->IsInteriorCell() || a_player->GetWorldspace() != state.bay->GetWorldspace() ||
            a_player->GetPosition().GetDistance(state.bay->GetPosition()) > kBayRadius) {
            return;
        }
        const auto what = state.moved ? Protocol::kBayStand : Protocol::kBayMoveSatsuma;
        if (RequestBay(what, a_generation, state.moved ? "near the bay" : "the Satsuma's move")) {
            state.bayGeneration = a_generation;
        }
    }

    void OnGarageDone(const std::uint8_t* a_data, std::uint32_t a_size)
    {
        if (!a_data || a_size < sizeof(Protocol::GarageDone)) {
            SKSE::log::warn("garage: bad GarageDone ({} bytes)", a_size);
            return;
        }
        std::memcpy(&state.last.done, a_data, sizeof(Protocol::GarageDone));
        state.last.count++;
        const auto& d = state.last.done;
        SKSE::log::info("garage: MSC did {:X} for gen {}: body {}, {} parts on their slots, {} on the floor, {} vehicles home, {} off their slots",
            d.request, d.generation, d.body ? "on its stand" : "not there", d.slotted, d.floor, d.vehicles, d.missed);
        if ((d.request & Protocol::kBayMoveSatsuma) != 0) {
            state.moved = true;
            SKSE::log::info("garage: the Satsuma moved over, this save remembers it");
        }
        // The garage board's answers, on Skyrim's HUD.
        if ((d.request & Protocol::kBayVehiclesHome) != 0) {
            RE::SendHUDMessage::ShowHUDMessage(std::format("{} {} home by the garage.", d.vehicles, d.vehicles == 1 ? "vehicle is" : "vehicles are").c_str());
        }
        if ((d.request & Protocol::kBayPartsHome) != 0) {
            RE::SendHUDMessage::ShowHUDMessage(std::format("{} car parts back on their own slots, {} elsewhere in the bay.", d.slotted, d.floor).c_str());
        }
        if (d.request == 0) {
            SKSE::log::info("garage: MSC couldn't do {:X} (no ground under the bay in time, or a new anchor)", d.asked);
            if ((d.asked & (Protocol::kBayVehiclesHome | Protocol::kBayPartsHome)) != 0) {
                RE::SendHUDMessage::ShowHUDMessage("The garage board couldn't bring them home: try again in a moment.");
            }
        }
        if (d.request == 0 && (d.asked & (Protocol::kBayMoveSatsuma | Protocol::kBayStand)) != 0) {
            state.bayGeneration = 0;  // ask again in a while
            state.retryAt = Now() + kBayRetry;
        }
    }

    const BayResult& LastBay()
    {
        return state.last;
    }

    std::uint32_t BayRequests()
    {
        return state.requests;
    }
}
