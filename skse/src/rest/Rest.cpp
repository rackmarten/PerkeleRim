#include "rest/Rest.h"

#include "link/LinkService.h"
#include "player/PlayerSync.h"
#include "time_skip.h"

namespace PerkeleRim::Rest
{
    namespace
    {
        SkipTracker tracker;

        double HoursPassed()
        {
            auto* calendar = RE::Calendar::GetSingleton();
            return calendar ? static_cast<double>(calendar->GetHoursPassed()) : 0.0;
        }

        // Lying in a bed, or on the way into one: the Sleep menu comes up from there.
        bool InBed(RE::PlayerCharacter* a_player)
        {
            if (!a_player) {
                return false;
            }
            switch (a_player->AsActorState()->GetSitSleepState()) {
            case RE::SIT_SLEEP_STATE::kWantToSleep:
            case RE::SIT_SLEEP_STATE::kWaitingForSleepAnim:
            case RE::SIT_SLEEP_STATE::kIsSleeping:
                return true;
            default:
                return false;
            }
        }

        class MenuSink : public RE::BSTEventSink<RE::MenuOpenCloseEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::MenuOpenCloseEvent* a_event, RE::BSTEventSource<RE::MenuOpenCloseEvent>*) override
            {
                if (a_event && a_event->menuName == RE::SleepWaitMenu::MENU_NAME && PlayerSync::Owned()) {
                    if (a_event->opening) {
                        tracker.Open(HoursPassed(), InBed(RE::PlayerCharacter::GetSingleton()));
                    } else {
                        tracker.Close(HoursPassed());
                    }
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void OnDataLoaded()
    {
        static MenuSink sink;
        if (auto* ui = RE::UI::GetSingleton()) {
            ui->AddEventSink<RE::MenuOpenCloseEvent>(&sink);
        }
    }

    void OnFrame(RE::PlayerCharacter* a_player, float a_delta)
    {
        if (!tracker.Armed() || a_delta <= 0.0f) {
            return;
        }
        auto* calendar = RE::Calendar::GetSingleton();
        if (!calendar) {
            return;
        }
        bool         slept = false;
        const double hours = tracker.Frame(HoursPassed(), a_delta, static_cast<double>(calendar->GetTimescale()), InBed(a_player), slept);
        if (hours <= 0.0) {
            return;
        }
        Protocol::TimePassed message{};
        message.hours = static_cast<float>(hours);
        message.flags = slept ? Protocol::kTimeBed : 0;
        auto& link = LinkService::Get();
        if (link.IsUp() && link.Send(Protocol::MessageType::TimePassed, &message, sizeof(message))) {
            SKSE::log::info("rest: {:.2f} game hours {} sent to MSC", hours, slept ? "slept in a bed" : "waited");
        } else {
            SKSE::log::warn("rest: {:.2f} game hours {} not sent (MSC isn't linked)", hours, slept ? "slept" : "waited");
        }
    }

    void OnGameLoad()
    {
        tracker.Reset();
        auto& link = LinkService::Get();
        const std::uint8_t none = 0;
        if (link.IsUp() && link.Send(Protocol::MessageType::LoadReset, &none, 0)) {
            SKSE::log::info("rest: a save is loading, MSC stops what its player was doing");
        }
    }
}
