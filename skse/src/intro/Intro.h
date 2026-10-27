#pragma once

// Kumpo's Helgen intro (rules in protocol/cpp/intro_cues.h): a new game opens with MSC playing Kumpo
// passing out drunk, snoring and waking up hungover while Skyrim fades up from black again and
// captions it, and Kumpo says Perkele after Ralof's "Hey, you. You're finally awake." in the cart;
// a few seconds later Helgen is skipped (HelgenSkip). Waking up in the shack plays the same hangover line (WakeUp). While Unbound (MQ101) runs nothing hands
// the player over (PlayerSync). Main thread only, except the topic sink, which only sets a flag.
namespace PerkeleRim::Intro
{
    void OnDataLoaded();  // topic sink, the quest and the fade; call on kDataLoaded
    void OnNewGame();     // call on kNewGame
    void OnGameLoaded();  // call on kPostLoadGame

    bool        Running();   // the Helgen intro (Unbound) is running
    std::string Describe();  // Unbound's stage and flags, for the log

    // Kumpo wakes up in the shack on a save's first handover: MSC plays the hangover line now.
    void WakeUp();

    // Once a frame from the player's update: sends a cue that's due (a_msc: MSC is linked and in
    // its game) and shows its caption while Skyrim has the player (a_owned false; while MSC has it,
    // PlayerSync shows Caption() as the subtitle line).
    void Tick(float a_delta, bool a_msc, bool a_owned);
    std::string_view Caption();  // the caption still showing, or empty
}
