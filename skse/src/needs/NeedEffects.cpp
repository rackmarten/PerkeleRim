#include "needs/NeedEffects.h"

#include "death/Death.h"
#include "hud/Hud.h"
#include "needs.h"

namespace PerkeleRim::NeedEffects
{
    namespace
    {
        constexpr auto          kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID    kTiredSpell = 0x920;  // PerkeleRimTiredStaminaRate: detrimental, magnitude set per cast, kTiredSeconds
        constexpr float         kTiredSeconds = 10.0f;  // its duration (esp-gen): a save taken meanwhile loses it soon after
        constexpr float         kTiredRecast = 5.0f;
        static_assert(kTiredRecast < kTiredSeconds);
        constexpr std::uint32_t kRecord = 'NEED';
        constexpr std::uint32_t kVersion = 1;

        struct State
        {
            RE::SpellItem* tired = nullptr;
            float          tiredApplied = 0.0f;  // regen cut cast, 0 = none on
            float          tiredAge = 0.0f;
            RE::Setting*   barterMin = nullptr;
            RE::Setting*   barterMax = nullptr;
            float          ownBarterMin = 0.0f, ownBarterMax = 0.0f;  // Skyrim's (or another mod's) values
            float          price = 1.0f;                               // the factor they are scaled by now
            bool           starving = false;
            Needs::DryClock dry;
            float          clock = 0.0f;   // seconds, drives the shake's waves
            int            shakeLogged = -1;  // tenth of the shake last logged
        } state;

        void TiredOff(RE::PlayerCharacter* a_player)
        {
            if (state.tired && state.tiredApplied > 0.0f) {
                if (auto* target = a_player->AsMagicTarget()) {
                    auto caster = a_player->GetHandle();
                    target->DispelEffect(state.tired, caster);
                }
            }
            state.tiredApplied = 0.0f;
        }

        void Tired(RE::PlayerCharacter* a_player, float a_fatigue, float a_delta)
        {
            state.tiredAge += a_delta;
            const float cut = Needs::FatigueRegenCut(a_fatigue);
            const bool  on = state.tiredApplied > 0.0f;
            if (!state.tired || (cut <= 0.0f && !on)) {
                return;
            }
            if (on && std::abs(cut - state.tiredApplied) < Needs::kRegenStep && state.tiredAge < kTiredRecast) {
                return;
            }
            // A recast stacks next to the running one, so the old one goes first (as Combat::Drunk).
            TiredOff(a_player);
            if (cut <= 0.0f) {
                SKSE::log::info("needs: rested, stamina regen is back");
                return;
            }
            auto* caster = a_player->GetMagicCaster(RE::MagicSystem::CastingSource::kInstant);
            if (!caster) {
                return;
            }
            caster->CastSpellImmediate(state.tired, false, a_player, 1.0f, false, cut, a_player);
            if (!on || std::abs(cut - state.tiredApplied) >= 10.0f) {
                SKSE::log::info("needs: fatigue {:.0f}, stamina regen {:.0f}% slower", a_fatigue, cut);
            }
            state.tiredApplied = cut;
            state.tiredAge = 0.0f;
        }

        void SetPrice(float a_price)
        {
            if (!state.barterMin || !state.barterMax || a_price == state.price ||
                (a_price != 1.0f && std::abs(a_price - state.price) < Needs::kPriceStep)) {
                return;
            }
            state.barterMin->data.f = state.ownBarterMin * a_price;
            state.barterMax->data.f = state.ownBarterMax * a_price;
            SKSE::log::info("needs: traders' prices x{:.2f} (fBarterMin {:.2f}, fBarterMax {:.2f})", a_price, state.barterMin->data.f,
                state.barterMax->data.f);
            state.price = a_price;
        }

        void Starve(RE::PlayerCharacter* a_player, float a_hunger, float a_thirst, float a_delta)
        {
            const float max = a_player->AsActorValueOwner()->GetPermanentActorValue(RE::ActorValue::kHealth);
            const float drain = Needs::StarveDrain(a_hunger, a_thirst, max);
            if ((drain > 0.0f) != state.starving) {
                state.starving = drain > 0.0f;
                SKSE::log::info("needs: {} (hunger {:.0f}, thirst {:.0f})", state.starving ? "starving, health drains" : "fed, health stops draining",
                    a_hunger, a_thirst);
            }
            Death::Starve(a_player, drain * a_delta);
        }

