#include "blackout/Blackout.h"

#include "blackout_wake.h"
#include "intro/Intro.h"
#include "link/LinkService.h"
#include "player/PlayerSync.h"

namespace PerkeleRim::Blackout
{
    namespace
    {
        // Skyrim.esm's carriage travel black, as the Helgen skip uses it: FadeOutGame's fader lets go
        // before the move's load screen, so the hold keeps it black through the move and the back
        // fades it up.
        constexpr RE::FormID kFadeToBlackHoldImod = 0x0F756E;
        constexpr RE::FormID kFadeToBlackBackImod = 0x0F756F;
        constexpr float      kArrivedUnits = 512.0f;  // with the link gone: this close to the marker is there

        struct State
        {
            RE::TESImageSpaceModifier* hold = nullptr;
            RE::TESImageSpaceModifier* back = nullptr;
            Machine                    machine;
            float                      hours = 0.0f;
            RE::NiPoint3               target;
            bool                       moved = false;          // Move found a marker and moved him there
            std::uint32_t              movedGeneration = 0;     // PlayerSync's anchor when he was moved
            std::mt19937               random{ std::random_device{}() };
        } state;

        float Roll()
        {
            return std::uniform_real_distribution<float>(0.0f, 1.0f)(state.random);
        }

        // Game.FadeOutGame, a global Papyrus function; runs on the VM's next update.
        void FadeOutGame(bool a_out, float a_seconds)
        {
            auto* vm = RE::BSScript::Internal::VirtualMachine::GetSingleton();
            RE::BSTSmartPointer<RE::BSScript::IStackCallbackFunctor> callback;
            if (!vm || !vm->DispatchStaticCall("Game", "FadeOutGame", RE::MakeFunctionArguments(bool{ a_out }, true, 0.0f, float{ a_seconds }), callback)) {
                SKSE::log::warn("blackout: Game.FadeOutGame didn't go to Papyrus");
            }
        }

        // Tamriel's map markers (persistent references in its persistent cell), enabled ones only.
        std::vector<std::pair<RE::TESObjectREFR*, Marker>> Markers()
        {
            std::vector<std::pair<RE::TESObjectREFR*, Marker>> markers;
            auto* world = RE::TESForm::LookupByID<RE::TESWorldSpace>(kTamriel);
            auto* cell = world ? world->persistentCell : nullptr;
            if (!cell) {
                return markers;
            }
            cell->ForEachReference([&](RE::TESObjectREFR* a_ref) {
                const auto* extra = a_ref ? a_ref->extraList.GetByType<RE::ExtraMapMarker>() : nullptr;
                if (extra && extra->mapData && !a_ref->IsDisabled()) {
                    const auto pos = a_ref->GetPosition();
                    markers.push_back({ a_ref, Marker{ { pos.x, pos.y, pos.z }, static_cast<std::uint16_t>(extra->mapData->type.underlying()) } });
                }
                return RE::BSContainer::ForEachResult::kContinue;
            });
            return markers;
        }

        // The game time as the log shows it, "Sundas, 17th of Last Seed, 4E 201, 9:41 PM"-ish.
        std::string Now()
        {
            auto* calendar = RE::Calendar::GetSingleton();
            if (!calendar) {
                return "no calendar";
            }
            char text[128]{};
            calendar->GetTimeDateString(text, sizeof(text), false);
            return fmt::format("{} (day {:.2f})", text, calendar->GetDaysPassed());
        }

        // Skyrim's clock on by a_hours. The calendar rolls the day over once the hour passes 24.
        void PassTime(float a_hours)
        {
            auto* calendar = RE::Calendar::GetSingleton();
            if (!calendar || !calendar->gameHour || !calendar->gameDaysPassed) {
                SKSE::log::warn("blackout: no calendar, the clock stays");
                return;
            }
            const auto before = Now();
            calendar->gameHour->value += a_hours;
            calendar->gameDaysPassed->value += a_hours / 24.0f;
            SKSE::log::info("blackout: {:.1f} hours passed, {} -> hour {:.2f}, day {:.2f}", a_hours, before, calendar->gameHour->value,
                calendar->gameDaysPassed->value);
        }

        void Move(RE::PlayerCharacter* a_player)
        {
            const auto  markers = Markers();
            std::vector<Marker> wire;
            wire.reserve(markers.size());
            for (const auto& [ref, marker] : markers) {
                wire.push_back(marker);
            }
            const auto  from = a_player->GetPosition();
            const float here[3] = { from.x, from.y, from.z };
            const auto  picked = Pick(wire, here, Roll());
            state.hours = Hours(state.machine.Kind(), Roll());
            PassTime(state.hours);
            state.movedGeneration = PlayerSync::Generation();
            if (picked < 0) {
                state.moved = false;
                SKSE::log::warn("blackout: none of Tamriel's {} map markers is a place to wake up at, Kumpo stays where he fell", markers.size());
                return;
            }
            auto* marker = markers[static_cast<std::size_t>(picked)].first;
            const auto& data = markers[static_cast<std::size_t>(picked)].second;
            state.target = marker->GetPosition();
            state.moved = true;
            a_player->MoveTo(marker);
            a_player->SetHeading(marker->GetAngleZ());
            const auto* extra = marker->extraList.GetByType<RE::ExtraMapMarker>();
            const char* name = extra && extra->mapData ? extra->mapData->locationName.GetFullName() : nullptr;
            SKSE::log::info("blackout: moving Kumpo from ({:.0f},{:.0f},{:.0f}) to {} ({:08X}, type {}) at ({:.0f},{:.0f},{:.0f}), {:.0f} units away, one of {} markers",
                from.x, from.y, from.z, name && *name ? name : "a nameless marker", marker->GetFormID(), data.type, state.target.x, state.target.y,
                state.target.z, from.GetDistance(state.target), markers.size());
        }

