#include "drink/Drink.h"

namespace PerkeleRim::Drink
{
    namespace
    {
        constexpr auto kPlugin = "PerkeleRim.esp"sv;
        // Fixed local form id, assigned by tools/esp-gen.
        constexpr RE::FormID kDrunkImod = 0x901;

        // MSC's drunk bar is full at level 4; so is the blur. Below this strength it's off.
        constexpr float kFullDrunk = 4.0f;
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
        const float strength = std::clamp(a_level / kFullDrunk, 0.0f, 1.0f);
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
