#include "fuel/Fuel.h"

#include "link/LinkService.h"

#include "fuel_hint.h"

namespace PerkeleRim::Fuel
{
    namespace
    {
        constexpr auto       kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID kPumpList = 0x980;    // FormList of the pump spot markers, assigned by tools/esp-gen
        constexpr RE::FormID kMarkerList = 0x981;  // and of their map markers, in the same order

        constexpr std::uint32_t kRecord = 'FUEL';
        constexpr std::uint32_t kVersion = 1;

        struct State
        {
            Protocol::FuelStations      message{};
            std::vector<FuelHint::Pump> hints;  // the same pumps with their map markers' names
            bool                        linkUp = false;
            bool                        sent = false;
            bool                        tipped = false;  // this save had the tow tip
            std::string                 lastLine;        // what Line made last, for pr.fuel
            std::uint32_t               lastFlags = 0;
        } state;

        // The map markers' names ("Whiterun Dwemer Oil") in the pumps' order; empty with an ESP
        // from before the marker list.
        std::vector<std::string> MarkerNames(RE::TESDataHandler* a_data)
        {
            std::vector<std::string> names;
            auto* list = a_data->LookupForm<RE::BGSListForm>(kMarkerList, kPlugin);
            if (!list) {
                return names;
            }
            list->ForEachForm([&](RE::TESForm* a_form) {
                auto*       ref = a_form ? a_form->As<RE::TESObjectREFR>() : nullptr;
                const auto* extra = ref ? ref->extraList.GetByType<RE::ExtraMapMarker>() : nullptr;
                const char* name = extra && extra->mapData ? extra->mapData->locationName.GetFullName() : nullptr;
                names.emplace_back(name ? name : "");
                return RE::BSContainer::ForEachResult::kContinue;
            });
            return names;
        }
    }

    void OnDataLoaded()
    {
        state.message = {};
        state.hints.clear();
        auto* data = RE::TESDataHandler::GetSingleton();
        auto* list = data ? data->LookupForm<RE::BGSListForm>(kPumpList, kPlugin) : nullptr;
        if (!list) {
            SKSE::log::info("fuel: {} has no pump list, no gas stations (rebuild it with scripts/build-esp.ps1)", kPlugin);
            return;
        }
        list->ForEachForm([](RE::TESForm* a_form) {
            auto* ref = a_form ? a_form->As<RE::TESObjectREFR>() : nullptr;
            if (ref && state.message.count < Protocol::kFuelPumpsMax) {
                auto& pump = state.message.pumps[state.message.count++];
                const auto at = ref->GetPosition();
                pump.position[0] = at.x;
                pump.position[1] = at.y;
                pump.position[2] = at.z;
                pump.heading = ref->GetAngleZ();
            }
            return RE::BSContainer::ForEachResult::kContinue;
        });
        const auto  names = MarkerNames(data);
        std::size_t named = 0;
        for (std::uint32_t i = 0; i < state.message.count; ++i) {
            const auto& pump = state.message.pumps[i];
            state.hints.push_back({ pump.position[0], pump.position[1], i < names.size() ? names[i] : std::string() });
            named += state.hints.back().name.empty() ? 0 : 1;
        }
        SKSE::log::info("fuel: {} pumps, {} with a map marker name", state.message.count, named);
    }

    void Update()
    {
        const bool up = LinkService::Get().IsUp();
        if (up != state.linkUp) {
            state.linkUp = up;
            state.sent = false;  // a new MSC, or the same one back: it gets the pumps again
        }
        if (!up || state.sent || state.message.count == 0) {
            return;
        }
        if (LinkService::Get().Send(Protocol::MessageType::FuelStations, &state.message, sizeof(state.message))) {
            state.sent = true;
            SKSE::log::info("fuel: {} pumps sent to MSC", state.message.count);
        }
    }

    std::string Line(const std::string& a_msc, std::uint32_t a_flags, const RE::NiPoint3& a_player)
    {
        if ((a_flags & Protocol::kFuelTextDry) != 0 && !state.tipped) {
            state.tipped = true;
            RE::SendHUDMessage::ShowHUDMessage(std::string(FuelHint::kDryTip).c_str());
            SKSE::log::info("fuel: the tank ran dry, tow tip shown (once a save)");
        }
        state.lastFlags = a_flags;
        state.lastLine = (a_flags & Protocol::kFuelTextLow) == 0 ? a_msc : FuelHint::LowLine(a_msc, FuelHint::Where(state.hints, a_player.x, a_player.y));
        return state.lastLine;
    }

    const std::string& LastLine() { return state.lastLine; }
    std::uint32_t      LastFlags() { return state.lastFlags; }
    bool               Tipped() { return state.tipped; }

    void Save(SKSE::SerializationInterface* a_intfc)
    {
        if (!a_intfc->OpenRecord(kRecord, kVersion) || !a_intfc->WriteRecordData(state.tipped)) {
            SKSE::log::warn("fuel: could not save the tow tip flag");
        }
    }

    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version)
    {
        if (a_type != kRecord) {
            return false;
        }
        if (a_version >= 1) {
            a_intfc->ReadRecordData(state.tipped);
        }
        SKSE::log::info("fuel: save loaded, tow tip {}", state.tipped ? "already shown" : "not shown yet");
        return true;
    }

    // New game, or before a save loads: a save without the record hasn't had the tip.
    void Revert()
    {
        state.tipped = false;
        Clear();
    }

    void Clear()
    {
        state.lastLine.clear();
        state.lastFlags = 0;
    }
}
