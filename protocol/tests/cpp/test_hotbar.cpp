#include <doctest/doctest.h>

#include "../../cpp/hotbar.h"

using namespace PerkeleRim;
using Hotbar::Bound;

TEST_CASE("unbound numbers do nothing: no hidden keys")
{
    for (int slot = 0; slot < Hotbar::kSlots; slot++) {
        CAPTURE(slot);
        CHECK(Hotbar::Resolve(slot, Bound::None).mscKey == 0);
        CHECK_FALSE(Hotbar::Resolve(slot, Bound::None).use);
        CHECK(Hotbar::Resolve(slot, Bound::Other).mscKey == 0);  // a sword someone bound
    }
}

TEST_CASE("the defaults bound on a save's first meeting are MSC's old keys")
{
    for (int slot = 0; slot < Hotbar::kSlots; slot++) {
        CAPTURE(slot);
        CHECK(Hotbar::Resolve(slot, Hotbar::DefaultOf(slot)).mscKey == 49 + slot);  // 1 hands .. 8 macaron box
    }
    CHECK(Hotbar::DefaultOf(7) == Bound::Macaron);
    CHECK(Hotbar::DefaultOf(8) == Bound::None);
    CHECK(Hotbar::DefaultOf(-1) == Bound::None);
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

TEST_CASE("numbers outside 1 to 8 do nothing")
{
    CHECK(Hotbar::Resolve(-1, Bound::Hands).mscKey == 0);
    CHECK(Hotbar::Resolve(8, Bound::None).mscKey == 0);
    CHECK_FALSE(Hotbar::Resolve(8, Bound::Consumable).use);
}
