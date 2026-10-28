#include "death/Death.h"

#include "damage.h"
#include "hurt.h"
#include "link/LinkService.h"
#include "model/ModelView.h"
#include "safe/Safe.h"

namespace PerkeleRim::Death
{
    namespace
    {
        // MSC's blood overlay, as ModelData.Overlay builds it (msc/src/Link/Model.cs): a quad 2 units
        // wide and kOverlayHalfHeight * 2 tall (player_blood_vision is 1024x512), facing a viewer
        // looking along its +Y.
        constexpr const char* kOverlayModel = "PlayerBlood";
        constexpr float       kOverlayHalfHeight = 0.5f;
        constexpr float       kOverlayDistance = 20.0f;  // units in front of the camera
        // Like MSC's PlayerBlood Expand and Rot: the blood grows over the view and turns slowly.
        constexpr float kFadeIn = 0.4f;     // seconds
        constexpr float kGrowTime = 4.0f;   // seconds to grow from 1.05 to kGrowTo times the view
        constexpr float kGrowTo = 1.35f;
        constexpr float kTurn = 0.12f;      // radians over kGrowTime
        constexpr float kShowFor = 20.0f;   // seconds; Skyrim has reloaded long before

        struct State
        {
            Damage::FallTracker       fall;
            Hurt::LowHealth           low;
            bool                      lethal = false;
            float                     lethalDamage = 0.0f;
            RE::NiPointer<RE::NiNode> overlay;  // kept and reused: nodes built here are never destroyed
            std::uint32_t             overlayModel = 0;
            bool                      showing = false;
            float                     shownFor = 0.0f;
            float                     nextLog = 0.0f;
            std::chrono::steady_clock::time_point lastTick;
        } state;

        // The player's own Update may stop once it is dead, so while the blood shows a small thread
        // also queues a pose update as an SKSE task, about 30 times a second, one at a time. (A task
        // that queued itself again ran in the same pass forever and froze the game.)
        std::atomic<bool> pulsing{ false };
        std::atomic<bool> taskQueued{ false };

        void TickOverlay();

        void StartPulse()
        {
            if (pulsing.exchange(true)) {
                return;
            }
            std::thread([] {
                while (pulsing) {
                    if (!taskQueued.exchange(true)) {
                        SKSE::GetTaskInterface()->AddTask([]() {
                            taskQueued = false;
                            TickOverlay();
                        });
                    }
                    std::this_thread::sleep_for(33ms);
                }
            }).detach();
        }

        float Setting(const char* a_name, float a_default)
        {
            auto* settings = RE::GameSettingCollection::GetSingleton();
            auto* setting = settings ? settings->GetSetting(a_name) : nullptr;
            return setting ? setting->GetFloat() : a_default;
        }

        // Tells MSC, which grunts or swears for it (PlayerHurt).
        void Send(RE::PlayerCharacter* a_player, std::uint32_t a_cause, float a_damage)
        {
            auto*       values = a_player->AsActorValueOwner();
            const float max = values->GetPermanentActorValue(RE::ActorValue::kHealth);
            Protocol::PlayerHurt hurt{};
            hurt.cause = a_cause;
            hurt.damage = max > 0.0f ? a_damage / max : 0.0f;
            hurt.health = max > 0.0f ? values->GetActorValue(RE::ActorValue::kHealth) / max : 0.0f;
            if (!LinkService::Get().Send(Protocol::MessageType::PlayerHurt, &hurt, sizeof(hurt))) {
                SKSE::log::warn("death: the ring to MSC is full, no hurt sound (cause {})", a_cause);
            }
        }

        // Takes a_amount off Skyrim's health; a hit that would kill is left to PlayerSync (Lethal),
        // which first takes the player back from MSC so Skyrim's death can ragdoll it.
        void Hurt(RE::PlayerCharacter* a_player, float a_amount, std::uint32_t a_cause, const char* a_what)
        {
            if (a_amount <= 0.0f) {
                return;
            }
            auto*       values = a_player->AsActorValueOwner();
            const float health = values->GetActorValue(RE::ActorValue::kHealth);
            SKSE::log::info("death: {} costs {:.0f} health of {:.0f} (max {:.0f})", a_what, a_amount, health,
                values->GetPermanentActorValue(RE::ActorValue::kHealth));
            if (a_amount >= health) {
                state.lethal = true;
                state.lethalDamage = a_amount;
                return;
            }
            {
                const Safe::OwnDamage own;  // a crash hurts even in a safe seat
                values->DamageActorValue(RE::ActorValue::kHealth, a_amount);
            }
            Send(a_player, a_cause, a_amount);
        }

