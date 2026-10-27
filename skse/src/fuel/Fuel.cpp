#include "fuel/Fuel.h"

#include "link/LinkService.h"

namespace PerkeleRim::Fuel
{
    namespace
    {
        constexpr auto       kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID kPumpList = 0x980;  // FormList of the pump spot markers, assigned by tools/esp-gen

        struct State
        {
            Protocol::FuelStations message{};
            bool                   linkUp = false;
            bool                   sent = false;
        } state;
    }

    void OnDataLoaded()
    {
        state.message = {};
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
        SKSE::log::info("fuel: {} pumps", state.message.count);
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
}
