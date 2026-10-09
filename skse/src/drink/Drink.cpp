#include "drink/Drink.h"

#include "link/LinkService.h"

namespace PerkeleRim::Drink
{
    namespace
    {
        constexpr auto kPlugin = "PerkeleRim.esp"sv;
        // Fixed local form ids, assigned by tools/esp-gen.
        constexpr RE::FormID kBeerDrinks = 0x900;
        constexpr RE::FormID kDrunkImod = 0x901;

        // MSC's drunk bar is full at level 4; so is the blur. Below this strength it's off.
        constexpr float kFullDrunk = 4.0f;
        constexpr float kMinStrength = 0.01f;

        struct State
        {
            RE::BGSListForm*                               drinks = nullptr;
            RE::TESImageSpaceModifier*                     imod = nullptr;
            RE::NiPointer<RE::ImageSpaceModifierInstanceForm> blur;
            bool                                           blurOn = false;
            float                                          blurAge = 0.0f;  // seconds since the loop was last triggered
        } state;

        // Sends a_count of a picked-up drink to MSC as beer and takes it out of the inventory, or
        // leaves it there when MSC can't get it (link down, ring full).
        void GiveToMsc(RE::FormID a_drink, std::int32_t a_count)
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* drink = RE::TESForm::LookupByID<RE::TESBoundObject>(a_drink);
            if (!player || !drink) {
                return;
            }
            const auto counts = player->GetInventoryCounts([drink](RE::TESBoundObject& a_object) { return &a_object == drink; });
            const auto held = counts.contains(drink) ? counts.at(drink) : 0;
            const auto count = (std::min)(a_count, held);
            if (count <= 0) {
                return;
            }
            const Protocol::GiveBeer beer{ static_cast<std::uint32_t>(count), 0 };
            if (!LinkService::Get().Send(Protocol::MessageType::GiveBeer, &beer, sizeof(beer))) {
                SKSE::log::info("drink: {} x{} stays in Skyrim, MSC isn't there", drink->GetName(), count);
                return;
            }
            player->RemoveItem(drink, count, RE::ITEM_REMOVE_REASON::kRemove, nullptr, nullptr);
            const auto text = std::format("{} went to MSC as beer", drink->GetName());
            RE::SendHUDMessage::ShowHUDMessage(text.c_str());
            SKSE::log::info("drink: {} x{} sent to MSC as beer", drink->GetName(), count);
        }

        class ContainerSink : public RE::BSTEventSink<RE::TESContainerChangedEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::TESContainerChangedEvent* a_event, RE::BSTEventSource<RE::TESContainerChangedEvent>*) override
            {
                if (a_event && a_event->itemCount > 0 && a_event->newContainer == 0x14 && state.drinks &&
                    state.drinks->HasForm(a_event->baseObj)) {
                    // Not from inside the inventory change itself.
                    const auto drink = a_event->baseObj;
                    const auto count = a_event->itemCount;
                    SKSE::GetTaskInterface()->AddTask([drink, count]() { GiveToMsc(drink, count); });
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void OnDataLoaded()
    {
        static ContainerSink containerSink;
        auto* data = RE::TESDataHandler::GetSingleton();
        state.drinks = data ? data->LookupForm<RE::BGSListForm>(kBeerDrinks, kPlugin) : nullptr;
        state.imod = data ? data->LookupForm<RE::TESImageSpaceModifier>(kDrunkImod, kPlugin) : nullptr;
        if (!state.drinks || !state.imod) {
            SKSE::log::warn("drink: {} has no drink list or drunk imagespace, rebuild it with scripts/build-esp.ps1", kPlugin);
        }
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESContainerChangedEvent>(&containerSink);
        }
    }

    void SetDrunk(float a_level, float a_delta)
    {
        if (!state.imod) {
            return;
        }
        const float strength = std::clamp(a_level / kFullDrunk, 0.0f, 1.0f);
        if (strength < kMinStrength) {
            if (state.blurOn) {
                RE::ImageSpaceModifierInstanceForm::Stop(state.imod);
                state.blur.reset();
                state.blurOn = false;
                SKSE::log::info("drunk blur: off");
            }
            return;
        }
        state.blurAge += a_delta;
        if (!state.blur || state.blurAge >= state.imod->data.duration) {
            if (!state.blurOn) {
                state.blurOn = true;
                SKSE::log::info("drunk blur: on at drunk level {:.2f}", a_level);
            }
            // Triggered again at the end of every loop: an imagespace modifier ends after its duration.
            state.blur.reset(RE::ImageSpaceModifierInstanceForm::Trigger(state.imod, strength, nullptr));
            state.blurAge = 0.0f;
        } else if (state.blur) {
            state.blur->strength = strength;
        }
    }
}
