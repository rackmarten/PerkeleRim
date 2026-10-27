#include "junk/ThrowJunk.h"

#include <random>
#include <unordered_set>

#include "coords.h"
#include "junk.h"
#include "link/LinkService.h"

namespace PerkeleRim::ThrowJunk
{
    namespace
    {
        constexpr auto           kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID     kFirstPocketItem = 0x906;  // PerkeleRim.esp's pocket items, 0x906 + kPocket* (Pocket.cpp)
        constexpr std::uint32_t  kPocketItems = Protocol::kPocketGold;  // the ones before gold
        constexpr RE::FormID     kLockpick = 0x00000A;
        constexpr RE::FormID     kGold = 0x00000F;
        constexpr float          kDropLift = 2.0f;  // units above where MSC's body rested, so it doesn't start in the ground

        struct Out
        {
            std::uint32_t       session = 0;
            std::uint32_t       seq = 0;
            RE::TESBoundObject* item = nullptr;
        };

        struct State
        {
            RE::BGSKeyword*                         clutter = nullptr;
            std::unordered_set<RE::FormID>          produce;
            std::unordered_set<RE::FormID>          skip;  // PerkeleRim's pocket items, gold, lockpicks
            std::vector<Out>                        out;   // in MSC's hand or in the air
            std::mt19937                            random{ std::random_device{}() };  // which piece comes out
        } state;

        struct Entry
        {
            RE::TESBoundObject* item;
            Junk::Item          rules;  // its count: in the inventory, less what is out
        };

        std::int32_t OutCount(const RE::TESBoundObject* a_item)
        {
            return static_cast<std::int32_t>(std::ranges::count_if(state.out, [&](const Out& a_out) { return a_out.item == a_item; }));
        }

        Junk::Kind KindOf(RE::TESBoundObject& a_item)
        {
            if (state.skip.contains(a_item.GetFormID())) {
                return Junk::Kind::kOther;
            }
            if (state.produce.contains(a_item.GetFormID())) {
                return Junk::Kind::kProduce;
            }
            auto* misc = a_item.As<RE::TESObjectMISC>();
            return misc && state.clutter && misc->HasKeyword(state.clutter) ? Junk::Kind::kClutter : Junk::Kind::kOther;
        }

        // The throwable junk Kumpo carries, with what is out taken off.
        std::vector<Entry> Entries(RE::PlayerCharacter* a_player)
        {
            std::vector<Entry> entries;
            auto inventory = a_player->GetInventory([](RE::TESBoundObject& a_item) { return KindOf(a_item) != Junk::Kind::kOther; });
            for (auto& [item, data] : inventory) {
                const auto count = data.first - OutCount(item);
                if (count <= 0) {
                    continue;
                }
                const auto* entry = data.second.get();
                Junk::Item  rules{ KindOf(*item), entry && entry->IsQuestObject(), count, item->GetWeight(), item->GetGoldValue(),
                    item->GetFormID() };
                if (Junk::Throwable(rules)) {
                    entries.push_back({ item, rules });
                }
            }
            return entries;
        }

        bool Send(std::uint32_t a_seq, RE::TESBoundObject* a_item)
        {
            Protocol::JunkOut message{};
            message.seq = a_seq;
            if (a_item) {
                message.form = a_item->GetFormID();
                message.mass = Junk::Mass(a_item->GetWeight());
                const auto& bounds = a_item->boundData;
                message.boundsMin[0] = bounds.boundMin.x;
                message.boundsMin[1] = bounds.boundMin.y;
                message.boundsMin[2] = bounds.boundMin.z;
                message.boundsMax[0] = bounds.boundMax.x;
                message.boundsMax[1] = bounds.boundMax.y;
                message.boundsMax[2] = bounds.boundMax.z;
            }
            if (!LinkService::Get().Send(Protocol::MessageType::JunkOut, &message, sizeof(message))) {
                SKSE::log::warn("junk: the link is full, MSC won't get {}", a_item ? a_item->GetName() : "its none");
                return false;
            }
            return true;
        }
    }

