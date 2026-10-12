#pragma once

// MSC's state is saved and loaded together with Skyrim's. Saving sends MscSave: MSC writes a
// snapshot of its state to Saves/PerkeleRim/<save name>, with a fresh key that this save's cosave
// keeps. Loading a save with a key sends MscLoad, and MSC puts that state back in place when the
// key there matches (also the reload after a death). Snapshot folders whose save is gone are
// removed on every save. A save taken while MSC had the player loads with Skyrim's controls back,
// then hands the player to MSC again.
// Main thread only.
namespace PerkeleRim::SaveSync
{
    // Cosave record (Cosave): the snapshot key and whether MSC had the player.
    void Save(SKSE::SerializationInterface* a_intfc);
    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version);  // false: not ours
    void Revert();

    void OnSaveGame(const char* a_name);  // kSaveGame (before the cosave is written)
    void OnGameLoaded();                  // kPostLoadGame
}