        const char* ConsumedName(std::uint32_t a_kind)
        {
            switch (a_kind) {
            case Protocol::kConsumedBeer:
                return "beer";
            case Protocol::kConsumedShot:
                return "shot";
            case Protocol::kConsumedKilju:
                return "kilju";
            case Protocol::kConsumedBooze:
                return "booze";
            case Protocol::kConsumedSpirit:
                return "spirit";
            case Protocol::kConsumedCigarette:
                return "cigarette";
            case Protocol::kConsumedFood:
                return "food";
            default:
                return "unknown consume";
            }
        }

        void SetAlpha(RE::NiAVObject* a_node, float a_alpha)
        {
            RE::BSVisit::TraverseScenegraphGeometries(a_node, [a_alpha](RE::BSGeometry* a_geometry) {
                auto* shader = netimmerse_cast<RE::BSLightingShaderProperty*>(a_geometry->GetGeometryRuntimeData().shaderProperty.get());
                if (shader && shader->material) {
                    static_cast<RE::BSLightingShaderMaterialBase*>(shader->material)->materialAlpha = a_alpha;
                }
                return RE::BSVisit::BSVisitControl::kContinue;
            });
        }

        // In front of the camera, covering the view. NiCamera looks along its local +X with +Y up
        // and +Z right; its frustum gives the view's half width and height at distance 1.
        void PoseOverlay(float a_time)
        {
            auto* camera = RE::Main::WorldRootCamera();
            if (!camera || !state.overlay) {
                return;
            }
            const auto& frustum = camera->GetRuntimeData2().viewFrustum;
            const auto& r = camera->world.rotate;
            const RE::NiPoint3 forward(r.entry[0][0], r.entry[1][0], r.entry[2][0]);
            const RE::NiPoint3 up(r.entry[0][1], r.entry[1][1], r.entry[2][1]);
            const RE::NiPoint3 right(r.entry[0][2], r.entry[1][2], r.entry[2][2]);

            const float grow = std::clamp(a_time / kGrowTime, 0.0f, 1.0f);
            const float ease = 1.0f - (1.0f - grow) * (1.0f - grow);
            const float angle = kTurn * ease;
            const RE::NiPoint3 x = right * std::cos(angle) - up * std::sin(angle);
            const RE::NiPoint3 z = up * std::cos(angle) + right * std::sin(angle);

            const float distance = (std::max)(kOverlayDistance, frustum.fNear * 2.0f);
            // Cover the view's width and height, plus the corners the turn swings in.
            const float cover = (std::max)(std::abs(frustum.fRight), std::abs(frustum.fTop) / kOverlayHalfHeight) * distance * 1.15f;

            auto& node = *state.overlay;
            for (int i = 0; i < 3; i++) {
                node.local.rotate.entry[i][0] = x[i];
                node.local.rotate.entry[i][1] = forward[i];
                node.local.rotate.entry[i][2] = z[i];
            }
            node.local.translate = camera->world.translate + forward * distance;
            node.local.scale = cover * (1.05f + (kGrowTo - 1.05f) * ease);
            RE::NiUpdateData update{};
            node.Update(update);
            SetAlpha(&node, std::clamp(a_time / kFadeIn, 0.0f, 1.0f));
            if (a_time >= state.nextLog) {
                state.nextLog = a_time + 1.0f;
                SKSE::log::info(
                    "death: blood at {:.1f} s: camera ({:.0f},{:.0f},{:.0f}) looking ({:.2f},{:.2f},{:.2f}), frustum right {:.3f} top {:.3f} near {:.1f}, scale {:.1f}, parent {}, culled {}",
                    a_time, camera->world.translate.x, camera->world.translate.y, camera->world.translate.z, forward.x, forward.y, forward.z,
                    frustum.fRight, frustum.fTop, frustum.fNear, node.local.scale, node.parent ? node.parent->name.c_str() : "none", node.GetAppCulled());
            }
        }

        void TickOverlay()
        {
            if (!state.showing) {
                return;
            }
            const auto now = std::chrono::steady_clock::now();
            state.shownFor += std::chrono::duration<float>(now - state.lastTick).count();
            state.lastTick = now;
            if (state.shownFor > kShowFor) {
                Hide();
                return;
            }
            PoseOverlay(state.shownFor);
        }

