// Junk to throw from Skyrim's inventory (docs/PROTOCOL.md "Throwing junk"). Key 3 (the Throw Junk
// power) takes MSC's own empty beer bottles out first (the pocket's kPocketBottle); once those are
// gone it takes junk: Skyrim's clutter (misc items with the VendorItemClutter keyword: tankards,
// plates, baskets, but not pelts, gems, ingots, lockpicks or keys) and fruit and vegetables
// (kProduce), never quest items or PerkeleRim's own pocket items. Bottles among the clutter go
// first, then the rest of the clutter, then the produce; each cheapest first, then lightest. MSC
// throws it like an empty bottle; only the damage goes by its weight (Damage::ThrownMass).
// Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <cstdint>
#include <span>
#include <string_view>

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
        bool          bottle = false;    // its name says bottle (IsBottleName)
        float         weight = 0.0f;     // Skyrim's
        std::int32_t  value = 0;         // gold
        std::uint32_t form = 0;          // form id, the last tie-break so the pick is stable
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

    // "bottle" anywhere in the name, any case: Skyrim's empty wine bottles, and the per-drink empty
    // bottles to come, go before the rest of the junk.
    constexpr bool IsBottleName(std::string_view a_name)
    {
        constexpr std::string_view word = "bottle";
        for (std::size_t i = 0; i + word.size() <= a_name.size(); i++) {
            bool same = true;
            for (std::size_t j = 0; j < word.size() && same; j++) {
                const char c = a_name[i + j];
                same = (c >= 'A' && c <= 'Z' ? static_cast<char>(c - 'A' + 'a') : c) == word[j];
            }
            if (same) {
                return true;
            }
        }
        return false;
    }

    constexpr bool Throwable(const Item& a_item)
    {
        return a_item.kind != Kind::kOther && !a_item.quest && a_item.weight <= kWeightMax;
    }

    // 0 bottles, 1 the rest of the clutter, 2 produce.
    constexpr int Tier(const Item& a_item)
    {
        return a_item.kind == Kind::kProduce ? 2 : a_item.bottle ? 0 : 1;
    }

    // Whether a_a is thrown before a_b (both throwable).
    constexpr bool Before(const Item& a_a, const Item& a_b)
    {
        if (Tier(a_a) != Tier(a_b)) {
            return Tier(a_a) < Tier(a_b);
        }
        if (a_a.value != a_b.value) {
            return a_a.value < a_b.value;
        }
        if (a_a.weight != a_b.weight) {
            return a_a.weight < a_b.weight;
        }
        return a_a.form < a_b.form;
    }

    // The entry thrown next, -1 for none.
    constexpr int Pick(std::span<const Item> a_items)
    {
        int best = -1;
        for (std::size_t i = 0; i < a_items.size(); i++) {
            if (Throwable(a_items[i]) && (best < 0 || Before(a_items[i], a_items[static_cast<std::size_t>(best)]))) {
                best = static_cast<int>(i);
            }
        }
        return best;
    }
}
