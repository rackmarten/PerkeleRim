#include "save/Cosave.h"

#include "garage/Garage.h"
#include "save/SaveSync.h"

namespace PerkeleRim::Cosave
{
    namespace
    {
        constexpr std::uint32_t kCosaveId = 'PRIM';

        void Save(SKSE::SerializationInterface* a_intfc)
        {
            Garage::Save(a_intfc);
            SaveSync::Save(a_intfc);
        }

        void Load(SKSE::SerializationInterface* a_intfc)
        {
            std::uint32_t type, version, length;
            while (a_intfc->GetNextRecordInfo(type, version, length)) {
                if (!Garage::Read(a_intfc, type, version) && !SaveSync::Read(a_intfc, type, version)) {
                    SKSE::log::warn("cosave: unknown record {:08X} v{}, skipped", type, version);
                }
            }
        }

        // New game, or before a save loads: nothing a save without our records would have.
        void Revert(SKSE::SerializationInterface*)
        {
            Garage::Revert();
            SaveSync::Revert();
        }
    }

    void Install()
    {
        auto* serialization = SKSE::GetSerializationInterface();
        serialization->SetUniqueID(kCosaveId);
        serialization->SetSaveCallback(Save);
        serialization->SetLoadCallback(Load);
        serialization->SetRevertCallback(Revert);
    }
}
