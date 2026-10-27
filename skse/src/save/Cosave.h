#pragma once

// The SKSE cosave next to every save. SKSE takes one set of callbacks per plugin, so this one
// hands each record to the module it belongs to (Garage, SaveSync, NeedEffects).
namespace PerkeleRim::Cosave
{
    void Install();  // call at SKSE load
}
