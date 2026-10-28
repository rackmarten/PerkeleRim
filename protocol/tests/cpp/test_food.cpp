#include <doctest/doctest.h>

#include <set>
#include <string>
#include <utility>

#include "../../cpp/food.h"

using namespace PerkeleRim;

TEST_CASE("a sweet roll takes the edge off hunger, a meal more")
{
    const auto* roll = Food::Find(Food::Plugin::Skyrim, 0x064B3D);
    REQUIRE(roll != nullptr);
    CHECK(std::string(roll->name) == "Sweet Roll");
    CHECK(roll->effect.hunger == doctest::Approx(-15.0f));
    CHECK_FALSE(roll->effect.Alcohol());
    CHECK(roll->effect.drink == Food::Drink::None);
    CHECK(roll->effect.empty == Food::Empty::None);

    const auto* meal = Food::Find(Food::Plugin::Skyrim, 0x0CD614);
    REQUIRE(meal != nullptr);
    CHECK(meal->effect.hunger < roll->effect.hunger);
    const auto* apple = Food::Find(Food::Plugin::Skyrim, 0x064B2E);
    REQUIRE(apple != nullptr);
    CHECK(apple->effect.hunger > roll->effect.hunger);
}

TEST_CASE("ale is MSC's beer: thirst, a little drunk, an empty bottle")
{
    const auto* ale = Food::Find(Food::Plugin::Skyrim, 0x034C5E);
    const auto* beer = Food::Find(Food::Plugin::PerkeleRim, 0x906);
    REQUIRE(ale != nullptr);
    REQUIRE(beer != nullptr);
    CHECK(ale->effect.drunk == doctest::Approx(0.08f));
    CHECK(beer->effect.drunk == doctest::Approx(ale->effect.drunk));
    CHECK(ale->effect.thirst == doctest::Approx(-20.0f));
    CHECK(ale->effect.urine > 0.0f);
    CHECK(ale->effect.drink == Food::Drink::Beer);
    CHECK(ale->effect.empty == Food::Empty::Beer);
    CHECK(ale->effect.Alcohol());
}

TEST_CASE("alcohol gets stronger from ale to mead, wine, spirits and Skooma")
{
    auto drunk = [](Food::Plugin a_plugin, std::uint32_t a_id) {
        const auto* entry = Food::Find(a_plugin, a_id);
        REQUIRE(entry != nullptr);
        return entry->effect.drunk;
    };
    const float ale = drunk(Food::Plugin::Skyrim, 0x034C5E);
    const float mead = drunk(Food::Plugin::Skyrim, 0x034C5D);
    const float blackBriar = drunk(Food::Plugin::Skyrim, 0x02C35A);
    const float wine = drunk(Food::Plugin::Skyrim, 0x03133C);
    const float brandy = drunk(Food::Plugin::Skyrim, 0x036D53);
    const float skooma = drunk(Food::Plugin::Skyrim, 0x057A7A);
    CHECK(ale < mead);
    CHECK(mead < blackBriar);
    CHECK(blackBriar < wine);
    CHECK(wine < brandy);
    CHECK(brandy < skooma);
    CHECK(skooma <= 8.0f);  // MSC's drunk tops out at 8

    // A mead bottle is beer-sized but gets him drunker; a whole bottle of wine about three beers;
    // spirits at least MSC's booze bottle.
    CHECK(mead > ale);
    CHECK(wine >= 3.0f * ale);
    CHECK(brandy >= 0.45f);
}

TEST_CASE("each drink shows its own bottle in MSC's hand and leaves its own empty")
{
    auto effect = [](Food::Plugin a_plugin, std::uint32_t a_id) {
        const auto* entry = Food::Find(a_plugin, a_id);
        REQUIRE(entry != nullptr);
        return entry->effect;
    };
    const auto mead = effect(Food::Plugin::Skyrim, 0x034C5D);
    CHECK(mead.drink == Food::Drink::Beer);
    CHECK(mead.empty == Food::Empty::Beer);
    const auto wine = effect(Food::Plugin::Skyrim, 0x03133C);
    CHECK(wine.drink == Food::Drink::Booze);
    CHECK(wine.empty == Food::Empty::Wine);
    const auto brandy = effect(Food::Plugin::Skyrim, 0x036D53);
    CHECK(brandy.drink == Food::Drink::Booze);
    CHECK(brandy.empty == Food::Empty::Liquor);
    const auto skooma = effect(Food::Plugin::Skyrim, 0x057A7A);
    CHECK(skooma.drink == Food::Drink::Spirit);
    CHECK(skooma.empty == Food::Empty::Skooma);
    const auto milk = effect(Food::Plugin::HearthFires, 0x003534);
    CHECK(milk.drink == Food::Drink::Milk);
    CHECK(milk.empty == Food::Empty::None);
    const auto juice = effect(Food::Plugin::PerkeleRim, 0x940);
    CHECK(juice.drink == Food::Drink::Juice);
    CHECK(juice.empty == Food::Empty::None);

    CHECK(Food::kEmptyIds[static_cast<int>(Food::Empty::Beer)] == 0x907);  // key 3's Empty Bottle
    CHECK(std::size(Food::kEmptyIds) == static_cast<std::size_t>(Food::Empty::Skooma) + 1);
    for (const auto id : Food::kEmptyIds) {
        CHECK(Food::Find(Food::Plugin::PerkeleRim, id) == nullptr);  // empties aren't food
    }
}