    void OnDataLoaded()
    {
        state.clutter = RE::TESForm::LookupByEditorID<RE::BGSKeyword>("VendorItemClutter");
        auto* data = RE::TESDataHandler::GetSingleton();
        std::size_t missing = 0;
        for (const auto& produce : Junk::kProduce) {
            auto* form = data ? data->LookupForm(produce.id, Food::kPluginFiles[static_cast<std::size_t>(produce.plugin)]) : nullptr;
            if (form) {
                state.produce.insert(form->GetFormID());
            } else {
                missing++;
            }
        }
        state.skip = { kLockpick, kGold };
        for (std::uint32_t i = 0; data && i < kPocketItems; i++) {
            if (auto* form = data->LookupForm(kFirstPocketItem + i, kPlugin)) {
                state.skip.insert(form->GetFormID());
            }
        }
        SKSE::log::info("junk: VendorItemClutter {}, {} of {} fruit and vegetables found ({} in a DLC that isn't loaded)", state.clutter ? "found" : "MISSING",
            state.produce.size(), std::size(Junk::kProduce), missing);
    }

    std::uint32_t Count(RE::PlayerCharacter* a_player)
    {
        if (!a_player) {
            return 0;
        }
        std::uint32_t count = 0;
        for (const auto& entry : Entries(a_player)) {
            count += static_cast<std::uint32_t>(entry.rules.count);
        }
        return count;
    }

    void Take(RE::PlayerCharacter* a_player, std::uint32_t a_session, std::uint32_t a_seq)
    {
        if (!a_player) {
            return;
        }
        const auto               entries = Entries(a_player);
        std::vector<Junk::Item>  rules;
        rules.reserve(entries.size());
        for (const auto& entry : entries) {
            rules.push_back(entry.rules);
        }
        const int pick = Junk::Pick(rules, [](std::int64_t a_n) { return std::uniform_int_distribution<std::int64_t>(0, a_n - 1)(state.random); });
        if (pick < 0) {
            SKSE::log::info("junk: MSC wants junk, Kumpo has none");
            Send(a_seq, nullptr);
            return;
        }
        auto* item = entries[static_cast<std::size_t>(pick)].item;
        if (!Send(a_seq, item)) {
            return;  // MSC gives up waiting; the item stays in the inventory, not out
        }
        state.out.push_back({ a_session, a_seq, item });
        SKSE::log::info("junk: {} ({:08X}, value {}, weight {:.2f}) out to MSC's hand, {} more of it", item->GetName(), item->GetFormID(), item->GetGoldValue(),
            item->GetWeight(), entries[static_cast<std::size_t>(pick)].rules.count - 1);
    }

    void OnBack(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        Protocol::JunkBack back{};
        if (a_size < sizeof(back) || !a_player) {
            return;
        }
        std::memcpy(&back, a_data, sizeof(back));
        const auto found = std::ranges::find_if(state.out, [&](const Out& a_out) { return a_out.session == back.session && a_out.seq == back.seq; });
        if (found == state.out.end()) {
            SKSE::log::info("junk: MSC let go of junk {}:{} that is back already", back.session, back.seq);
            return;
        }
        auto* item = found->item;
        state.out.erase(found);
        if ((back.flags & Protocol::kJunkLanded) == 0) {
            SKSE::log::info("junk: {} back in the inventory", item->GetName());
            return;
        }
        if (a_player->GetItemCount(item) <= 0) {
            SKSE::log::info("junk: {} landed, but Kumpo has none left to drop (sold or dropped meanwhile)", item->GetName());
            return;
        }
        // Upright, turned the way the body's model faced: Havok settles it from there.
        float m[3][3];
        Coords::ToMatrix({ back.rotation[0], back.rotation[1], back.rotation[2], back.rotation[3] }, m);
        const RE::NiPoint3 at(back.position[0], back.position[1], back.position[2] + kDropLift);
        const RE::NiPoint3 angle(0.0f, 0.0f, std::atan2(m[0][1], m[1][1]));
        const auto dropped = a_player->RemoveItem(item, 1, RE::ITEM_REMOVE_REASON::kDropping, nullptr, nullptr, &at, &angle);
        if (auto ref = dropped.get()) {
            ref->SetPosition(at);
            ref->SetAngle(angle);
        }
        SKSE::log::info("junk: {} landed at ({:.0f}, {:.0f}, {:.0f}){}", item->GetName(), at.x, at.y, at.z, dropped ? "" : ", but Skyrim made no reference of it");
    }

    void Release()
    {
        if (state.out.empty()) {
            return;
        }
        SKSE::log::info("junk: {} out in MSC's hand or the air back to the inventory", state.out.size());
        state.out.clear();
    }
}
