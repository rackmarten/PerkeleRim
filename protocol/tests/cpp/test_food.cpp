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
    CHECK_FALSE(roll->effect.bottle);

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
    CHECK(ale->effect.bottle);
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
}

TEST_CASE("every listed drink is a bottle, every bottle alcohol; food has no alcohol")
{
    for (const auto& entry : Food::kTable) {
        CAPTURE(entry.name);
        CHECK(entry.effect.bottle == entry.effect.Alcohol());
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
    CHECK_FALSE(drink.bottle);
    CHECK(Food::Fallback(Food::Sound::Soup, 0.5f).thirst < 0.0f);
    CHECK(Food::Fallback(Food::Sound::Soup, 0.5f).hunger < 0.0f);

    CHECK(Food::Fallback(Food::Sound::Eat, 0.0f).hunger == doctest::Approx(-10.0f));
    CHECK(Food::Fallback(Food::Sound::Eat, 2.0f).hunger == doctest::Approx(-45.0f));
    CHECK(Food::Fallback(Food::Sound::Eat, 50.0f).hunger == doctest::Approx(-45.0f));  // a modded cheese mountain
    CHECK(Food::Fallback(Food::Sound::Eat, -1.0f).hunger == doctest::Approx(-10.0f));
    CHECK(Food::Fallback(Food::Sound::Eat, 0.1f).thirst == 0.0f);
}
