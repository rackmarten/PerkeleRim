#include "pocket/Pocket.h"

#include "link/LinkService.h"

namespace PerkeleRim::Pocket
{
    namespace
    {
        constexpr auto kPlugin = "PerkeleRim.esp"sv;
        // Fixed local form ids, assigned by tools/esp-gen: the drinks list, and the items at
        // kFirstItem + kPocket* kind.
        constexpr RE::FormID kBeerDrinks = 0x900;
        constexpr RE::FormID kFirstItem = 0x906;

        constexpr float kCountEvery = 0.25f;  // seconds between looks at the inventory

        using Counts = std::array<std::uint32_t, Protocol::kPocketKinds>;

        struct State
        {
            std::array<RE::TESBoundObject*, Protocol::kPocketKinds> items{};
            RE::BGSListForm*                                         drinks = nullptr;
            std::uint32_t                                            session = 0;  // MSC's, of the last PocketChange
            std::uint32_t                                            applied = 0;  // its seq
            Counts                                                   counts{};
            Counts                                                   sent{};
            bool                                                     sentValid = false;  // MSC has counts from this link session
            bool                                                     dirty = false;      // send now, don't wait for the next look
            bool                                                     linkUp = false;
            float                                                    sinceCount = kCountEvery;
        } state;

        bool Loaded()
        {
            return std::ranges::all_of(state.items, [](auto* a_item) { return a_item != nullptr; });
        }

        // Mead or ale picked up becomes as much MSC beer.
        void SwapForBeer(RE::FormID a_drink, std::int32_t a_count)
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* drink = RE::TESForm::LookupByID<RE::TESBoundObject>(a_drink);
            auto* beer = state.items[Protocol::kPocketBeer];
            if (!player || !drink || !beer) {
                return;
            }
            const auto count = (std::min)(a_count, player->GetItemCount(drink));
            if (count <= 0) {
                return;
            }
            player->RemoveItem(drink, count, RE::ITEM_REMOVE_REASON::kRemove, nullptr, nullptr);
            player->AddObjectToContainer(beer, nullptr, count, nullptr);
            const auto text = std::format("{} became {}", drink->GetName(), beer->GetName());
            RE::SendHUDMessage::ShowHUDMessage(text.c_str());
            SKSE::log::info("pocket: {} x{} became {}", drink->GetName(), count, beer->GetName());
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
                    SKSE::GetTaskInterface()->AddTask([drink, count]() { SwapForBeer(drink, count); });
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };

        Counts Count(RE::PlayerCharacter* a_player)
        {
            Counts counts{};
            for (std::size_t i = 0; i < counts.size(); i++) {
                counts[i] = static_cast<std::uint32_t>((std::max)(0, a_player->GetItemCount(state.items[i])));
            }
            return counts;
        }
    }

    void OnDataLoaded()
    {
        static ContainerSink containerSink;
        auto* data = RE::TESDataHandler::GetSingleton();
        state.drinks = data ? data->LookupForm<RE::BGSListForm>(kBeerDrinks, kPlugin) : nullptr;
        for (std::uint32_t i = 0; i < Protocol::kPocketKinds; i++) {
            state.items[i] = data ? data->LookupForm<RE::TESObjectMISC>(kFirstItem + i, kPlugin) : nullptr;
        }
        if (!state.drinks || !Loaded()) {
            SKSE::log::warn("pocket: {} has no drink list or pocket items, rebuild it with scripts/build-esp.ps1", kPlugin);
            return;
        }
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESContainerChangedEvent>(&containerSink);
        }
    }

    void Update(RE::PlayerCharacter* a_player, float a_delta)
    {
        if (!Loaded() || !a_player) {
            return;
        }
        const bool up = LinkService::Get().IsUp();
        if (up != state.linkUp) {
            state.linkUp = up;
            state.sentValid = false;  // a new MSC, or the same one back: it gets the counts again
        }
        if (!up) {
            return;
        }
        state.sinceCount += a_delta;
        if (state.sinceCount < kCountEvery && !state.dirty && state.sentValid) {
            return;
        }
        state.sinceCount = 0.0f;
        state.counts = Count(a_player);
        if (state.sentValid && !state.dirty && state.counts == state.sent) {
            return;
        }
        Protocol::PocketCounts message{ state.session, state.applied, {} };
        std::ranges::copy(state.counts, message.counts);
        if (LinkService::Get().Send(Protocol::MessageType::PocketCounts, &message, sizeof(message))) {
            if (!state.sentValid || state.counts != state.sent) {
                SKSE::log::info("pocket: counts to MSC {} {} {} {} {} {} {} (applied {}:{})", state.counts[0], state.counts[1], state.counts[2],
                    state.counts[3], state.counts[4], state.counts[5], state.counts[6], state.session, state.applied);
            }
            state.sent = state.counts;
            state.sentValid = true;
            state.dirty = false;
        }
    }

    void OnChange(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        Protocol::PocketChange change{};
        if (a_size < sizeof(change) || !a_player) {
            return;
        }
        std::memcpy(&change, a_data, sizeof(change));
        if (change.session != state.session) {
            state.session = change.session;  // MSC's link came up again: its seq starts over
            state.applied = 0;
        }
        if (change.seq <= state.applied) {
            return;
        }
        state.applied = change.seq;
        state.dirty = true;
        auto* item = change.kind < Protocol::kPocketKinds ? state.items[change.kind] : nullptr;
        if (!item) {
            SKSE::log::warn("pocket: change for unknown kind {} dropped", change.kind);
            return;
        }
        if (change.delta > 0) {
            a_player->AddObjectToContainer(item, nullptr, change.delta, nullptr);
            SKSE::log::info("pocket: +{} {}", change.delta, item->GetName());
        } else if (change.delta < 0) {
            const auto count = (std::min)(-change.delta, a_player->GetItemCount(item));
            if (count > 0) {
                a_player->RemoveItem(item, count, RE::ITEM_REMOVE_REASON::kRemove, nullptr, nullptr);
            }
            if (count < -change.delta) {
                SKSE::log::warn("pocket: MSC took {} {} out, the inventory had {}", -change.delta, item->GetName(), count);
            } else {
                SKSE::log::info("pocket: -{} {}", count, item->GetName());
            }
        }
    }
}
