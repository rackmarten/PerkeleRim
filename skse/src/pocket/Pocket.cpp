#include "pocket/Pocket.h"

#include "food.h"
#include "hotbar.h"
#include "link/LinkService.h"

namespace PerkeleRim::Pocket
{
    namespace
    {
        constexpr auto kPlugin = "PerkeleRim.esp"sv;
        // Fixed local form ids, assigned by tools/esp-gen: the items at kFirstItem + kPocket* kind
        // (gold is Skyrim's own), and the hotbar powers.
        constexpr RE::FormID kFirstItem = 0x906;
        constexpr RE::FormID kGold = 0x00000F;  // Skyrim.esm Gold001
        constexpr RE::FormID kHotbarHands = 0x91A;
        constexpr RE::FormID kHotbarTools = 0x91B;
        constexpr RE::FormID kHotbarBottle = 0x91C;

        constexpr float kCountEvery = 0.25f;  // seconds between looks at the inventory

        using Counts = std::array<std::uint32_t, Protocol::kPocketKinds>;

        struct State
        {
            std::array<RE::TESBoundObject*, Protocol::kPocketKinds> items{};
            std::array<RE::SpellItem*, 3>                            powers{};  // Hands, Tools, Bottle
            std::unordered_map<RE::FormID, Food::Effect>             food;      // food.h's table, resolved
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
            return std::ranges::all_of(state.items, [](auto* a_item) { return a_item != nullptr; }) &&
                   std::ranges::all_of(state.powers, [](auto* a_power) { return a_power != nullptr; });
        }

        // What an ingestible does to MSC's needs: food.h's table, else by its sound and weight for
        // other food; nothing for potions that aren't listed.
        std::optional<Food::Effect> EffectOf(RE::AlchemyItem* a_item)
        {
            if (const auto it = state.food.find(a_item->GetFormID()); it != state.food.end()) {
                return it->second;
            }
            if (!a_item->IsFood()) {
                return std::nullopt;
            }
            const auto* sound = a_item->data.consumptionSound;
            const auto  id = sound ? sound->GetFormID() : 0;
            const auto  kind = id == Food::kSoundBottle ? Food::Sound::Bottle : id == Food::kSoundSoup ? Food::Sound::Soup : Food::Sound::Eat;
            return Food::Fallback(kind, a_item->GetWeight());
        }

        // Kumpo ate or drank a_item in Skyrim.
        void Consumed(RE::FormID a_item)
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* item = RE::TESForm::LookupByID<RE::AlchemyItem>(a_item);
            if (!player || !item) {
                return;
            }
            const auto effect = EffectOf(item);
            if (!effect) {
                return;
            }
            Protocol::ConsumeEvent message{ effect->hunger, effect->thirst, effect->urine, effect->drunk, effect->bottle ? Protocol::kConsumeDrink : 0u };
            const bool sent = LinkService::Get().Send(Protocol::MessageType::ConsumeEvent, &message, sizeof(message));
            SKSE::log::info("food: {} hunger {:+.0f} thirst {:+.0f} urine {:+.0f} drunk {:+.3f}{}", item->GetName(), effect->hunger, effect->thirst,
                effect->urine, effect->drunk, sent ? "" : " (not sent, no link)");
            if (effect->bottle) {
                player->AddObjectToContainer(state.items[Protocol::kPocketBottle], nullptr, 1, nullptr);
                state.dirty = true;
            }
        }

