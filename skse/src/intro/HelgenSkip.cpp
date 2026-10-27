#include "intro/HelgenSkip.h"

#include "Ini.h"
#include "garage/Garage.h"
#include "helgen_skip.h"
#include "intro/Intro.h"
#include "intro_cues.h"

namespace PerkeleRim::HelgenSkip
{
    namespace
    {
        constexpr RE::FormID kPrisonerCuffsPlayer = 0x10E039;  // Skyrim.esm, equipped in the cart
        constexpr RE::FormID kSittingAngleLimit = 0x07F8F0;    // Skyrim.esm global, 2.618 in the cart, 0 once out of it
        // Skyrim.esm's carriage travel black: FadeOutGame's fader lets go before the move's load
        // screen, so the hold keeps it black through the move and the back fades it up (2 s).
        constexpr RE::FormID kFadeToBlackHoldImod = 0x0F756E;
        constexpr RE::FormID kFadeToBlackBackImod = 0x0F756F;
        constexpr float      kArrivedUnits = 256.0f;           // this close to the spawn marker is there
        constexpr auto       kName = "Kumpo";

        struct State
        {
            RE::TESQuest*              unbound = nullptr;
            RE::TESQuest*              beforeTheStorm = nullptr;
            RE::TESQuest*              dragonAttack = nullptr;
            RE::TESForm*               cuffs = nullptr;
            RE::TESGlobal*             sittingAngle = nullptr;
            RE::TESImageSpaceModifier* hold = nullptr;
            RE::TESImageSpaceModifier* back = nullptr;
            Machine                    machine;
        } state;

        RE::BSScript::Internal::VirtualMachine* Vm()
        {
            auto* vm = RE::BSScript::Internal::VirtualMachine::GetSingleton();
            return vm && vm->GetObjectHandlePolicy() ? vm : nullptr;
        }

        // A global Papyrus function (Game.EnablePlayerControls and the like), the same call MQ101's
        // fragments make. Runs on the VM's next update.
        template <class... Args>
        void Static(const char* a_class, const char* a_function, Args&&... a_args)
        {
            auto* vm = Vm();
            RE::BSTSmartPointer<RE::BSScript::IStackCallbackFunctor> callback;
            if (!vm || !vm->DispatchStaticCall(a_class, a_function, RE::MakeFunctionArguments(std::forward<Args>(a_args)...), callback)) {
                SKSE::log::warn("helgen skip: {}.{} didn't go to Papyrus", a_class, a_function);
            }
        }

        // A Papyrus method on a form (Quest.SetStage, Actor.SetVehicle, ...).
        template <class... Args>
        void Method(RE::TESForm* a_form, const char* a_class, const char* a_function, Args&&... a_args)
        {
            auto* vm = Vm();
            auto* policy = vm ? vm->GetObjectHandlePolicy() : nullptr;
            const auto handle = policy && a_form ? policy->GetHandleForObject(a_form->GetFormType(), a_form) : RE::VMHandle{};
            RE::BSTSmartPointer<RE::BSScript::IStackCallbackFunctor> callback;
            if (!policy || handle == policy->EmptyHandle() ||
                !vm->DispatchMethodCall(handle, a_class, a_function, RE::MakeFunctionArguments(std::forward<Args>(a_args)...), callback)) {
                SKSE::log::warn("helgen skip: {}.{} on {:08X} didn't go to Papyrus", a_class, a_function, a_form ? a_form->GetFormID() : 0);
            }
        }

        // A property of a script on a_form, as MQ101's own fragments see it; null if it isn't bound.
        RE::BSScript::Variable* Property(RE::TESForm* a_form, const char* a_script, const char* a_property)
        {
            auto* vm = Vm();
            auto* policy = vm ? vm->GetObjectHandlePolicy() : nullptr;
            RE::BSTSmartPointer<RE::BSScript::Object> object;
            if (!policy || !a_form || !vm->FindBoundObject(policy->GetHandleForObject(a_form->GetFormType(), a_form), a_script, object) || !object) {
                return nullptr;
            }
            return object->GetProperty(a_property);
        }

        std::string Stage(const RE::TESQuest* a_quest)
        {
            if (!a_quest) {
                return "missing";
            }
            return fmt::format("stage {}{}", a_quest->GetCurrentStageID(), a_quest->IsCompleted() ? " completed" : a_quest->IsEnabled() ? "" : " not running");
        }