        // Where the blood hangs: the node the player's own 3D is in, like ItemView's items (hung
        // straight under the shadow scene node it was posed right but never drawn).
        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }
    }

    void OnDamage(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        Protocol::PlayerDamage damage{};
        if (a_size < sizeof(damage)) {
            SKSE::log::warn("death: short PlayerDamage ({} bytes)", a_size);
            return;
        }
        std::memcpy(&damage, a_data, sizeof(damage));
        const float max = a_player->AsActorValueOwner()->GetPermanentActorValue(RE::ActorValue::kHealth);
        Hurt(a_player, Damage::Crash(max, damage.severity), Protocol::kHurtCrash,
            damage.cause == Protocol::kDamageCrash ? "an MSC crash" : "an MSC death (not a crash)");
    }

    void Starve(RE::PlayerCharacter* a_player, float a_amount)
    {
        if (a_amount <= 0.0f || state.lethal) {
            return;
        }
        auto*       values = a_player->AsActorValueOwner();
        const float health = values->GetActorValue(RE::ActorValue::kHealth);
        if (a_amount >= health) {
            SKSE::log::info("death: Kumpo starved (hunger or thirst), {:.1f} health left", health);
            state.lethal = true;
            state.lethalDamage = a_amount;
            return;
        }
        const Safe::OwnDamage own;  // no grunt or blood a frame: it's slow
        values->DamageActorValue(RE::ActorValue::kHealth, a_amount);
    }

    void OnConsumed(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size)
    {
        Protocol::Consumed consumed{};
        if (a_size < sizeof(consumed)) {
            SKSE::log::warn("death: short Consumed ({} bytes)", a_size);
            return;
        }
        std::memcpy(&consumed, a_data, sizeof(consumed));
        auto*       values = a_player->AsActorValueOwner();
        const float max = values->GetPermanentActorValue(RE::ActorValue::kHealth);
        const float health = values->GetActorValue(RE::ActorValue::kHealth);
        const float heal = Damage::Heal(consumed.kind, max, health);
        SKSE::log::info("death: MSC's {} heals {:.0f} health of {:.0f} (max {:.0f})", ConsumedName(consumed.kind), heal, health, max);
        if (heal > 0.0f) {
            values->RestoreActorValue(RE::ActorValue::kHealth, heal);
        }
    }

    void OnHit(RE::PlayerCharacter* a_player, bool a_blocked)
    {
        Send(a_player, a_blocked ? Protocol::kHurtBlocked : Protocol::kHurtHit, 0.0f);
    }

    void OnHandover() { state.low.Reset(); }

    void OnPose(RE::PlayerCharacter* a_player, const Protocol::PlayerState* a_pose, bool a_seated)
    {
        auto*       values = a_player->AsActorValueOwner();
        const float max = values->GetPermanentActorValue(RE::ActorValue::kHealth);
        if (max > 0.0f && state.low.Step(values->GetActorValue(RE::ActorValue::kHealth) / max)) {
            SKSE::log::info("death: the player's health dropped low");
            Send(a_player, Protocol::kHurtLow, 0.0f);
        }
        if (!a_pose || a_seated) {
            state.fall.Reset();  // a crash in a vehicle comes as PlayerDamage instead
            return;
        }
        const float fallen = state.fall.Update(a_pose->position[2], (a_pose->flags & Protocol::kPlayerGrounded) != 0);
        if (fallen <= 0.0f) {
            return;
        }
        const Damage::FallSettings settings{ Setting("fJumpFallHeightMin", 600.0f), Setting("fJumpFallHeightMult", 0.1f),
            Setting("fJumpFallHeightExponent", 1.45f) };
        const float damage = Damage::Fall(fallen, settings);
        if (damage > 0.0f) {
            Hurt(a_player, damage, Protocol::kHurtFall, std::format("a {:.0f} unit fall", fallen).c_str());
        }
    }

    bool Lethal() { return state.lethal; }

    void Kill(RE::PlayerCharacter* a_player)
    {
        const float damage = state.lethalDamage;
        state.lethal = false;
        state.lethalDamage = 0.0f;
        state.fall.Reset();
        if (!a_player->IsDead()) {
            SKSE::log::info("death: the hit kills the player");
            const Safe::OwnDamage own;
            a_player->KillImpl(nullptr, damage, true, false);
        }
    }

    void Show()
    {
        const auto model = ModelView::Find(kOverlayModel);
        if (!model) {
            SKSE::log::warn("death: MSC's blood overlay hasn't arrived, no blood");
            return;
        }
        if (!state.overlay || state.overlayModel != model) {
            state.overlay = ModelView::Instance(model);
            state.overlayModel = model;
        }
        if (!state.overlay) {
            return;
        }
        auto* scene = Scene();
        if (scene && state.overlay->parent != scene) {
            if (state.overlay->parent) {
                state.overlay->parent->DetachChild(state.overlay.get());
            }
            scene->AttachChild(state.overlay.get(), true);
        }
        state.overlay->SetAppCulled(false);
        state.showing = true;
        state.shownFor = 0.0f;
        state.nextLog = 0.0f;
        state.lastTick = std::chrono::steady_clock::now();
        PoseOverlay(0.0f);
        StartPulse();
        SKSE::log::info("death: MSC's blood over the view");
    }

    void Hide()
    {
        state.lethal = false;
        state.fall.Reset();
        if (!state.showing) {
            return;
        }
        state.showing = false;
        pulsing = false;
        if (state.overlay) {
            state.overlay->SetAppCulled(true);
            if (state.overlay->parent) {
                state.overlay->parent->DetachChild(state.overlay.get());
            }
        }
        SKSE::log::info("death: blood overlay off");
    }

    void Tick()
    {
        TickOverlay();
    }
}
