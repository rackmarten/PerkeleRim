// Junk to throw from Skyrim's inventory (docs/PROTOCOL.md "Throwing junk"). Key 3 (the Throw Junk
// power) takes out a random piece of what Kumpo carries: MSC's own empty beer bottles (the pocket's
// kPocketBottle, mixed in on MSC's side, Pocket.ThrowKind) and junk: Skyrim's clutter (misc items
// with the VendorItemClutter keyword: tankards, plates, bottles, baskets, but not pelts, gems, ingots,
// lockpicks or keys) and fruit and vegetables (kProduce), never quest items or PerkeleRim's own
// pocket items. Every piece is as likely as any other, so a stack of ten comes up ten times as often
// as a single one. MSC throws it like an empty bottle; only the damage goes by its weight
// (Damage::ThrownMass). Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <cstdint>
#include <span>

#include "food.h"

namespace PerkeleRim::Junk
{
    // What an inventory entry is, as far as throwing goes.
    enum class Kind : std::uint8_t
    {
        kOther,    // not junk
        kClutter,  // a misc item with VendorItemClutter
        kProduce,  // fruit or a vegetable (kProduce)
    };

    // One inventory entry, as Skyrim sees it.
    struct Item
    {
        Kind          kind = Kind::kOther;
        bool          quest = false;     // a quest item, or one of PerkeleRim's own pocket items
        std::int32_t  count = 1;         // how many Kumpo carries, less what is out
        float         weight = 0.0f;     // Skyrim's
        std::int32_t  value = 0;         // gold
        std::uint32_t form = 0;          // form id
    };

    // Heavier than this, it isn't thrown (Skyrim's weight units; a full charge still flies like a bottle).
    inline constexpr float kWeightMax = 10.0f;

    // Skyrim's weight as the mass the hit counts (Damage::ThrownMass): a unit of weight a kilogram,
    // weightless items kMassMin.
    inline constexpr float kKilogramsPerWeight = 1.0f;
    inline constexpr float kMassMin = 0.1f;

    constexpr float Mass(float a_weight)
    {
        const float mass = a_weight * kKilogramsPerWeight;
        return mass > kMassMin ? mass : kMassMin;
    }

    // Fruit and vegetables from food.h's table: thrown, the rest of the food is eaten.
    struct Produce
    {
        Food::Plugin  plugin;
        std::uint32_t id;  // local form id in plugin
        const char*   name;
    };

    inline constexpr Produce kProduce[] = {
        { Food::Plugin::Skyrim, 0x064B2F, "Green Apple" },
        { Food::Plugin::Skyrim, 0x064B2E, "Red Apple" },
        { Food::Plugin::Skyrim, 0x064B42, "Tomato" },
        { Food::Plugin::Skyrim, 0x10D666, "Gourd" },
        { Food::Plugin::Skyrim, 0x064B3F, "Cabbage" },
        { Food::Plugin::Skyrim, 0x064B41, "Potato" },
        { Food::Plugin::Skyrim, 0x064B40, "Carrot" },
        { Food::Plugin::Skyrim, 0x0669A5, "Leek" },
        { Food::Plugin::Dragonborn, 0x0206E7, "Ash Yam" },
    };

    constexpr bool Throwable(const Item& a_item)
    {
        return a_item.kind != Kind::kOther && !a_item.quest && a_item.weight <= kWeightMax;
    }

    // How many pieces there are to throw.
    constexpr std::int64_t Total(std::span<const Item> a_items)
    {
        std::int64_t total = 0;
        for (const auto& item : a_items) {
            if (Throwable(item) && item.count > 0) {
                total += item.count;
            }
        }
        return total;
    }

    // The entry thrown next, -1 for none: a random piece, each one as likely. a_random(n) returns a
    // number in 0..n-1 (n >= 1); out-of-range numbers are clamped.
    template <class Random>
    constexpr int Pick(std::span<const Item> a_items, Random&& a_random)
    {
        const std::int64_t total = Total(a_items);
        if (total <= 0) {
            return -1;
        }
        std::int64_t roll = static_cast<std::int64_t>(a_random(total));
        roll = roll < 0 ? 0 : roll >= total ? total - 1 : roll;
        int last = -1;
        for (std::size_t i = 0; i < a_items.size(); i++) {
            const auto& item = a_items[i];
            if (!Throwable(item) || item.count <= 0) {
                continue;
            }
            last = static_cast<int>(i);
            if (roll < item.count) {
                return last;
            }
            roll -= item.count;
        }
        return last;
    }
}
