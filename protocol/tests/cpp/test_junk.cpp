#include <doctest/doctest.h>

#include <vector>

#include "../../cpp/junk.h"

using namespace PerkeleRim;
using Junk::Item;
using Junk::Kind;

namespace
{
    Item Clutter(std::uint32_t a_form, std::int32_t a_value, float a_weight = 1.0f, bool a_bottle = false)
    {
        return { Kind::kClutter, false, a_bottle, a_weight, a_value, a_form };
    }

    Item Produce(std::uint32_t a_form, std::int32_t a_value, float a_weight = 0.1f)
    {
        return { Kind::kProduce, false, false, a_weight, a_value, a_form };
    }
}

TEST_CASE("only clutter and produce are junk, never quest items or heavy ones")
{
    CHECK(Junk::Throwable(Clutter(1, 1)));
    CHECK(Junk::Throwable(Produce(2, 1)));
    CHECK_FALSE(Junk::Throwable(Item{ Kind::kOther, false, false, 1.0f, 1, 3 }));  // a pelt, a garnet, a lockpick
    auto quest = Clutter(4, 1);
    quest.quest = true;
    CHECK_FALSE(Junk::Throwable(quest));
    CHECK(Junk::Throwable(Clutter(5, 1, Junk::kWeightMax)));
    CHECK_FALSE(Junk::Throwable(Clutter(6, 1, Junk::kWeightMax + 0.5f)));
}

TEST_CASE("nothing to throw picks nothing")
{
    CHECK(Junk::Pick({}) == -1);
    const std::vector<Item> none = { Item{ Kind::kOther, false, false, 1.0f, 1, 1 } };
    CHECK(Junk::Pick(none) == -1);
}

TEST_CASE("bottles first, then the cheapest clutter, produce last")
{
    const std::vector<Item> items = {
        Produce(0x10, 0),                 // a cabbage: free, but produce goes last
        Clutter(0x20, 5),                 // a tankard
        Clutter(0x30, 2),                 // a plate: cheaper
        Clutter(0x40, 9, 0.5f, true),     // an empty wine bottle: dearer, but a bottle
    };
    CHECK(Junk::Pick(items) == 3);
    std::vector<Item> left = items;
    left.erase(left.begin() + 3);
    CHECK(Junk::Pick(left) == 2);  // the plate
    left.erase(left.begin() + 2);
    CHECK(Junk::Pick(left) == 1);  // the tankard
    left.erase(left.begin() + 1);
    CHECK(Junk::Pick(left) == 0);  // the cabbage, once the clutter is gone
}

TEST_CASE("equal value: the lighter one, then the lower form id")
{
    const std::vector<Item> items = { Clutter(0x50, 3, 2.0f), Clutter(0x60, 3, 1.0f), Clutter(0x40, 3, 1.0f) };
    CHECK(Junk::Pick(items) == 2);
}

TEST_CASE("a bottle is a bottle in any case, anywhere in the name")
{
    CHECK(Junk::IsBottleName("Empty Wine Bottle"));
    CHECK(Junk::IsBottleName("BOTTLE"));
    CHECK(Junk::IsBottleName("bottled"));
    CHECK_FALSE(Junk::IsBottleName("Tankard"));
    CHECK_FALSE(Junk::IsBottleName("bott"));
    CHECK_FALSE(Junk::IsBottleName(""));
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
