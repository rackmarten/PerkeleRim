#include "intro/Intro.h"

#include "hud/Hud.h"
#include "intro_cues.h"
#include "link/LinkService.h"

namespace PerkeleRim::Intro
{
    namespace
    {
        // Skyrim's own fade up from black (the cart at the start of a new game), about 8 s. Started
        // again with the blackout, so the opening stays black while Kumpo passes out.
        constexpr auto       kFadePlugin = "Skyrim.esm"sv;
        constexpr RE::FormID kFade = 0x0591A3;  // MQ101FadeUpFromBlackImod

        struct State
        {
            RE::TESQuest*              unbound = nullptr;
            RE::TESImageSpaceModifier* fade = nullptr;
            Cues                       cues;
            std::string                caption;      // shown while Skyrim has the player
            float                      captionLeft = 0.0f;  // seconds
            std::atomic<bool>          ralofDone = false;  // set by the topic sink
        } state;

        class TopicSink : public RE::BSTEventSink<RE::TESTopicInfoEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::TESTopicInfoEvent* a_event, RE::BSTEventSource<RE::TESTopicInfoEvent>*) override
            {
                if (a_event && a_event->topicInfoFormID == kRalofFinallyAwake &&
                    a_event->type == RE::TESTopicInfoEvent::TopicInfoEventType::kTopicEnd) {
                    state.ralofDone = true;
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };

        void Send(std::uint32_t a_cue)
        {
            const Protocol::IntroCue cue{ a_cue, 0 };
            if (!LinkService::Get().Send(Protocol::MessageType::IntroCue, &cue, sizeof(cue))) {
                SKSE::log::warn("intro: could not send cue {} to MSC", a_cue);
                return;
            }
            if (a_cue == Protocol::kIntroBlackout && state.fade) {
                RE::ImageSpaceModifierInstanceForm::Trigger(state.fade, 1.0f, nullptr);
            }
            state.caption = Caption(a_cue);
            state.captionLeft = CaptionSeconds(a_cue);
            SKSE::log::info("intro: {}", a_cue == Protocol::kIntroBlackout ? "Kumpo passes out (MSC plays it)" : "Kumpo answers Ralof (MSC plays it)");
        }
    }

    void OnDataLoaded()
    {
        static TopicSink topics;
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESTopicInfoEvent>(&topics);
        }
        state.unbound = RE::TESForm::LookupByID<RE::TESQuest>(kUnbound);
        auto* data = RE::TESDataHandler::GetSingleton();
        state.fade = data ? data->LookupForm<RE::TESImageSpaceModifier>(kFade, kFadePlugin) : nullptr;
        SKSE::log::info("intro: Unbound {}, fade {}", state.unbound ? "found" : "missing", state.fade ? "found" : "missing");
    }

    void OnNewGame()
    {
        state.cues.NewGame();
        state.ralofDone = false;
        state.captionLeft = 0.0f;
        SKSE::log::info("intro: new game, Kumpo passes out once MSC is in its game");
    }

    void OnGameLoaded()
    {
        state.cues.Loaded();
        state.ralofDone = false;
        state.captionLeft = 0.0f;
    }

    bool Running()
    {
        // IsRunning() is only "not stopping", true for a quest that never started (a coc start, a
        // mod that skips Helgen); enabled is what runs.
        return state.unbound && state.unbound->IsEnabled() && !state.unbound->IsCompleted();
    }

    void Tick(float a_delta, bool a_msc, bool a_owned)
    {
        if (state.ralofDone.exchange(false)) {
            SKSE::log::info("intro: Ralof's first line in the cart ended");
            state.cues.OnTopicEnd(kRalofFinallyAwake);
        }
        if (const auto cue = state.cues.Tick(a_delta, a_msc && Running())) {  // no cart (a mod that skips it): no cues
            Send(cue);
        }
        if (a_owned) {
            state.captionLeft = 0.0f;  // the handover's HUD has the subtitle line now
            return;
        }
        Hud::View view;
        bool      refresh = false;
        if (state.captionLeft > 0.0f) {
            const float before = state.captionLeft;
            state.captionLeft -= a_delta;
            refresh = std::floor(before) != std::floor(state.captionLeft);  // once a second, in case the HUD movie wasn't up yet
            view.show = true;
            view.dot = false;
            view.subtitle = state.caption;
        }
        Hud::Set(view, refresh);
    }
}
