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
            std::uint32_t              captionCue = 0;  // the last cue sent, while its captions run (Captions)
            float                      captionSince = 0.0f;  // seconds since it went out
            bool                       captionShown = false;  // our HUD layer shows a caption (cleared once it's over)
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
            state.captionCue = a_cue;
            state.captionSince = 0.0f;
            SKSE::log::info("intro: {}", a_cue == Protocol::kIntroBlackout ? "Kumpo passes out, snores and wakes up hungover (MSC plays it)" :
                                         a_cue == Protocol::kIntroWake      ? "Kumpo says Perkele (MSC plays it)" :
                                                                              "Kumpo wakes up hungover (MSC plays it)");
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
        state.captionCue = 0;
        SKSE::log::info("intro: new game, Kumpo passes out once MSC is in its game");
    }

    void OnGameLoaded()
    {
        state.cues.Loaded();
        state.ralofDone = false;
        state.captionCue = 0;
    }

    void WakeUp()
    {
        Send(Protocol::kIntroHangover);
    }

    std::string_view Caption()
    {
        return state.captionCue ? CaptionAt(state.captionCue, state.captionSince) : std::string_view{};
    }

    std::string Describe()
    {
        if (!state.unbound) {
            return "no Unbound";
        }
        return fmt::format("Unbound stage {}, {}, {}", state.unbound->GetCurrentStageID(), state.unbound->IsEnabled() ? "enabled" : "not enabled",
            state.unbound->IsCompleted() ? "completed" : "not completed");
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
        const float before = state.captionSince;
        if (state.captionCue) {
            state.captionSince += a_delta;
            if (state.captionSince > CaptionsSeconds(state.captionCue)) {
                state.captionCue = 0;
            }
        }
        if (a_owned) {
            return;  // the handover's HUD shows Caption() as its subtitle line
        }
        const auto caption = Caption();
        if (caption.empty() && !state.captionShown) {
            return;  // nothing of ours on the HUD
        }
        Hud::View view;
        if (!caption.empty()) {
            view.show = true;
            view.dot = false;
            view.subtitle = caption;
        }
        state.captionShown = !caption.empty();
        Hud::Set(view, std::floor(before) != std::floor(state.captionSince));  // once a second, in case the HUD movie wasn't up yet
    }
}