TEST_CASE("soft drinks quench thirst with no drunk")
{
    for (const auto& entry : Food::kTable) {
        if (entry.effect.drink != Food::Drink::Milk && entry.effect.drink != Food::Drink::Juice) {
            continue;
        }
        CAPTURE(entry.name);
        CHECK_FALSE(entry.effect.Alcohol());
        CHECK(entry.effect.thirst <= -20.0f);
        CHECK(entry.effect.empty == Food::Empty::None);
    }
    CHECK(Food::kWater.thirst < 0.0f);
    CHECK_FALSE(Food::kWater.Alcohol());
    CHECK(Food::kWater.drink == Food::Drink::None);  // a sip shows nothing
}

TEST_CASE("of several drinks at once MSC shows the strongest bottle")
{
    using D = Food::Drink;
    CHECK(Food::Shown(D::None, D::Beer) == D::Beer);
    CHECK(Food::Shown(D::Beer, D::None) == D::Beer);
    CHECK(Food::Shown(D::Beer, D::Booze) == D::Booze);
    CHECK(Food::Shown(D::Booze, D::Beer) == D::Booze);
    CHECK(Food::Shown(D::Booze, D::Spirit) == D::Spirit);
    CHECK(Food::Shown(D::Juice, D::Milk) == D::Milk);
    CHECK(Food::Shown(D::Milk, D::Beer) == D::Beer);
    CHECK(Food::Shown(D::None, D::Juice) == D::Juice);
}

TEST_CASE("a sip of water: crouched, on foot, ankle deep, not too often")
{
    using namespace Food::Water;
    CHECK(CanSip(0.2f, true, true, 10.0f));
    CHECK(CanSip(kSipLevel, true, true, kSipSeconds));
    CHECK_FALSE(CanSip(0.05f, true, true, 10.0f));   // a wet patch
    CHECK_FALSE(CanSip(0.2f, false, true, 10.0f));   // standing
    CHECK_FALSE(CanSip(0.2f, true, false, 10.0f));   // seated, or Skyrim has the player
    CHECK_FALSE(CanSip(0.2f, true, true, 0.5f));     // the last sip is still going
}

TEST_CASE("alcohol comes in a bottle and leaves an empty; food has no alcohol and shows nothing")
{
    for (const auto& entry : Food::kTable) {
        CAPTURE(entry.name);
        CHECK((entry.effect.empty != Food::Empty::None) == entry.effect.Alcohol());
        if (entry.effect.Alcohol()) {
            CHECK((entry.effect.drink == Food::Drink::Beer || entry.effect.drink == Food::Drink::Booze || entry.effect.drink == Food::Drink::Spirit));
        } else {
            CHECK((entry.effect.drink == Food::Drink::None || entry.effect.drink == Food::Drink::Milk || entry.effect.drink == Food::Drink::Juice));
        }
        CHECK(entry.effect.drunk >= 0.0f);
        CHECK(entry.effect.drunk < 3.0f);
        CHECK(entry.effect.hunger <= 0.0f);   // nothing makes him hungrier
        CHECK(entry.effect.hunger >= -60.0f);
        CHECK(entry.effect.thirst <= 5.0f);
        CHECK(entry.effect.urine >= 0.0f);
        CHECK((entry.effect.hunger < 0.0f || entry.effect.thirst < 0.0f));  // everything does something
    }
}

TEST_CASE("no form is listed twice")
{
    std::set<std::pair<Food::Plugin, std::uint32_t>> seen;
    for (const auto& entry : Food::kTable) {
        CAPTURE(entry.name);
        CHECK(seen.insert({ entry.plugin, entry.id }).second);
        CHECK(static_cast<std::size_t>(entry.plugin) < std::size(Food::kPluginFiles));
    }
}

TEST_CASE("MSC's own food from Skyrim's inventory")
{
    const auto* sausages = Food::Find(Food::Plugin::PerkeleRim, 0x908);
    const auto* pizza = Food::Find(Food::Plugin::PerkeleRim, 0x909);
    REQUIRE(sausages != nullptr);
    REQUIRE(pizza != nullptr);
    CHECK(pizza->effect.hunger < sausages->effect.hunger);
    CHECK(Food::Find(Food::Plugin::PerkeleRim, 0x907) == nullptr);  // the empty bottle isn't food
    CHECK(Food::Find(Food::Plugin::PerkeleRim, 0x90C) == nullptr);  // cigarettes are smoked in MSC
    CHECK(Food::Find(Food::Plugin::Dawnguard, 0x064B3D) == nullptr);  // plugin and id both count
}

TEST_CASE("food from other mods goes by its sound and weight")
{
    const auto drink = Food::Fallback(Food::Sound::Bottle, 0.5f);
    CHECK(drink.thirst < 0.0f);
    CHECK_FALSE(drink.Alcohol());
    CHECK(drink.drink == Food::Drink::Juice);  // other mods' soft drinks show MSC's juice
    CHECK(drink.empty == Food::Empty::None);
    CHECK(Food::Fallback(Food::Sound::Soup, 0.5f).drink == Food::Drink::None);
    CHECK(Food::Fallback(Food::Sound::Eat, 0.5f).drink == Food::Drink::None);
    CHECK(Food::Fallback(Food::Sound::Soup, 0.5f).thirst < 0.0f);
    CHECK(Food::Fallback(Food::Sound::Soup, 0.5f).hunger < 0.0f);

    CHECK(Food::Fallback(Food::Sound::Eat, 0.0f).hunger == doctest::Approx(-10.0f));
    CHECK(Food::Fallback(Food::Sound::Eat, 2.0f).hunger == doctest::Approx(-45.0f));
    CHECK(Food::Fallback(Food::Sound::Eat, 50.0f).hunger == doctest::Approx(-45.0f));  // a modded cheese mountain
    CHECK(Food::Fallback(Food::Sound::Eat, -1.0f).hunger == doctest::Approx(-10.0f));
    CHECK(Food::Fallback(Food::Sound::Eat, 0.1f).thirst == 0.0f);
}
