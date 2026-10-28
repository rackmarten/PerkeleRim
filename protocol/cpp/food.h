// What Skyrim's food and drink do to Kumpo's MSC needs (docs/PROTOCOL.md "Food and drink"). The player
// eats or drinks in Skyrim (its inventory, a hotkey) and Skyrim sends MSC a ConsumeEvent with these
// numbers. MSC's scale: hunger and thirst rise about 110 an hour of play (one tick +1.42 / +1.83 every
// 45 s), its HUD bar fills at 100 and 200 kills. MSC's own beer takes about 20 thirst and adds about
// 0.08 drunk (PlayerDrunkAdjusted; five beers about 0.4), its booze bottle about 0.45: the
// yardsticks for the alcohol below. Each drink also says which of MSC's drinks it shows (Drink, the
// protocol's kDrink*) and which empty bottle it leaves in Skyrim's inventory (Empty).
// Vanilla and DLC food is listed by form id; anything else (other mods' food) goes by its use sound
// and weight (Fallback). Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <algorithm>
#include <cstdint>
#include <span>

namespace PerkeleRim::Food
{
    // The plugin a listed form id is local to.
    enum class Plugin : std::uint8_t
    {
        Skyrim,       // Skyrim.esm
        Dawnguard,    // Dawnguard.esm
        HearthFires,  // HearthFires.esm
        Dragonborn,   // Dragonborn.esm
        PerkeleRim,   // PerkeleRim.esp (MSC's food, beer and juice, tools/esp-gen)
    };

    inline constexpr const char* kPluginFiles[] = { "Skyrim.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm", "PerkeleRim.esp" };

    // MSC's drinking shown for it, once Skyrim's menu is closed: the protocol's kDrink* values.
    enum class Drink : std::uint8_t
    {
        None,    // food, or nothing to show
        Beer,    // DRINKBEER: ale, mead
        Booze,   // DRINKVIINA, the booze bottle: wine, brandy, Sujamma
        Spirit,  // DRINKSPIRIT, the spirit bottle: Skooma (MSC's blindness roll is blocked)
        Milk,    // DRINKMILK
        Juice,   // DRINKJUICE: juice, other mods' soft drinks
    };

    // The empty bottle a drink leaves in Skyrim's inventory: PerkeleRim.esp misc items (kEmptyIds).
    enum class Empty : std::uint8_t
    {
        None,
        Beer,    // 0x907 Empty Bottle, the one key 3 takes into MSC's hand
        Wine,    // Empty Wine Bottle
        Liquor,  // Empty Liquor Bottle: brandy, Sujamma, Flin
        Skooma,  // Empty Skooma Bottle
    };

    inline constexpr std::uint32_t kEmptyIds[] = { 0, 0x907, 0x941, 0x942, 0x943 };  // by Empty, local to PerkeleRim.esp

    // Added to MSC's needs. Negative hunger and thirst: less hungry, less thirsty.
    struct Effect
    {
        float hunger = 0.0f;
        float thirst = 0.0f;
        float urine = 0.0f;
        float drunk = 0.0f;
        Drink drink = Drink::None;  // MSC's drinking shown for it
        Empty empty = Empty::None;  // left in the inventory

        constexpr bool Alcohol() const { return drunk > 0.0f; }
    };

    // Of several drinks at once (one menu), the one MSC shows: the strongest bottle.
    constexpr Drink Shown(Drink a_was, Drink a_now)
    {
        constexpr int rank[] = { 0, 3, 4, 5, 2, 1 };  // None, Beer, Booze, Spirit, Milk, Juice
        return rank[static_cast<int>(a_now)] > rank[static_cast<int>(a_was)] ? a_now : a_was;
    }

