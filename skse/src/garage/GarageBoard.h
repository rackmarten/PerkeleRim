#pragma once

// The garage board in the Satsuma bay (PerkeleRim.esp, an activator on a work table): Kumpo uses it
// (E, with nothing of MSC's under his hand) and Skyrim asks what to bring home: every vehicle to its
// spot by the garage, or every loose car part back to the bay (protocol GarageBay, kBayVehiclesHome
// and kBayPartsHome; Garage::RequestBay). MSC's answer shows as a HUD line. Main thread only.
namespace PerkeleRim::GarageBoard
{
    void OnDataLoaded();  // finds the board and listens for its activation; call on kDataLoaded
    void Open();          // the board's question, as using it does (the test kit's pr.garage.board)
    bool Choose(std::int32_t a_button);  // answers the open question: 0 vehicles, 1 parts, 2 neither; false: none open
}