        // Skyrim sends an equip event for every potion drunk and food eaten.
        class EquipSink : public RE::BSTEventSink<RE::TESEquipEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::TESEquipEvent* a_event, RE::BSTEventSource<RE::TESEquipEvent>*) override
            {
                if (a_event && a_event->equipped && a_event->actor && a_event->actor->IsPlayerRef()) {
                    const auto item = a_event->baseObject;
                    if (auto* form = RE::TESForm::LookupByID(item); form && form->Is(RE::FormType::AlchemyItem)) {
                        SKSE::GetTaskInterface()->AddTask([item]() { Consumed(item); });  // not from inside the equip itself
                    }
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

        // The hotbar powers, so the Favorites menu can bind them. Skyrim says "Power added" once.
        void GivePowers(RE::PlayerCharacter* a_player)
        {
            for (auto* power : state.powers) {
                if (!a_player->HasSpell(power)) {
                    a_player->AddSpell(power);
                    SKSE::log::info("hotbar: gave the player {}", power->GetName());
                }
            }
        }

        // What Skyrim's hotkey a_slot holds: a spell (MagicFavorites) or an item in the inventory
        // with that ExtraHotkey.
        Hotbar::Bound BoundTo(RE::PlayerCharacter* a_player, int a_slot, RE::TESBoundObject*& a_item)
        {
            a_item = nullptr;
            if (auto* magic = RE::MagicFavorites::GetSingleton(); magic && static_cast<std::uint32_t>(a_slot) < magic->hotkeys.size()) {
                if (auto* form = magic->hotkeys[a_slot]) {
                    return form == state.powers[0] ? Hotbar::Bound::Hands
                         : form == state.powers[1] ? Hotbar::Bound::Tools
                         : form == state.powers[2] ? Hotbar::Bound::Bottle
                                                    : Hotbar::Bound::Other;
                }
            }
            auto* changes = a_player->GetInventoryChanges();
            if (!changes || !changes->entryList) {
                return Hotbar::Bound::None;
            }
            for (auto* entry : *changes->entryList) {
                if (!entry || !entry->object || !entry->extraLists) {
                    continue;
                }
                for (auto* extra : *entry->extraLists) {
                    auto* hotkey = extra ? extra->GetByType<RE::ExtraHotkey>() : nullptr;
                    if (!hotkey || static_cast<int>(hotkey->hotkey.get()) != a_slot) {
                        continue;
                    }
                    a_item = entry->object;
                    if (a_item == state.items[Protocol::kPocketBeer]) return Hotbar::Bound::Beer;
                    if (a_item == state.items[Protocol::kPocketSausages]) return Hotbar::Bound::Sausages;
                    if (a_item == state.items[Protocol::kPocketPizza]) return Hotbar::Bound::Pizza;
                    if (a_item == state.items[Protocol::kPocketChips]) return Hotbar::Bound::Chips;
                    if (a_item == state.items[Protocol::kPocketMacaron]) return Hotbar::Bound::Macaron;
                    return a_item->Is(RE::FormType::AlchemyItem) ? Hotbar::Bound::Consumable : Hotbar::Bound::Other;
                }
            }
            return Hotbar::Bound::None;
        }
    }

    void OnDataLoaded()
    {
        static EquipSink equipSink;
        auto* data = RE::TESDataHandler::GetSingleton();
        for (std::uint32_t i = 0; i < Protocol::kPocketKinds; i++) {
            // Food or misc item: untyped lookups (the typed ones match one exact form type).
            auto* form = !data ? nullptr : i == Protocol::kPocketGold ? RE::TESForm::LookupByID(kGold) : data->LookupForm(kFirstItem + i, kPlugin);
            state.items[i] = form ? form->As<RE::TESBoundObject>() : nullptr;
        }
        const RE::FormID powers[] = { kHotbarHands, kHotbarTools, kHotbarBottle };
        for (std::size_t i = 0; i < state.powers.size(); i++) {
            state.powers[i] = data ? data->LookupForm<RE::SpellItem>(powers[i], kPlugin) : nullptr;
        }
        if (!Loaded()) {
            SKSE::log::warn("pocket: {} has no pocket items or hotbar powers, rebuild it with scripts/build-esp.ps1", kPlugin);
            return;
        }
        std::size_t missing = 0;
        for (const auto& entry : Food::kTable) {
            const auto* file = Food::kPluginFiles[static_cast<std::size_t>(entry.plugin)];
            if (auto* form = data->LookupForm(entry.id, file)) {
                state.food[form->GetFormID()] = entry.effect;
            } else {
                missing++;  // a DLC that isn't installed
            }
        }
        SKSE::log::info("food: {} of {} listed foods and drinks found ({} in a DLC that isn't loaded)", state.food.size(), std::size(Food::kTable), missing);
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESEquipEvent>(&equipSink);
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
        GivePowers(a_player);
        state.counts = Count(a_player);
        if (state.sentValid && !state.dirty && state.counts == state.sent) {
            return;
        }
        Protocol::PocketCounts message{ state.session, state.applied, {} };
        std::ranges::copy(state.counts, message.counts);
        if (LinkService::Get().Send(Protocol::MessageType::PocketCounts, &message, sizeof(message))) {
            if (!state.sentValid || state.counts != state.sent) {
                SKSE::log::info("pocket: counts to MSC {} {} {} {} {} {} {} gold {} (applied {}:{})", state.counts[0], state.counts[1], state.counts[2],
                    state.counts[3], state.counts[4], state.counts[5], state.counts[6], state.counts[7], state.session, state.applied);
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

    std::uint16_t Hotkey(RE::PlayerCharacter* a_player, int a_slot)
    {
        RE::TESBoundObject* item = nullptr;
        // The last thing bound to each number this session: Skyrim drops an item's hotkey with its
        // last one, and the default key then would eat something else (beer on 7 gone: 7 eats chips).
        static std::array<Hotbar::Bound, Hotbar::kSlots> last{};
        if (a_slot < 0 || a_slot >= Hotbar::kSlots) {
            return 0;
        }
        if (!Loaded() || !a_player) {
            return Hotbar::Resolve(a_slot, Hotbar::Bound::None).mscKey;
        }
        const auto bound = BoundTo(a_player, a_slot, item);
        if (bound != Hotbar::Bound::None) {
            last[a_slot] = bound;
        } else if (last[a_slot] >= Hotbar::Bound::Beer) {
            SKSE::log::info("hotbar: {} had an item bound, none left", a_slot + 1);
            return 0;
        }
        std::uint32_t anywhere = 0;  // what all eight numbers hold, so a default gives way to its own binding
        for (int slot = 0; slot < Hotbar::kSlots; slot++) {
            RE::TESBoundObject* other = nullptr;
            anywhere |= Hotbar::Bit(slot == a_slot ? bound : BoundTo(a_player, slot, other));
        }
        const auto action = Hotbar::Resolve(a_slot, bound, anywhere);
        if (action.use && item) {
            SKSE::log::info("hotbar: {} uses {}", a_slot + 1, item->GetName());
            RE::ActorEquipManager::GetSingleton()->EquipObject(a_player, item);  // eaten or drunk, EquipSink follows
        } else if (bound != Hotbar::Bound::None) {
            SKSE::log::info("hotbar: {} is MSC's key {} ({})", a_slot + 1, action.mscKey, item ? item->GetName() : "a power or spell");
        }
        return action.mscKey;
    }
}
