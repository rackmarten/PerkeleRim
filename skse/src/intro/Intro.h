#pragma once

// Kumpo's Helgen intro (rules in protocol/cpp/intro_cues.h): a new game opens with MSC playing Kumpo
// passing out drunk while Skyrim fades up from black again and captions it, and Kumpo answers
// Ralof's "Hey, you. You're finally awake." in the cart. While Unbound (MQ101) runs nothing hands
// the player over (PlayerSync). Main thread only, except the topic sink, which only sets a flag.
namespace PerkeleRim::Intro
{
    void OnDataLoaded();  // topic sink, the quest and the fade; call on kDataLoaded
    void OnNewGame();     // call on kNewGame
    void OnGameLoaded();  // call on kPostLoadGame

    bool Running();  // the Helgen intro (Unbound) is running

    // Once a frame from the player's update: sends a cue that's due (a_msc: MSC is linked and in
    // its game) and shows its caption while Skyrim has the player (a_owned false).
    void Tick(float a_delta, bool a_msc, bool a_owned);
}