    // Kinds of food, so the table reads as a menu.
    inline constexpr Effect kNibble{ -5.0f };                    // a leek, a potato, butter, flour
    inline constexpr Effect kFruit{ -8.0f, -3.0f };              // apples, a tomato, a gourd
    inline constexpr Effect kSnack{ -12.0f };                    // a treat, a wedge of cheese, raw meat bits
    inline constexpr Effect kSweet{ -15.0f, 2.0f };              // the sweet roll, bread, pies
    inline constexpr Effect kRaw{ -15.0f };                      // raw meat: eaten anyway, he's Finnish
    inline constexpr Effect kCooked{ -30.0f };                   // grilled or roasted, one portion
    inline constexpr Effect kRoast{ -45.0f };                    // a big piece: a haunch, a steak, a loaf
    inline constexpr Effect kCheeseWheel{ -50.0f, 5.0f };
    inline constexpr Effect kMeal{ -60.0f, -10.0f, 2.0f };       // a homecooked meal
    inline constexpr Effect kSoup{ -25.0f, -20.0f, 4.0f };
    inline constexpr Effect kStew{ -40.0f, -15.0f, 4.0f };
    inline constexpr Effect kMilk{ -10.0f, -25.0f, 6.0f, 0.0f, Drink::Milk };
    inline constexpr Effect kJuice{ -5.0f, -25.0f, 6.0f, 0.0f, Drink::Juice };
    inline constexpr Effect kDrink{ 0.0f, -20.0f, 5.0f, 0.0f, Drink::Juice };  // a soft drink that isn't listed
    inline constexpr Effect kWater{ 0.0f, -10.0f, 2.0f };                      // a sip from a lake or a river

    // Alcohol by strength: a beer-sized bottle quenches thirst like MSC's beer, wine and spirits less.
    // Ale and mead show MSC's beer bottle, wine and spirits its booze bottle, Skooma its spirit.
    constexpr Effect Ale(float a_drunk) { return { -3.0f, -20.0f, 6.0f, a_drunk, Drink::Beer, Empty::Beer }; }
    constexpr Effect Wine(float a_drunk) { return { -2.0f, -12.0f, 4.0f, a_drunk, Drink::Booze, Empty::Wine }; }
    constexpr Effect Liquor(float a_drunk) { return { -1.0f, -5.0f, 2.0f, a_drunk, Drink::Booze, Empty::Liquor }; }
    constexpr Effect Skooma(float a_drunk) { return { -1.0f, -5.0f, 2.0f, a_drunk, Drink::Spirit, Empty::Skooma }; }

    struct Entry
    {
        Plugin        plugin;
        std::uint32_t id;  // local form id in plugin
        const char*   name;
        Effect        effect;
    };