        // The cart's hold on the player, undone the way MQ101's own stages undo it in the keep
        // (EnablePlayerControls, SetInChargen, the cart HUD, the cuffs at stage 240, the weather
        // override at 250, the cart sound at 43, the intro music at 20).
        void OffTheCart(RE::PlayerCharacter* a_player)
        {
            Method(a_player, "Actor", "SetVehicle", static_cast<RE::TESObjectREFR*>(nullptr));
            Method(a_player, "Actor", "SetRestrained", false);
            Static("Game", "SetPlayerAIDriven", false);
            a_player->NotifyAnimationGraph("IdleForceDefaultState");  // out of the cart passenger idle
            Static("Game", "SetInChargen", false, false, false);
            Static("Game", "EnablePlayerControls", true, true, true, true, true, true, true, true, static_cast<std::int32_t>(0));
            Static("Game", "SetHudCartMode", false);
            Static("Game", "ShowFirstPersonGeometry", true);
            Static("Game", "HideTitleSequenceMenu");
            Static("Weather", "ReleaseOverride");
            if (state.sittingAngle) {
                Method(state.sittingAngle, "GlobalVariable", "SetValue", 0.0f);
            }
            if (state.cuffs) {
                Method(a_player, "ObjectReference", "RemoveItem", static_cast<RE::TESForm*>(state.cuffs), static_cast<std::int32_t>(99), true, static_cast<RE::TESObjectREFR*>(nullptr));
            }
            if (auto* sound = Property(state.unbound, "MQ101QuestScript", "CartSoundInstanceID"); sound && sound->IsInt()) {
                Static("Sound", "StopInstance", sound->GetSInt());
            }
            for (const auto* music : { "IntroMusic", "MUSDungeonChargen" }) {
                if (auto* property = Property(state.unbound, "QF_MQ101_0003372B", music); property && property->IsObject()) {
                    if (auto* type = property->Unpack<RE::BGSMusicType*>()) {
                        Method(type, "MusicType", "Remove");
                    }
                }
            }
        }

        // The race menu never opens: the default player is a Nord already, and only gets a name.
        void MakeKumpo(RE::PlayerCharacter* a_player)
        {
            auto* base = a_player->GetActorBase();
            if (!base) {
                return;
            }
            base->SetFullName(kName);
            base->AddChange(RE::TESNPC::ChangeFlags::kFullName);  // kept in the save
            const auto* race = a_player->GetRace();
            SKSE::log::info("helgen skip: the player is {}, a {}", base->GetFullName(), race ? race->GetFullName() : "race nobody knows");
        }

        // Unbound and Before the Storm as if he had escaped through the keep: vanilla's own skip
        // stage of MQ102 sets up the friend's quest, points the journal at Riverwood and sets MQ101
        // to 900 (completed, then 1000 stops it); MQ101DragonAttack's last stage leaves Helgen in ruins.
        void Quests()
        {
            const auto side = ParseSide(Ini::Value("HelgenSide"));
            if (state.dragonAttack) {
                Method(state.dragonAttack, "Quest", "SetStage", static_cast<std::int32_t>(kRuinsStage));
            } else {
                SKSE::log::warn("helgen skip: no MQ101DragonAttack ({:08X}), Helgen isn't left in ruins", kDragonAttack);
            }
            if (state.beforeTheStorm) {
                Method(state.beforeTheStorm, "Quest", "SetStage", static_cast<std::int32_t>(BeforeTheStormStage(side)));
            } else {
                SKSE::log::warn("helgen skip: no Before the Storm ({:08X}), Unbound stays unfinished and the main quest doesn't go on", kBeforeTheStorm);
            }
            SKSE::log::info("helgen skip: Unbound done, Before the Storm with {} (stage {}), Helgen in ruins", Friend(side), BeforeTheStormStage(side));
        }

        void Move(RE::PlayerCharacter* a_player)
        {
            auto* spawn = Garage::Spawn();
            if (!spawn) {
                SKSE::log::error("helgen skip: PerkeleRim.esp has no garage spawn marker (not loaded, or built before it), Kumpo wakes up off the cart where he is");
                return;
            }
            a_player->MoveTo(spawn);
            a_player->SetHeading(spawn->GetAngleZ());
            SKSE::log::info("helgen skip: moving Kumpo to the garage");
        }

        // After the move from the cart's forced clear weather the garage sat in fog for about half a
        // minute; forcing the sky's current weather again while it's black clears it at once.
        void SettleWeather()
        {
            auto* sky = RE::Sky::GetSingleton();
            if (!sky || !sky->currentWeather) {
                return;
            }
            const auto* from = sky->lastWeather;
            const float pct = sky->currentWeatherPct;
            sky->ForceWeather(sky->currentWeather, false);
            SKSE::log::info("helgen skip: weather {:08X} settled (was {:.0f}% over from {:08X})", sky->currentWeather->GetFormID(), pct * 100.0f,
                from ? from->GetFormID() : 0);
        }

        // The move starts no fall: the controller forgets where a fall began (the cart's hill, or
        // in the air while the garage's cell loads), so landing there costs no health.
        void NoFall(RE::PlayerCharacter* a_player)
        {
            if (auto* controller = a_player->GetCharController()) {
                controller->fallStartHeight = a_player->GetPositionZ();
                controller->fallTime = 0.0f;
            }
        }

        // Health Skyrim took off him (its damage modifier, negative), 0 when he's at full health.
        float Lost(RE::PlayerCharacter* a_player)
        {
            return -a_player->GetActorValueModifier(RE::ACTOR_VALUE_MODIFIER::kDamage, RE::ActorValue::kHealth);
        }

