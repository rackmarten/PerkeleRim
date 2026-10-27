#pragma once

// Skipping Helgen after Kumpo's Perkele in the cart (rules in protocol/cpp/helgen_skip.h): a slow
// fade to black, Kumpo (a Nord, named Kumpo) off the cart, Unbound completed and Before the Storm
// started on HelgenSide's side, Helgen in ruins, and a fade back in at the garage's spawn marker.
// Intro (skse/src/intro/Intro.cpp) drives it. Main thread only.
namespace PerkeleRim::HelgenSkip
{
    void OnDataLoaded();  // the quests and forms; call on kDataLoaded
    void OnNewGame();
    void OnGameLoaded();

    void Woke();             // Kumpo's Perkele went out (or would have, without MSC)
    void Tick(float a_delta);
    bool Skipping();         // faded or fading out and not at the garage yet: nothing hands over
    std::string Describe();  // Before the Storm's and the dragon attack's stages, for the log
}
