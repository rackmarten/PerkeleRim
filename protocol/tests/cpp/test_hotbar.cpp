#include <doctest/doctest.h>

#include "../../cpp/hotbar.h"

using namespace PerkeleRim;
using Hotbar::Bound;

TEST_CASE("unbound numbers keep their defaults: 1 and 2 MSC's, 3 to 8 the pocket's")
{
    for (int slot = 0; slot < Hotbar::kSlots; slot++) {
        CAPTURE(slot);
        const auto none = Hotbar::Resolve(slot, Bound::None);
        CHECK(none.mscKey == 49 + slot);
        CHECK_FALSE(none.use);
        const auto other = Hotbar::Resolve(slot, Bound::Other);  // a sword someone bound
        CHECK(other.mscKey == 49 + slot);
        CHECK_FALSE(other.use);
    }
}

TEST_CASE("the powers and MSC's items work on any number")
{
    CHECK(Hotbar::Resolve(7, Bound::Hands).mscKey == 49);
    CHECK(Hotbar::Resolve(4, Bound::Tools).mscKey == 50);
    CHECK(Hotbar::Resolve(0, Bound::Bottle).mscKey == 51);
    CHECK(Hotbar::Resolve(1, Bound::Beer).mscKey == 52);
    CHECK(Hotbar::Resolve(2, Bound::Sausages).mscKey == 53);
    CHECK(Hotbar::Resolve(2, Bound::Pizza).mscKey == 54);
    CHECK(Hotbar::Resolve(2, Bound::Chips).mscKey == 55);
    CHECK(Hotbar::Resolve(2, Bound::Macaron).mscKey == 56);
    CHECK_FALSE(Hotbar::Resolve(1, Bound::Beer).use);
}

TEST_CASE("other food and drink are used in Skyrim, nothing reaches MSC")
{
    const auto sweetroll = Hotbar::Resolve(4, Bound::Consumable);
    CHECK(sweetroll.use);
    CHECK(sweetroll.mscKey == 0);
}

TEST_CASE("a default gives way when what it stands for is bound to another number")
{
    const auto tools = Hotbar::Bit(Bound::Tools);
    CHECK(Hotbar::Resolve(2, Bound::Tools, tools).mscKey == 50);  // Tools moved to 3
    CHECK(Hotbar::Resolve(1, Bound::None, tools).mscKey == 0);    // 2 doesn't take the tools out any more
    CHECK(Hotbar::Resolve(1, Bound::Other, tools).mscKey == 0);
    CHECK(Hotbar::Resolve(0, Bound::None, tools).mscKey == 49);   // 1 is still the hand
    const auto beer = Hotbar::Bit(Bound::Beer) | Hotbar::Bit(Bound::Consumable);
    CHECK(Hotbar::Resolve(3, Bound::None, beer).mscKey == 0);     // MSC Beer on 7: 4 is free
    CHECK(Hotbar::Resolve(4, Bound::None, beer).mscKey == 53);    // food defaults stay
    CHECK(Hotbar::DefaultOf(7) == Bound::Macaron);
    CHECK(Hotbar::DefaultOf(8) == Bound::None);
}

TEST_CASE("numbers outside 1 to 8 do nothing")
{
    CHECK(Hotbar::Resolve(-1, Bound::Hands).mscKey == 0);
    CHECK(Hotbar::Resolve(8, Bound::None).mscKey == 0);
    CHECK_FALSE(Hotbar::Resolve(8, Bound::Consumable).use);
}
