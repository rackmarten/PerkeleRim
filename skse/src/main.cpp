// SKSE entry point. SKSE::Init sets up logging to
// Documents/My Games/Skyrim Special Edition/SKSE/PerkeleRim.log.

#include "autoload/Autoload.h"
#include "collision/CollisionSampler.h"
#include "combat/Combat.h"
#include "death/Death.h"
#include "debug/HitTest.h"
#include "drink/Drink.h"
#include "fuel/Fuel.h"
#include "garage/Garage.h"
#include "intro/Intro.h"
#include "junk/ThrowJunk.h"
#include "link/LinkService.h"
#include "menu/MscMenuView.h"
#include "player/PlayerSync.h"
#include "pocket/Pocket.h"
#include "rest/Rest.h"
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
            PerkeleRim::Fuel::OnDataLoaded();
            PerkeleRim::Intro::OnDataLoaded();
            PerkeleRim::Drink::OnDataLoaded();
            PerkeleRim::Pocket::OnDataLoaded();
            PerkeleRim::ThrowJunk::OnDataLoaded();
            PerkeleRim::CollisionSampler::OnDataLoaded();
            PerkeleRim::Combat::OnDataLoaded();
            PerkeleRim::Autoload::OnDataLoaded();
            PerkeleRim::Rest::OnDataLoaded();
            PerkeleRim::HitTest::OnDataLoaded();
            PerkeleRim::MscMenuView::OnDataLoaded();
        } else if (a_msg->type == SKSE::MessagingInterface::kPreLoadGame || a_msg->type == SKSE::MessagingInterface::kNewGame) {
            PerkeleRim::Death::Hide();  // the reload after a death
            PerkeleRim::Rest::OnGameLoad();  // before the release: MSC stops pissing and the like at once
            PerkeleRim::PlayerSync::OnGameLoad();
            if (a_msg->type == SKSE::MessagingInterface::kNewGame) {
                PerkeleRim::Intro::OnNewGame();
                PerkeleRim::PlayerSync::OnGameLoaded();
            }
        } else if (a_msg->type == SKSE::MessagingInterface::kPostLoadGame) {
            PerkeleRim::Autoload::OnGameLoaded();
            PerkeleRim::SaveSync::OnGameLoaded();
            PerkeleRim::Combat::OnGameLoaded();
            PerkeleRim::HitTest::OnGameLoaded();
            PerkeleRim::Intro::OnGameLoaded();
            if (a_msg->data) {  // the load worked (its success flag is the pointer's value)
                PerkeleRim::PlayerSync::OnGameLoaded();  // after SaveSync's MscLoad
            }
        } else if (a_msg->type == SKSE::MessagingInterface::kSaveGame) {
            PerkeleRim::SaveSync::OnSaveGame(static_cast<const char*>(a_msg->data));
        }
    });

    return true;
}
