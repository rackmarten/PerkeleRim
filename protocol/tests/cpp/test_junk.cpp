#include <doctest/doctest.h>

#include <string_view>
#include <vector>

#include "../../cpp/junk.h"

using namespace PerkeleRim;
using Junk::Item;
using Junk::Kind;

namespace
{
    Item Clutter(std::uint32_t a_form, std::int32_t a_count = 1, float a_weight = 1.0f)
    {
        return { Kind::kClutter, false, a_count, a_weight, 1, a_form };
    }

    Item Produce(std::uint32_t a_form, std::int32_t a_count = 1, float a_weight = 0.1f)
    {
        return { Kind::kProduce, false, a_count, a_weight, 0, a_form };
    }

    // A random source that always rolls a_roll.
    auto Roll(std::int64_t a_roll)
    {
        return [a_roll](std::int64_t) { return a_roll; };
    }
}

TEST_CASE("only clutter and produce are junk, never quest items or heavy ones")
{
    CHECK(Junk::Throwable(Clutter(1, 1)));
    CHECK(Junk::Throwable(Produce(2, 1)));
    CHECK_FALSE(Junk::Throwable(Item{ Kind::kOther, false, 1, 1.0f, 1, 3 }));  // a pelt, a garnet, a lockpick
    auto quest = Clutter(4, 1);
    quest.quest = true;
    CHECK_FALSE(Junk::Throwable(quest));
    CHECK(Junk::Throwable(Clutter(5, 1, Junk::kWeightMax)));
    CHECK_FALSE(Junk::Throwable(Clutter(6, 1, Junk::kWeightMax + 0.5f)));
}

TEST_CASE("nothing to throw picks nothing")
{
    CHECK(Junk::Pick({}, Roll(0)) == -1);
    const std::vector<Item> none = { Item{ Kind::kOther, false, 1, 1.0f, 1, 1 }, Clutter(2, 0) };
    CHECK(Junk::Pick(none, Roll(0)) == -1);
    CHECK(Junk::Total(none) == 0);
}

TEST_CASE("every piece is as likely: a stack comes up as often as its count")
{
    const std::vector<Item> items = {
        Clutter(0x10, 10),                          // ten tankards
        Item{ Kind::kOther, false, 5, 1.0f, 1, 0x20 },  // a pelt: never
        Produce(0x30, 1),                           // one cabbage
        Clutter(0x40, 3),                           // three wine bottles
    };
    CHECK(Junk::Total(items) == 14);
    std::int64_t asked = 0;
    CHECK(Junk::Pick(items, [&](std::int64_t a_n) { asked = a_n; return 0; }) == 0);
    CHECK(asked == 14);
    int counts[4] = {};
    for (std::int64_t roll = 0; roll < 14; roll++) {
        counts[Junk::Pick(items, Roll(roll))]++;
    }
    CHECK(counts[0] == 10);
    CHECK(counts[1] == 0);
    CHECK(counts[2] == 1);
    CHECK(counts[3] == 3);
}

TEST_CASE("a roll out of range is clamped")
{
    const std::vector<Item> items = { Clutter(0x10, 2), Produce(0x20, 1) };
    CHECK(Junk::Pick(items, Roll(-5)) == 0);
    CHECK(Junk::Pick(items, Roll(99)) == 1);
}

TEST_CASE("pieces out in MSC's hand or the air aren't there to throw")
{
    CHECK(Junk::Available(3, 0) == 3);
    CHECK(Junk::Available(3, 1) == 2);
    CHECK(Junk::Available(1, 1) == 0);
    CHECK(Junk::Available(0, 1) == 0);  // sold or dropped meanwhile
}

TEST_CASE("the hit test's one piece counts after the one still flying")
{
    CHECK(Junk::Available(1, 0, true) == 1);
    CHECK(Junk::Available(5, 0, true) == 1);
    CHECK(Junk::Available(1, 1, true) == 0);  // thrown, not landed: nothing to throw yet
    CHECK(Junk::Available(2, 1, true) == 1);  // one added meanwhile is there at once
}

TEST_CASE("a unit of weight is a kilogram, weightless junk still weighs a little")
{
    CHECK(Junk::Mass(0.25f) == doctest::Approx(0.25f));
    CHECK(Junk::Mass(2.0f) == doctest::Approx(2.0f));
    CHECK(Junk::Mass(0.0f) == doctest::Approx(Junk::kMassMin));
    CHECK(Junk::Mass(-1.0f) == doctest::Approx(Junk::kMassMin));
}

TEST_CASE("the produce is food.h's fruit and vegetables")
{
    for (const auto& produce : Junk::kProduce) {
        CAPTURE(produce.name);
        bool listed = false;
        for (const auto& food : Food::kTable) {
            listed = listed || (food.plugin == produce.plugin && food.id == produce.id && std::string_view(food.name) == produce.name);
        }
        CHECK(listed);
    }
}
