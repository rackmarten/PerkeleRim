#pragma once

// The pocket on Skyrim's side (docs/PROTOCOL.md "The pocket", "Food and drink", "The hotbar"):
// Skyrim's inventory holds MSC's beer, empty bottles, food and cigarettes as PerkeleRim.esp items
// (tools/esp-gen), so they travel through load doors and live in Skyrim's saves, and its gold is
// MSC's money.
//  - MSC's PocketChange adds or removes them (Y pockets, keys 3 to 8 drink, eat or take out, money
//    spent or earned in MSC);
//  - the counts go back to MSC as PocketCounts when they change, whoever changed them;
//  - food and drink Kumpo uses in Skyrim go to MSC's needs as a ConsumeEvent (protocol/cpp/food.h),
//    and a bottle of alcohol leaves an Empty Bottle;
//  - number keys 1 to 8 while MSC has the player do what Skyrim's favorites hotkeys hold
//    (protocol/cpp/hotbar.h); the player gets the ESP's three hotbar powers to bind.
// Without the ESP loaded all of it stays off. Main thread only.
namespace PerkeleRim::Pocket
{
    void OnDataLoaded();  // finds the ESP's records, watches what the player eats; call on kDataLoaded

    // Every frame, owned or not: sends the counts to MSC when they changed or the link came up.
    void Update(RE::PlayerCharacter* a_player, float a_delta);

    void OnChange(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);  // PocketChange

    // Kumpo's money for the HUD: Skyrim's gold, -1 without the ESP.
    std::int64_t Money(RE::PlayerCharacter* a_player);

    // Number key a_slot (0 for 1 .. 7 for 8) went down while MSC has the player: the Unity KeyCode
    // MSC gets held for it (0 for none). Food or drink on the hotkey is used here and now.
    std::uint16_t Hotkey(RE::PlayerCharacter* a_player, int a_slot);
}
