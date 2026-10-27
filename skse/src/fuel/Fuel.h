#pragma once

// The dwemer oil pumps along Skyrim's main roads from PerkeleRim.esp (tools/esp-gen): their spots go
// to MSC (FuelStations) each time the link comes up, and MSC does the rest (msc/src/Game/FuelLink.cs:
// its diesel pump at the one Kumpo is near, gold per litre, towing a dry vehicle). Without the ESP
// loaded MSC gets no pumps. Main thread only.
namespace PerkeleRim::Fuel
{
    void OnDataLoaded();  // finds the ESP's pump spots; call on kDataLoaded
    void Update();        // every frame: sends the pumps when the link came up
}