    // PerkeleRim.esp's MSC items are 0x906 + kPocket* (Pocket.cpp, tools/esp-gen).
    inline constexpr Entry kTable[] = {
        // Alcohol. MSC beer is the yardstick: Skyrim's ale the same, meads a little stronger (a bottle
        // as big as a beer), a whole bottle of wine about three beers, spirits like MSC's booze
        // (+0.45 a bottle) or more, and Skooma a joke.
        { Plugin::PerkeleRim, 0x906, "MSC Beer", Ale(0.08f) },
        { Plugin::Skyrim, 0x034C5E, "Ale", Ale(0.08f) },
        { Plugin::Skyrim, 0x034C5D, "Nord Mead", Ale(0.10f) },
        { Plugin::Skyrim, 0x107A8A, "Mead with Juniper Berry", Ale(0.10f) },
        { Plugin::Skyrim, 0x0555E8, "Dragon's Breath Mead", Ale(0.12f) },
        { Plugin::Skyrim, 0x0508CA, "Honningbrew Mead", Ale(0.12f) },
        { Plugin::Skyrim, 0x02C35A, "Black-Briar Mead", Ale(0.15f) },
        { Plugin::Skyrim, 0x0F693F, "Black-Briar Reserve", Ale(0.25f) },
        { Plugin::Dragonborn, 0x03572F, "Ashfire Mead", Ale(0.15f) },
        { Plugin::Dragonborn, 0x0248CE, "Matze", Ale(0.10f) },
        { Plugin::Dragonborn, 0x0248CC, "Shein", Wine(0.20f) },
        { Plugin::Skyrim, 0x03133C, "Wine", Wine(0.25f) },
        { Plugin::Skyrim, 0x0C5348, "Wine", Wine(0.25f) },
        { Plugin::Skyrim, 0x03133B, "Alto Wine", Wine(0.25f) },
        { Plugin::Skyrim, 0x0C5349, "Alto Wine", Wine(0.25f) },
        { Plugin::Skyrim, 0x0F257E, "Jessica's Wine", Wine(0.25f) },
        { Plugin::Skyrim, 0x085368, "Spiced Wine", Wine(0.25f) },
        { Plugin::Skyrim, 0x01895F, "Firebrand Wine", Wine(0.28f) },
        { Plugin::Dragonborn, 0x0320DF, "Emberbrand Wine", Wine(0.28f) },
        { Plugin::HearthFires, 0x003536, "Surilie Brothers Wine", Wine(0.25f) },
        { Plugin::HearthFires, 0x003535, "Argonian Bloodwine", Wine(0.28f) },
        { Plugin::Skyrim, 0x036D53, "Colovian Brandy", Liquor(0.50f) },
        { Plugin::Dragonborn, 0x0207E6, "Sujamma", Liquor(0.50f) },
        { Plugin::Dragonborn, 0x024E0B, "Sadri's Sujamma", Liquor(0.60f) },
        { Plugin::Dragonborn, 0x0207E5, "Flin", Liquor(0.45f) },
        { Plugin::Skyrim, 0x057A7A, "Skooma", Skooma(1.5f) },
        { Plugin::Skyrim, 0x057A7B, "Kordir's Skooma", Skooma(1.5f) },
        { Plugin::Dawnguard, 0x01391D, "Redwater Skooma", Skooma(2.0f) },
        { Plugin::Skyrim, 0x03F4BD, "Double-Distilled Skooma", Skooma(2.5f) },

        // MSC's own food and juice, as Skyrim items.
        { Plugin::PerkeleRim, 0x940, "Juice", kJuice },
        { Plugin::PerkeleRim, 0x908, "Sausages", kCooked },
        { Plugin::PerkeleRim, 0x909, "Pizza", kRoast },
        { Plugin::PerkeleRim, 0x90A, "Potato Chips", { -15.0f, 5.0f } },
        { Plugin::PerkeleRim, 0x90B, "Macaron Box", { -20.0f, 2.0f } },

        // Soup, stew and milk: food and drink at once.
        { Plugin::Skyrim, 0x0F4320, "Elsweyr Fondue", kSoup },
        { Plugin::Skyrim, 0x0F431E, "Vegetable Soup", kSoup },
        { Plugin::Skyrim, 0x0F431C, "Tomato Soup", kSoup },
        { Plugin::Skyrim, 0x0F431B, "Cabbage Potato Soup", kSoup },
        { Plugin::Skyrim, 0x0EBA02, "Cabbage Soup", kSoup },
        { Plugin::HearthFires, 0x00353E, "Clam Chowder", kSoup },
        { Plugin::HearthFires, 0x00353D, "Potato Soup", kSoup },
        { Plugin::Skyrim, 0x0F431D, "Venison Stew", kStew },
        { Plugin::Skyrim, 0x0F4315, "Horker Stew", kStew },
        { Plugin::Skyrim, 0x0F4314, "Beef Stew", kStew },
        { Plugin::Skyrim, 0x0EBA01, "Apple Cabbage Stew", kStew },
        { Plugin::Dragonborn, 0x03CD5B, "Horker and Ash Yam Stew", kStew },
        { Plugin::HearthFires, 0x003534, "Jug of Milk", kMilk },

        // Sweet things and bread.
        { Plugin::Skyrim, 0x064B3D, "Sweet Roll", kSweet },
        { Plugin::Skyrim, 0x065C98, "Bread", kSweet },
        { Plugin::Skyrim, 0x065C97, "Bread", kSweet },
        { Plugin::HearthFires, 0x0009DC, "Garlic Bread", kSweet },
        { Plugin::HearthFires, 0x0009DB, "Braided Bread", kSweet },
        { Plugin::HearthFires, 0x003537, "Potato Bread", kSweet },
        { Plugin::Skyrim, 0x064B43, "Apple Pie", kSweet },
        { Plugin::HearthFires, 0x00353B, "Snowberry Crostata", kSweet },
        { Plugin::HearthFires, 0x00353A, "Jazbay Crostata", kSweet },
        { Plugin::HearthFires, 0x003539, "Juniper Berry Crostata", kSweet },
        { Plugin::HearthFires, 0x011801, "Lavender Dumpling", kSweet },
        { Plugin::HearthFires, 0x0117FF, "Chicken Dumpling", kSweet },
        { Plugin::HearthFires, 0x003533, "Apple Dumpling", kSweet },
        { Plugin::Skyrim, 0x064B30, "Boiled Creme Treat", kSnack },
        { Plugin::Skyrim, 0x064B39, "Long Taffy Treat", kSnack },
        { Plugin::Skyrim, 0x064B38, "Honey Nut Treat", kSnack },
        { Plugin::Skyrim, 0x10394D, "Honey", kSnack },

        // Cheese.
        { Plugin::Skyrim, 0x064B36, "Sliced Eidar Cheese", kCheeseWheel },
        { Plugin::Skyrim, 0x064B35, "Sliced Goat Cheese", kCheeseWheel },
        { Plugin::Skyrim, 0x064B34, "Eidar Cheese Wheel", kCheeseWheel },
        { Plugin::Skyrim, 0x064B33, "Goat Cheese Wheel", kCheeseWheel },
        { Plugin::Skyrim, 0x064B32, "Eidar Cheese Wedge", kSnack },
        { Plugin::Skyrim, 0x064B31, "Goat Cheese Wedge", kSnack },
        { Plugin::Skyrim, 0x0669A3, "Mammoth Cheese Bowl", kCooked },

        // Fruit and vegetables.
        { Plugin::Skyrim, 0x064B2F, "Green Apple", kFruit },
        { Plugin::Skyrim, 0x064B2E, "Red Apple", kFruit },
        { Plugin::Skyrim, 0x064B42, "Tomato", kFruit },
        { Plugin::Skyrim, 0x10D666, "Gourd", kFruit },
        { Plugin::Skyrim, 0x064B3F, "Cabbage", kFruit },
        { Plugin::Skyrim, 0x064B41, "Potato", kNibble },
        { Plugin::Skyrim, 0x064B40, "Carrot", kNibble },
        { Plugin::Skyrim, 0x0669A5, "Leek", kNibble },
        { Plugin::Dragonborn, 0x0206E7, "Ash Yam", kNibble },
        { Plugin::HearthFires, 0x00353C, "Butter", kNibble },
        { Plugin::HearthFires, 0x003538, "Sack of Flour", kNibble },
        { Plugin::Dawnguard, 0x014DC4, "Soul Husk", kNibble },
        { Plugin::Skyrim, 0x064B3E, "Grilled Leeks", kSnack },
        { Plugin::Skyrim, 0x064B3A, "Baked Potatoes", kSnack },

        // Cooked meat and fish.
        { Plugin::Skyrim, 0x0E8947, "Grilled Chicken Breast", kCooked },
        { Plugin::Skyrim, 0x0E8448, "Charred Skeever Meat", kCooked },
        { Plugin::Skyrim, 0x0CADFB, "Spiced Beef", kCooked },
        { Plugin::Skyrim, 0x0722C7, "Pheasant Roast", kCooked },
        { Plugin::Skyrim, 0x0722C2, "Rabbit Haunch", kCooked },
        { Plugin::Skyrim, 0x0721E8, "Cooked Beef", kCooked },
        { Plugin::Skyrim, 0x064B3C, "Seared Slaughterfish", kCooked },
        { Plugin::Skyrim, 0x064B3B, "Salmon Steak", kCooked },
        { Plugin::HearthFires, 0x003541, "Salmon Steak", kCooked },
        { Plugin::HearthFires, 0x00353F, "Steamed Mudcrab Legs", kCooked },
        { Plugin::Skyrim, 0x0722BD, "Venison Chop", kRoast },
        { Plugin::Skyrim, 0x0722BB, "Mammoth Steak", kRoast },
        { Plugin::Skyrim, 0x0722B0, "Horse Haunch", kRoast },
        { Plugin::Skyrim, 0x07224E, "Horker Loaf", kRoast },
        { Plugin::Skyrim, 0x07224C, "Leg of Goat Roast", kRoast },
        { Plugin::Dragonborn, 0x03CF72, "Cooked Boar Meat", kRoast },
        { Plugin::Skyrim, 0x0CD614, "Homecooked Meal", kMeal },

        // Raw meat and fish.
        { Plugin::Skyrim, 0x10211A, "Mammoth Snout", kRaw },
        { Plugin::Skyrim, 0x0669A4, "Mammoth Snout", kRaw },
        { Plugin::Skyrim, 0x0F2011, "Chicken Breast", kRaw },
        { Plugin::Skyrim, 0x0EDB2E, "Dog Meat", kRaw },
        { Plugin::Skyrim, 0x0669A2, "Venison", kRaw },
        { Plugin::Skyrim, 0x065C9C, "Horse Meat", kRaw },
        { Plugin::Skyrim, 0x065C9B, "Horker Meat", kRaw },
        { Plugin::Skyrim, 0x065C9A, "Leg of Goat", kRaw },
        { Plugin::Skyrim, 0x065C99, "Raw Beef", kRaw },
        { Plugin::Dragonborn, 0x03BD14, "Boar Meat", kRaw },
        { Plugin::Dragonborn, 0x03D125, "Ash Hopper Leg", kRaw },
        { Plugin::Dragonborn, 0x03BD15, "Ash Hopper Meat", kSnack },
        { Plugin::Skyrim, 0x065C9F, "Salmon Meat", kSnack },
        { Plugin::Skyrim, 0x065C9E, "Raw Rabbit Leg", kSnack },
        { Plugin::Skyrim, 0x065C9D, "Pheasant Breast", kSnack },
        { Plugin::Skyrim, 0x0EBA03, "Clam Meat", kNibble },
        { Plugin::HearthFires, 0x003540, "Mudcrab Legs", kSnack },
    };