        void Shake(float a_drunk, float a_delta)
        {
            auto* calendar = RE::Calendar::GetSingleton();
            if (!calendar) {
                return;
            }
            const float hours = calendar->GetHoursPassed();
            state.dry.Step(hours, a_drunk);
            const float dry = state.dry.DryHours(hours);
            const float shake = Needs::Shake(dry, a_drunk);
            state.clock = std::fmod(state.clock + a_delta, 1000.0f);
            const auto offset = Needs::ShakeOffset(state.clock, shake, 1.0f);
            Hud::ShakeHands(shake, offset.x, offset.y);
            const int tenth = static_cast<int>(shake * 10.0f);
            if (tenth != state.shakeLogged) {
                state.shakeLogged = tenth;
                SKSE::log::info("needs: Kumpo's hands shake {:.1f} ({:.1f} hours dry, drunk {:.2f})", shake, dry, a_drunk);
            }
        }
    }

    void OnDataLoaded()
    {
        auto* data = RE::TESDataHandler::GetSingleton();
        state.tired = data ? data->LookupForm<RE::SpellItem>(kTiredSpell, kPlugin) : nullptr;
        SKSE::log::info("needs: Tired spell {}", state.tired ? "found" : "missing (PerkeleRim.esp not loaded or too old), fatigue doesn't slow stamina regen");
        if (auto* settings = RE::GameSettingCollection::GetSingleton()) {
            state.barterMin = settings->GetSetting("fBarterMin");
            state.barterMax = settings->GetSetting("fBarterMax");
        }
        if (state.barterMin && state.barterMax) {
            state.ownBarterMin = state.barterMin->data.f;
            state.ownBarterMax = state.barterMax->data.f;
            SKSE::log::info("needs: barter factor {:.2f} to {:.2f}, dirt raises it", state.ownBarterMin, state.ownBarterMax);
        } else {
            state.barterMin = state.barterMax = nullptr;
            SKSE::log::warn("needs: fBarterMin or fBarterMax not found, dirt doesn't change prices");
        }
    }

    void Update(RE::PlayerCharacter* a_player, const std::array<float, 7>* a_needs, float a_delta)
    {
        if (!a_needs) {
            return;
        }
        const auto& needs = *a_needs;
        Starve(a_player, needs[Protocol::kNeedHunger], needs[Protocol::kNeedThirst], a_delta);
        Tired(a_player, needs[Protocol::kNeedFatigue], a_delta);
        SetPrice(Needs::DirtPrice(needs[Protocol::kNeedDirtiness]));
        Shake(needs[Protocol::kNeedDrunk], a_delta);
    }

    void Stop()
    {
        if (auto* player = RE::PlayerCharacter::GetSingleton()) {
            TiredOff(player);
        }
        SetPrice(1.0f);
        state.starving = false;
        state.shakeLogged = -1;
        Hud::ShakeHands(0.0f, 0.0f, 0.0f);
    }

    void Save(SKSE::SerializationInterface* a_intfc)
    {
        const float last = state.dry.LastDrunk();
        if (!a_intfc->OpenRecord(kRecord, kVersion) || !a_intfc->WriteRecordData(last)) {
            SKSE::log::warn("needs: could not save when Kumpo was last drunk");
        }
    }

    bool Read(SKSE::SerializationInterface* a_intfc, std::uint32_t a_type, std::uint32_t a_version)
    {
        if (a_type != kRecord) {
            return false;
        }
        float last = Needs::kNever;
        if (a_version >= 1 && a_intfc->ReadRecordData(last)) {
            state.dry.SetLastDrunk(last);
        }
        SKSE::log::info("needs: save loaded, Kumpo was last drunk at hour {:.1f}", last);
        return true;
    }

    // New game, or before a save loads: a save without the record wakes Kumpo up dry.
    void Revert()
    {
        state.dry.Reset();
    }
}
