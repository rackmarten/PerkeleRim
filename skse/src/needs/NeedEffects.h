#pragma once

#include <array>

// What MSC's needs do to Kumpo in Skyrim while MSC has the player, by protocol/cpp/needs.h:
// starving or dehydrated drains health (Death::Starve, which can kill), fatigue casts PerkeleRim.esp's
// Tired (slower stamina regen), dirt raises Skyrim's barter factor (fBarterMin, fBarterMax), and the
// hands shake the longer Kumpo has been dry (Hud::ShakeHands), with his last drink kept in the
// cosave. Stress makes punches harder in Combat::OnPunch and a full bladder slows MSC's walk
// (msc/src/Link/Needs.cs). Main thread only.
namespace PerkeleRim::NeedEffects
{
    void OnDataLoaded();

    // Every frame MSC has the player: its needs (PlayerState.needs, kNeed* order), null when it sent none.
    void Update(RE::PlayerCharacter* a_player, const std::array<float, 7>* a_needs, float a_delta);

    // Skyrim has the player back: everything off, prices back to Skyrim's.
    void Stop();

    // Cosave record (save/Cosave.cpp): when Kumpo was last drunk.
    void Save(SKSE::SerializationInterface* a_intfc);
    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version);
    void Revert();
}
