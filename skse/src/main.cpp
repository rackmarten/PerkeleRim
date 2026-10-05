// SKSE entry point. SKSE::Init sets up logging to
// Documents/My Games/Skyrim Special Edition/SKSE/PerkeleRim.log.

#include "garage/Garage.h"
#include "link/LinkService.h"
#include "player/PlayerSync.h"

SKSEPluginLoad(const SKSE::LoadInterface* a_skse)
{
    SKSE::Init(a_skse);

    SKSE::log::info(
        "PerkeleRim loaded (plugin {}, Skyrim runtime {})",
        SKSE::GetPluginVersion().string("."),
        a_skse->RuntimeVersion().string("."));

    PerkeleRim::LinkService::Start();
    PerkeleRim::PlayerSync::Install();
    PerkeleRim::Garage::Install();
    SKSE::GetMessagingInterface()->RegisterListener([](SKSE::MessagingInterface::Message* a_msg) {
        if (a_msg->type == SKSE::MessagingInterface::kDataLoaded) {
            PerkeleRim::PlayerSync::OnDataLoaded();
            PerkeleRim::Garage::OnDataLoaded();
        }
    });

    return true;
}