    constexpr const Entry* Find(Plugin a_plugin, std::uint32_t a_id)
    {
        for (const auto& entry : kTable) {
            if (entry.plugin == a_plugin && entry.id == a_id) {
                return &entry;
            }
        }
        return nullptr;
    }

    // How an unlisted ingestible sounds when used (its consume sound), which is how Skyrim tells
    // drinks from food: vanilla bottles play ITMPotionUse, soups and milk ITMFoodEatSoup, the rest
    // ITMFoodEat.
    enum class Sound : std::uint8_t
    {
        Eat,
        Soup,
        Bottle,
    };

    inline constexpr std::uint32_t kSoundEat = 0x0CAF94;     // Skyrim.esm sound descriptors
    inline constexpr std::uint32_t kSoundSoup = 0x10E2EA;
    inline constexpr std::uint32_t kSoundBottle = 0x0B6435;

    // Food from other mods, by its use sound and weight: bottles are soft drinks (thirst, MSC's
    // juice drinking; no alcohol is guessed, and no empty bottle), soup is soup, food is snack sized
    // up to a roast for 2 and more.
    constexpr Effect Fallback(Sound a_sound, float a_weight)
    {
        switch (a_sound) {
        case Sound::Bottle: return kDrink;
        case Sound::Soup: return kSoup;
        default: break;
        }
        const float weight = std::clamp(a_weight, 0.0f, 2.0f);
        return { -(10.0f + 17.5f * weight) };  // 10 for a crumb, 45 for a roast
    }

    // Drinking water: crouched (MSC's crouch is Skyrim's sneak) in water at least ankle deep, E
    // with nothing under Skyrim's crosshair is a sip (kWater), at most one every kSipSeconds.
    namespace Water
    {
        inline constexpr float kSipLevel = 0.1f;    // submerged fraction at the feet (GetSubmergeLevel)
        inline constexpr float kSipSeconds = 1.5f;  // a sip's length: holding or mashing E doesn't drink a lake

        constexpr bool CanSip(float a_level, bool a_crouched, bool a_onFoot, float a_sinceLast)
        {
            return a_onFoot && a_crouched && a_level >= kSipLevel && a_sinceLast >= kSipSeconds;
        }
    }
}