        // Kumpo wakes up at the garage at full health: whatever the cart or the move took goes back.
        void Heal(RE::PlayerCharacter* a_player, const char* a_when)
        {
            const float lost = Lost(a_player);
            if (lost < 0.5f) {
                return;
            }
            auto* values = a_player->AsActorValueOwner();
            const float health = values->GetActorValue(RE::ActorValue::kHealth);
            values->RestoreActorValue(RE::ActorValue::kHealth, lost);
            SKSE::log::info("helgen skip: Kumpo had {:.0f} of {:.0f} health {}, {:.0f} back", health,
                values->GetPermanentActorValue(RE::ActorValue::kHealth), a_when, lost);
        }

        bool Arrived(RE::PlayerCharacter* a_player)
        {
            auto* spawn = Garage::Spawn();
            auto* cell = a_player->GetParentCell();
            return !spawn || (cell && cell->IsAttached() && a_player->Is3DLoaded() &&
                                 a_player->GetPosition().GetDistance(spawn->GetPosition()) < kArrivedUnits);
        }
    }

    void OnDataLoaded()
    {
        state.unbound = RE::TESForm::LookupByID<RE::TESQuest>(Intro::kUnbound);
        state.beforeTheStorm = RE::TESForm::LookupByID<RE::TESQuest>(kBeforeTheStorm);
        state.dragonAttack = RE::TESForm::LookupByID<RE::TESQuest>(kDragonAttack);
        state.cuffs = RE::TESForm::LookupByID(kPrisonerCuffsPlayer);
        state.sittingAngle = RE::TESForm::LookupByID<RE::TESGlobal>(kSittingAngleLimit);
        state.hold = RE::TESForm::LookupByID<RE::TESImageSpaceModifier>(kFadeToBlackHoldImod);
        state.back = RE::TESForm::LookupByID<RE::TESImageSpaceModifier>(kFadeToBlackBackImod);
        SKSE::log::info("helgen skip: Before the Storm {}, dragon attack {}, side {}, hold black {}", state.beforeTheStorm ? "found" : "missing",
            state.dragonAttack ? "found" : "missing", Friend(ParseSide(Ini::Value("HelgenSide"))), state.hold && state.back ? "found" : "missing");
    }

    void OnNewGame()
    {
        state.machine.NewGame();
    }

    void OnGameLoaded()
    {
        state.machine.Loaded();
    }

    void Woke()
    {
        if (state.machine.Current() == Phase::Cart) {
            SKSE::log::info("helgen skip: Kumpo woke up in the cart, fading out in {:.0f} s", kPerkeleSeconds + kAfterPerkele);
        }
        state.machine.Woke();
    }

    bool Skipping()
    {
        return state.machine.Skipping();
    }

    std::string Describe()
    {
        return fmt::format("Before the Storm {}, dragon attack {}", Stage(state.beforeTheStorm), Stage(state.dragonAttack));
    }

    void Tick(float a_delta)
    {
        if (state.machine.Current() == Phase::Off) {
            return;
        }
        auto* player = RE::PlayerCharacter::GetSingleton();
        auto* ui = RE::UI::GetSingleton();
        if (!player) {
            return;
        }
        if (state.machine.Current() == Phase::Moving || state.machine.Healing()) {
            NoFall(player);
        }
        if (state.machine.Healing()) {
            Heal(player, "after waking up at the garage");  // the first handover's move, MSC putting him down
        }
        Frame frame;
        frame.intro = Intro::Running();
        frame.unboundStage = state.unbound ? state.unbound->GetCurrentStageID() : 0;
        frame.loading = ui && ui->IsMenuOpen(RE::LoadingMenu::MENU_NAME);
        frame.arrived = state.machine.Current() == Phase::Moving && Arrived(player);
        frame.delta = a_delta;
        switch (state.machine.Tick(frame)) {
        case Step::FadeOut:
            SKSE::log::info("helgen skip: fading out (Unbound stage {})", frame.unboundStage);
            Static("Game", "FadeOutGame", true, true, 0.0f, float{ kFadeOutSeconds });
            break;
        case Step::Release:
            if (state.hold) {
                RE::ImageSpaceModifierInstanceForm::Trigger(state.hold, 1.0f, nullptr);
            }
            Heal(player, "off the cart");
            OffTheCart(player);
            MakeKumpo(player);
            Garage::SkippedHelgen();
            Quests();
            break;
        case Step::Move:
            Move(player);
            NoFall(player);
            break;
        case Step::FadeIn:
            SKSE::log::info("helgen skip: {}, fading in ({}, {})", state.machine.GaveUp() ? "Kumpo never got to the garage" : "Kumpo is at the garage",
                Intro::Describe(), Describe());
            SettleWeather();
            Heal(player, "at the garage");
            Static("Game", "FadeOutGame", false, true, 0.0f, float{ kFadeInSeconds });
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
