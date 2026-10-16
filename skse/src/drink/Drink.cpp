#include "drink/Drink.h"

namespace PerkeleRim::Drink
{
    namespace
    {
        constexpr auto kPlugin = "PerkeleRim.esp"sv;
        // Fixed local form id, assigned by tools/esp-gen.
        constexpr RE::FormID kDrunkImod = 0x901;

        // MSC's drunk level climbs slowly (five beers are about 0.4, two dozen about 2.5) and MSC only
        // wobbles its camera, which Skyrim's already follows. So the blur stays off until MSC slurs
        // the player's speech (1.5) and only reaches kBlurMax at level 4, near blacking out. Below
        // kMinStrength it's off.
        constexpr float kBlurStart = 1.5f;
        constexpr float kFullDrunk = 4.0f;
        constexpr float kBlurMax = 0.3f;
        constexpr float kMinStrength = 0.01f;

        struct State
        {
            RE::TESImageSpaceModifier*                     imod = nullptr;
            RE::NiPointer<RE::ImageSpaceModifierInstanceForm> blur;
            bool                                           blurOn = false;
            float                                          blurAge = 0.0f;  // seconds since the loop was last triggered
        } state;
    }

    void OnDataLoaded()
    {
        auto* data = RE::TESDataHandler::GetSingleton();
        state.imod = data ? data->LookupForm<RE::TESImageSpaceModifier>(kDrunkImod, kPlugin) : nullptr;
        if (!state.imod) {
            SKSE::log::warn("drink: {} has no drunk imagespace, rebuild it with scripts/build-esp.ps1", kPlugin);
        }
    }

    void SetDrunk(float a_level, float a_delta)
    {
        if (!state.imod) {
            return;
        }
        const float strength = std::clamp((a_level - kBlurStart) / (kFullDrunk - kBlurStart), 0.0f, 1.0f) * kBlurMax;
        if (strength < kMinStrength) {
            if (state.blurOn) {
                RE::ImageSpaceModifierInstanceForm::Stop(state.imod);
                state.blur.reset();
                state.blurOn = false;
                SKSE::log::info("drunk blur: off");
            }
            return;
        }
        state.blurAge += a_delta;
        if (!state.blur || state.blurAge >= state.imod->data.duration) {
            if (!state.blurOn) {
                state.blurOn = true;
                SKSE::log::info("drunk blur: on at drunk level {:.2f}", a_level);
            }
            // Triggered again at the end of every loop: an imagespace modifier ends after its duration.
            state.blur.reset(RE::ImageSpaceModifierInstanceForm::Trigger(state.imod, strength, nullptr));
            state.blurAge = 0.0f;
        } else if (state.blur) {
            state.blur->strength = strength;
        }
    }
}
