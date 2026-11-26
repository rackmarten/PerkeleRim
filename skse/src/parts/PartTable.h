#pragma once

#include "parts.h"

// The Satsuma's parts as MSC last reported them (PartState, v58; rules in protocol/cpp/parts.h): fitted or not and
// how tight their bolts are, for the bolt-state probe (pr.part) and the log. Main thread only.
namespace PerkeleRim::PartTable
{
    // MSC's PartState message (PlayerSync reads MSC's messages only while MSC has the player).
    void OnMessage(const std::uint8_t* a_data, std::uint32_t a_size);

    // Forgets every part: at each handover, when MSC sends them all again from scratch.
    void Clear();

    const Parts::Table& Get();
}
