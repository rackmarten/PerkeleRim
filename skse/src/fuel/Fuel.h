#pragma once

#include <string>

// The dwemer oil pumps along Skyrim's main roads from PerkeleRim.esp (tools/esp-gen): their spots go
// to MSC (FuelStations) each time the link comes up, and MSC does the rest (msc/src/Game/FuelLink.cs:
// its diesel pump at the one Kumpo is near, gold per litre, towing a dry vehicle). Skyrim adds where
// the nearest pump is to MSC's low-fuel line and tips about the tow the first time a save's tank runs
// dry (protocol/cpp/fuel_hint.h). Without the ESP loaded MSC gets no pumps. Main thread only.
namespace PerkeleRim::Fuel
{
    void OnDataLoaded();  // finds the ESP's pump spots and their map markers' names; call on kDataLoaded
    void Update();        // every frame: sends the pumps when the link came up

    // MSC's fuel line (FuelText) as the HUD shows it with a_player standing where it is: a low line
    // (kFuelTextLow) gets the nearest pump after it. A dry tank (kFuelTextDry) shows the tow tip
    // once a save.
    std::string Line(const std::string& a_msc, std::uint32_t a_flags, const RE::NiPoint3& a_player);

    void Clear();  // Skyrim has the player back: no fuel line any more

    // For the test kit's pr.fuel: the last line Line made, the flags it had, and whether this save had the tip.
    const std::string& LastLine();
    std::uint32_t      LastFlags();
    bool               Tipped();

    // Cosave record (save/Cosave.cpp): whether this save had the tow tip already.
    void Save(SKSE::SerializationInterface* a_intfc);
    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version);  // false: not ours
    void Revert();
}
