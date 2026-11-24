#include "garage/GarageBoard.h"

#include "garage/Garage.h"
#include "link/LinkService.h"
#include "player/PlayerSync.h"

namespace PerkeleRim::GarageBoard
{
    namespace
    {
        constexpr auto       kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID kBoard = 0xD01;  // the board's reference (tools/esp-gen)

        constexpr auto kQuestion = "The garage board. What should come home?";
        constexpr auto kVehicles = "Bring all vehicles home";
        constexpr auto kParts = "Bring loose car parts home";
        constexpr auto kNeither = "Leave it";

        RE::TESObjectREFR* board = nullptr;

        void Answered(std::uint8_t a_button)
        {
            if (a_button > 1) {
                return;
            }
            if (!PlayerSync::Owned() || !LinkService::Get().IsUp()) {
                RE::SendHUDMessage::ShowHUDMessage("Only with My Summer Car's Kumpo: it brings the things home.");
                return;
            }
            const auto what = a_button == 0 ? Protocol::kBayVehiclesHome : Protocol::kBayPartsHome;
            if (!Garage::RequestBay(what, PlayerSync::Generation(), "the garage board")) {
                RE::SendHUDMessage::ShowHUDMessage("The garage board didn't reach My Summer Car. Try again.");
            }
        }

        class ActivateSink : public RE::BSTEventSink<RE::TESActivateEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::TESActivateEvent* a_event, RE::BSTEventSource<RE::TESActivateEvent>*) override
            {
                if (a_event && board && a_event->objectActivated.get() == board && a_event->actionRef &&
                    a_event->actionRef->IsPlayerRef()) {
                    SKSE::log::info("garage board: used");
                    Open();
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void OnDataLoaded()
    {
        auto* data = RE::TESDataHandler::GetSingleton();
        board = data ? data->LookupForm<RE::TESObjectREFR>(kBoard, kPlugin) : nullptr;
        SKSE::log::info("garage board: {}", board ? "found" : "missing (an ESP from before the Satsuma bay)");
        if (!board) {
            return;
        }
        static ActivateSink sink;
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESActivateEvent>(&sink);
        }
    }

    void Open()
    {
        RE::MessageBoxMenu::Create(kQuestion, &Answered, 0, 4, 3, kVehicles, kParts, kNeither);
    }

    bool Choose(std::int32_t a_button)
    {
        auto* box = RE::MessageBoxMenu::GetCurrentMessageBoxData();
        if (!box || std::string_view(box->bodyText.c_str()) != kQuestion) {
            return false;
        }
        RE::MessageBoxMenu::SelectOption(a_button);
        return true;
    }
}
