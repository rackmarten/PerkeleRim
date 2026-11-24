// Shopping at Teimo's stall in Riverwood: where his goods stand and where his counter is, in the
// stall's own frame, and the stall's rules on Skyrim's side (when the goods go to MSC, the bill on
// the HUD, the bounty for walking off with goods). The goods themselves, their prices and the till
// are MSC's (msc/src/Link/StallShop.cs, msc/src/Game/StallGoods.cs): MSC's own shop goods at MSC's
// own prices, paid in MSC's money, which is Skyrim's gold.
// Header-only and free of Skyrim types, so the protocol tests cover it; Skyrim's half is
// skse/src/teimo/Teimo.cpp.

#pragma once

#include "../perkelerim_protocol.h"

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <string>

namespace PerkeleRim::Teimo
{
    // The stall's frame, as tools/esp-gen builds it: origin at Teimo's feet behind the counter, +y
    // the way the stall faces (toward the customers), +x to Teimo's right, z up from his feet; rot
    // turns a good about z from facing +y. Units.
    struct StallSpot
    {
        std::uint32_t product;  // Protocol::kShop*
        float         x, y, z;  // a little above what it stands on; MSC sets it down on the surface nearest this height
        float         rot;
    };

    // What the stall sells, and where: beer cases on the crates by his cart, food and the like on
    // the barrel and the crates on his other side, the counter itself kept clear for paying.
    inline constexpr std::array kStallSpots{
        StallSpot{ Protocol::kShopBeer, 120.0f, 15.0f, 55.0f, 0.1f },
        StallSpot{ Protocol::kShopBeer, 150.0f, 12.0f, 55.0f, -0.1f },
        StallSpot{ Protocol::kShopBeer, 135.0f, 60.0f, 103.0f, 0.4f },
        StallSpot{ Protocol::kShopSausages, -135.0f, 30.0f, 93.0f, 0.3f },
        StallSpot{ Protocol::kShopPizza, -118.0f, 10.0f, 93.0f, -0.2f },
        StallSpot{ Protocol::kShopChips, -105.0f, 35.0f, 93.0f, 0.5f },
        StallSpot{ Protocol::kShopMacaron, -140.0f, 5.0f, 93.0f, 0.0f },
        StallSpot{ Protocol::kShopCigarettes, -55.0f, 60.0f, 85.0f, 0.2f },
        StallSpot{ Protocol::kShopJuice, -115.0f, -45.0f, 48.0f, 0.0f },
        StallSpot{ Protocol::kShopMilk, -128.0f, -58.0f, 48.0f, 0.3f },
        StallSpot{ Protocol::kShopMotorOil, 112.0f, -38.0f, 55.0f, 0.2f },
        StallSpot{ Protocol::kShopCoffee, 132.0f, -46.0f, 55.0f, -0.3f },
        StallSpot{ Protocol::kShopMosquitoSpray, 55.0f, 60.0f, 85.0f, 0.0f },
    };
    static_assert(kStallSpots.size() <= Protocol::kStallGoodsMax);

    // The counter's top where goods are paid for: its middle, half its width and depth, and how
    // high above it a good still counts as lying on it.
    struct Box
    {
        float x, y, z;
        float halfX, halfY, halfZ;
    };
    inline constexpr Box kCounterTop{ 0.0f, 55.0f, 80.0f, 40.0f, 25.0f, 40.0f };

    inline constexpr float kStallReach = 3500.0f;  // units (50 m) from Teimo within which MSC gets the stall's goods

    struct Point
    {
        float x, y, z;
    };

    // A point of the stall's frame in Skyrim's world, the stall's origin at a_at facing a_heading
    // (radians, clockwise from north), as tools/esp-gen places its pieces.
    inline Point StallToWorld(const Point& a_at, float a_heading, float a_x, float a_y, float a_z)
    {
        const float c = std::cos(a_heading), s = std::sin(a_heading);
        return { a_at.x + a_x * c + a_y * s, a_at.y - a_x * s + a_y * c, a_at.z + a_z };
    }

    // The stall's goods and counter for MSC (Protocol::TeimoStall), Teimo's spot a_at facing a_heading.
    inline Protocol::TeimoStall MakeStall(std::uint32_t a_generation, const Point& a_at, float a_heading)
    {
        Protocol::TeimoStall stall{};
        stall.generation = a_generation;
        stall.count = static_cast<std::uint32_t>(kStallSpots.size());
        stall.teimo[0] = a_at.x;
        stall.teimo[1] = a_at.y;
        stall.teimo[2] = a_at.z;
        stall.heading = a_heading;
        const auto counter = StallToWorld(a_at, a_heading, kCounterTop.x, kCounterTop.y, kCounterTop.z);
        stall.counter[0] = counter.x;
        stall.counter[1] = counter.y;
        stall.counter[2] = counter.z;
        stall.counterHalf[0] = kCounterTop.halfX;
        stall.counterHalf[1] = kCounterTop.halfY;
        stall.counterHalf[2] = kCounterTop.halfZ;
        for (std::size_t i = 0; i < kStallSpots.size(); i++) {
            const auto& spot = kStallSpots[i];
            const auto  at = StallToWorld(a_at, a_heading, spot.x, spot.y, spot.z);
            auto&       good = stall.goods[i];
            good.product = spot.product;
            good.position[0] = at.x;
            good.position[1] = at.y;
            good.position[2] = at.z;
            const float pi = 3.14159265f;
            float       heading = std::remainder(a_heading + spot.rot, 2.0f * pi);
            good.heading = heading < 0.0f ? heading + 2.0f * pi : heading;
        }
        return stall;
    }

    // The bounty for goods worth a_total marks carried off unpaid: what they cost, in whole gold,
    // at least 1. Skyrim's own theft bounty is the stolen item's value too.
    inline std::uint32_t TheftBounty(float a_total)
    {
        return static_cast<std::uint32_t>((std::max)(1.0f, std::ceil(a_total - 0.001f)));
    }

    // MSC's marks as Skyrim shows them: whole gold, pennies after a point only when there are any.
    inline std::string Marks(float a_marks)
    {
        const long cents = std::lround(static_cast<double>(a_marks) * 100.0);
        const long whole = cents / 100, part = std::labs(cents % 100);
        if (part == 0) {
            return std::to_string(whole);
        }
        return std::to_string(whole) + (part < 10 ? ".0" : ".") + std::to_string(part);
    }

    // The HUD's note for what lies on the counter (TeimoShop kShopBill), empty for nothing.
    inline std::string BillNote(std::uint32_t a_count, float a_total)
    {
        if (a_count == 0) {
            return {};
        }
        return (a_count == 1 ? std::string("1 thing") : std::to_string(a_count) + " things") + " on Teimo's counter: " + Marks(a_total) +
               " gold. Talk to him to pay.";
    }
}
