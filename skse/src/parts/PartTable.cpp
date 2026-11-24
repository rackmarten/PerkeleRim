#include "parts/PartTable.h"

namespace PerkeleRim::PartTable
{
    namespace
    {
        Parts::Table table;
        bool         first = true;  // the first report of a session isn't news in the log
    }

    void OnMessage(const std::uint8_t* a_data, std::uint32_t a_size)
    {
        const auto part = Parts::Decode(a_data, a_size);
        if (!part) {
            SKSE::log::warn("parts: bad PartState ({} bytes)", a_size);
            return;
        }
        const bool known = table.All().contains(Parts::Key(part->name));
        if (table.Apply(*part) && known) {
            SKSE::log::info("parts: {} {}{}, bolts {}/{} tight, stages {}", part->name, part->Installed() ? "on the car" : "off the car",
                part->Bolted() ? ", bolted" : "", part->boltsTight, part->bolts, part->stages);
        }
        if (std::exchange(first, false)) {
            SKSE::log::info("parts: MSC reports the Satsuma's parts");
        }
    }

    void Clear()
    {
        table.Clear();
        first = true;
    }

    const Parts::Table& Get()
    {
        return table;
    }
}
