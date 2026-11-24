#pragma once

#include "parts.h"

// The Satsuma's parts as MSC last reported them (PartState, v55; rules in protocol/cpp/parts.h): fitted or not and
// how tight their bolts are, for the bolt-state probe (pr.part) and the log. Main thread only.
namespace PerkeleRim::PartTable
{
    // MSC's PartState message (PlayerSync reads MSC's messages only while MSC has the player).
    void OnMessage(const std::uint8_t* a_data, std::uint32_t a_size);

    const Parts::Table& Get();
}
