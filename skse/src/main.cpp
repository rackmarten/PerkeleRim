// SKSE entry point. SKSE::Init sets up logging to
// Documents/My Games/Skyrim Special Edition/SKSE/PerkeleRim.log.

#include "autoload/Autoload.h"
#include "collision/CollisionSampler.h"
#include "death/Death.h"
#include "drink/Drink.h"
#include "garage/Garage.h"
#include "link/LinkService.h"
#include "player/PlayerSync.h"
#include "save/Cosave.h"
#include "save/SaveSync.h"

SKSEPluginLoad(const SKSE::LoadInterface* a_skse)
{
    SKSE::Init(a_skse);

    SKSE::log::info(
        "PerkeleRim loaded (plugin {}, Skyrim runtime {})",
        SKSE::GetPluginVersion().string("."),
        a_skse->RuntimeVersion().string("."));

    PerkeleRim::LinkService::Start();
    PerkeleRim::PlayerSync::Install();
    PerkeleRim::Cosave::Install();
    SKSE::GetMessagingInterface()->RegisterListener([](SKSE::MessagingInterface::Message* a_msg) {
        if (a_msg->type == SKSE::MessagingInterface::kDataLoaded) {
            PerkeleRim::PlayerSync::OnDataLoaded();
            PerkeleRim::Garage::OnDataLoaded();
            PerkeleRim::Drink::OnDataLoaded();
            PerkeleRim::CollisionSampler::OnDataLoaded();
            PerkeleRim::Autoload::OnDataLoaded();
        } else if (a_msg->type == SKSE::MessagingInterface::kPreLoadGame || a_msg->type == SKSE::MessagingInterface::kNewGame) {
            PerkeleRim::Death::Hide();  // the reload after a death
            PerkeleRim::PlayerSync::OnGameLoad();
        } else if (a_msg->type == SKSE::MessagingInterface::kPostLoadGame) {
            PerkeleRim::Autoload::OnGameLoaded();
            PerkeleRim::SaveSync::OnGameLoaded();
        } else if (a_msg->type == SKSE::MessagingInterface::kSaveGame) {
            PerkeleRim::SaveSync::OnSaveGame(static_cast<const char*>(a_msg->data));
        }
    });

    return true;
}