        // The move starts no fall: landing at the marker costs no health.
        void NoFall(RE::PlayerCharacter* a_player)
        {
            if (auto* controller = a_player->GetCharController()) {
                controller->fallStartHeight = a_player->GetPositionZ();
                controller->fallTime = 0.0f;
            }
        }

        // MSC put him down on the island of the anchor sent after the move. With the link gone (no
        // handover), Skyrim's player is at the marker with its cell and 3D in.
        bool Arrived(RE::PlayerCharacter* a_player)
        {
            if (PlayerSync::Owned()) {
                return PlayerSync::Standing() && (!state.moved || PlayerSync::Generation() != state.movedGeneration);
            }
            auto* cell = a_player->GetParentCell();
            return cell && cell->IsAttached() && a_player->Is3DLoaded() &&
                   (!state.moved || a_player->GetPosition().GetDistance(state.target) < kArrivedUnits);
        }

        void Wake()
        {
            Protocol::TimePassed message{};
            message.hours = state.hours;
            message.flags = Protocol::kTimeBed | Protocol::kTimeBlackout;
            auto& link = LinkService::Get();
            if (link.IsUp() && link.Send(Protocol::MessageType::TimePassed, &message, sizeof(message))) {
                SKSE::log::info("blackout: {:.1f} hours of blackout sent to MSC", state.hours);
                Intro::WakeUp();
            } else {
                SKSE::log::warn("blackout: MSC isn't linked, its needs and clock stay");
            }
        }
    }

    void OnDataLoaded()
    {
        state.hold = RE::TESForm::LookupByID<RE::TESImageSpaceModifier>(kFadeToBlackHoldImod);
        state.back = RE::TESForm::LookupByID<RE::TESImageSpaceModifier>(kFadeToBlackBackImod);
    }

    void OnGameLoaded()
    {
        if (state.machine.Busy() && state.hold) {
            RE::ImageSpaceModifierInstanceForm::Stop(state.hold);
        }
        state.machine.Loaded();
    }

    void OnMessage(const std::uint8_t* a_data, std::uint32_t a_size)
    {
        if (!a_data || a_size < sizeof(Protocol::Blackout)) {
            SKSE::log::warn("blackout: bad Blackout ({} bytes)", a_size);
            return;
        }
        Protocol::Blackout message{};
        std::memcpy(&message, a_data, sizeof(message));
        const char* kind = message.kind == Protocol::kBlackoutKnockout ? "knocked out" : "passed out drunk";
        if (!state.machine.Start(message.kind)) {
            SKSE::log::info("blackout: Kumpo {} again while still out, ignored", kind);
            return;
        }
        SKSE::log::info("blackout: Kumpo {} (MSC), fading out", kind);
    }

    void Tick(RE::PlayerCharacter* a_player, float a_delta)
    {
        if (!a_player || state.machine.Current() == Phase::Off) {
            return;
        }
        if (state.machine.NoFall()) {
            NoFall(a_player);
        }
        auto* ui = RE::UI::GetSingleton();
        Frame frame;
        frame.loading = ui && ui->IsMenuOpen(RE::LoadingMenu::MENU_NAME);
        frame.arrived = state.machine.Current() == Phase::Moving && Arrived(a_player);
        frame.delta = a_delta;
        switch (state.machine.Tick(frame)) {
        case Step::FadeOut:
            FadeOutGame(true, kFadeOutSeconds);
            break;
        case Step::Move:
            if (state.hold) {
                RE::ImageSpaceModifierInstanceForm::Trigger(state.hold, 1.0f, nullptr);
            }
            Move(a_player);
            NoFall(a_player);
            break;
        case Step::Wake:
            SKSE::log::info("blackout: {}, fading in at {}", state.machine.GaveUp() ? "Kumpo never stood at the marker" : "Kumpo stands at the marker", Now());
            Wake();
            FadeOutGame(false, kFadeInSeconds);
            if (state.hold) {
                RE::ImageSpaceModifierInstanceForm::Stop(state.hold);
                if (state.back) {
                    RE::ImageSpaceModifierInstanceForm::Trigger(state.back, 1.0f, nullptr);
                }
            }
            break;
        default:
            break;
        }
    }
}
