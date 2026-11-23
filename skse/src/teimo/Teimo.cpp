#include "teimo/Teimo.h"

#include "link/LinkService.h"
#include "model/ModelView.h"
#include "player/PlayerSync.h"

#include <atomic>
#include <cmath>
#include <cstring>
#include <random>

namespace PerkeleRim::Teimo
{
    namespace
    {
        constexpr auto          kPlugin = "PerkeleRim.esp"sv;
        constexpr RE::FormID    kTeimoRef = 0xB02;            // tools/esp-gen: Teimo standing at his stall
        constexpr auto          kModelName = "PerkeleRim.Teimo"sv;  // msc/src/Game/TeimoModel.cs
        constexpr float         kHeadHeight = 120.0f;         // units above his feet his voice comes from
        constexpr float         kHideEvery = 1.0f;            // seconds between walks over his Skyrim body (its head may load late)

        struct State
        {
            RE::FormID                id = 0;  // his reference, 0 without the ESP
            RE::NiPointer<RE::NiNode> node;
            std::uint32_t             model = 0;
            bool                      shown = false;
            RE::NiAVObject*           hiddenRoot = nullptr;  // the 3D his body was hidden in
            float                     hideIn = 0.0f;
            float                     heading = 0.0f;
            bool                      headingSet = false;
            float                     home = 0.0f;  // his stall's heading: his reference's angle z
            Talk                      talk;
            double                    clock = 0.0;  // seconds of game time MSC had the player, his talk's clock
            const Line*               line = nullptr;  // the line he says, while its caption runs
            std::string               lastClip;
            std::string               lastTopic;
            double                    lastAt = -1.0;
            std::uint32_t             lines = 0;
            std::mt19937              random{ std::random_device{}() };
        } state;

        std::atomic<RE::FormID> teimoId{ 0 };  // for OnTalk on other threads
        std::atomic<bool>       talkedTo{ false };

        RE::Actor* Find()
        {
            return state.id ? RE::TESForm::LookupByID<RE::Actor>(state.id) : nullptr;
        }

        float FlatDistance(const RE::NiPoint3& a, const RE::NiPoint3& b)
        {
            return std::hypot(a.x - b.x, a.y - b.y);
        }

        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }

        // Culls every shape of his Skyrim body (and its head, gear) so only MSC's Teimo shows; a_show
        // brings them back.
        void ShowBody(RE::NiAVObject* a_object, bool a_show)
        {
            if (!a_object) {
                return;
            }
            if (a_object->AsGeometry()) {
                a_object->SetAppCulled(!a_show);
                return;
            }
            if (auto* node = a_object->AsNode()) {
                for (auto& child : node->GetChildren()) {
                    ShowBody(child.get(), a_show);
                }
            }
        }

        bool LoadModel()
        {
            const auto model = ModelView::Find(kModelName);
            if (model == 0 || model == state.model) {
                return state.node != nullptr;
            }
            auto node = ModelView::Instance(model);
            if (!node) {
                return state.node != nullptr;
            }
            if (state.node && state.node->parent) {
                state.node->parent->DetachChild(state.node.get());
            }
            state.node = node;
            state.model = model;
            state.shown = false;
            SKSE::log::info("teimo: MSC's Teimo arrived (model {:08X})", model);
            return true;
        }

        void HideModel()
        {
            if (state.node && state.shown) {
                state.node->SetAppCulled(true);
                state.shown = false;
            }
        }

        void SetHeading(RE::NiMatrix3& a_m, float a_heading)
        {
            // Turns the model's +Y (its front) to the heading, clockwise from north (+Y).
            const float c = std::cos(a_heading), s = std::sin(a_heading);
            a_m.entry[0][0] = c;
            a_m.entry[0][1] = s;
            a_m.entry[0][2] = 0.0f;
            a_m.entry[1][0] = -s;
            a_m.entry[1][1] = c;
            a_m.entry[1][2] = 0.0f;
            a_m.entry[2][0] = 0.0f;
            a_m.entry[2][1] = 0.0f;
            a_m.entry[2][2] = 1.0f;
        }

        void Send(const Line& a_line, const RE::NiPoint3& a_head)
        {
            Protocol::TeimoLine message{};
            message.position[0] = a_head.x;
            message.position[1] = a_head.y;
            message.position[2] = a_head.z;
            message.length = static_cast<std::uint32_t>((std::min)(a_line.clip.size(), static_cast<std::size_t>(Protocol::kTeimoClipMax)));
            std::memcpy(message.clip, a_line.clip.data(), message.length);
            if (!LinkService::Get().Send(Protocol::MessageType::TeimoLine, &message, sizeof(message))) {
                SKSE::log::warn("teimo: {} didn't go to MSC (ring full or link down)", a_line.clip);
            }
        }

        void Say(RE::PlayerCharacter* a_player, RE::Actor* a_teimo, Topic a_topic, const char* a_why)
        {
            std::uniform_real_distribution<double> unit(0.0, 1.0);
            const auto*                            line = state.talk.Say(a_topic, state.clock, unit(state.random));
            if (!line) {
                return;
            }
            auto head = a_teimo->GetPosition();
            head.z += kHeadHeight;
            Send(*line, head);
            state.line = line;
            state.lastClip = std::string(line->clip);
            state.lastTopic = std::string(TopicName(a_topic));
            state.lastAt = state.clock;
            state.lines++;
            SKSE::log::info("teimo: says {} ({}, {}): \"{}\", Kumpo {:.1f} m away", line->clip, TopicName(a_topic), a_why,
                line->captions[0].text, FlatDistance(a_player->GetPosition(), a_teimo->GetPosition()) / 70.0f);
        }
    }

    void OnDataLoaded()
    {
        auto* data = RE::TESDataHandler::GetSingleton();
        auto* teimo = data && data->LookupLoadedModByName(kPlugin) ? data->LookupForm<RE::Actor>(kTeimoRef, kPlugin) : nullptr;
        state.id = teimo ? teimo->GetFormID() : 0;
        teimoId = state.id;
        if (!teimo) {
            SKSE::log::info("teimo: {} has no Teimo (not loaded, or older than his stall): no Teimo in Riverwood", kPlugin);
            return;
        }
        state.home = teimo->GetAngleZ();
        SKSE::log::info("teimo: at his stall in Riverwood ({:08X}) at ({:.0f},{:.0f},{:.0f}) facing {:.2f}", state.id, teimo->GetPositionX(),
            teimo->GetPositionY(), teimo->GetPositionZ(), state.home);
    }

    void OnGameLoaded()
    {
        state.talk.Reset();
        state.clock = 0.0;
        state.line = nullptr;
        state.lines = 0;
        state.lastAt = -1.0;
        state.hiddenRoot = nullptr;
        state.headingSet = false;
        talkedTo = false;
    }

    void Draw(RE::PlayerCharacter* a_player, float a_delta)
    {
        if (!PlayerSync::Owned()) {
            talkedTo = false;  // E with Skyrim's own player: no Kumpo to greet, and no greeting saved for the next handover
        }
        auto* teimo = Find();
        auto* root = teimo && !teimo->IsDisabled() && teimo->Is3DLoaded() ? teimo->Get3D(false) : nullptr;
        if (!root || !LoadModel()) {
            HideModel();
            return;
        }
        auto* scene = Scene();
        if (scene && state.node->parent != scene) {
            if (state.node->parent) {
                state.node->parent->DetachChild(state.node.get());
            }
            scene->AttachChild(state.node.get(), true);
        }
        // His body hidden once his model is up, and again now and then (a head or gear loading late).
        state.hideIn -= a_delta;
        if (root != state.hiddenRoot || state.hideIn <= 0.0f) {
            ShowBody(root, false);
            state.hiddenRoot = root;
            state.hideIn = kHideEvery;
        }

        const auto at = teimo->GetPosition();
        const auto kumpo = a_player->GetPosition();
        if (!state.headingSet) {
            state.heading = state.home;
            state.headingSet = true;
        }
        const float target = FlatDistance(at, kumpo) <= kTurnRange ? HeadingTo(kumpo.x - at.x, kumpo.y - at.y) : state.home;
        state.heading = TurnToward(state.heading, target, a_delta);

        auto& node = *state.node;
        SetHeading(node.local.rotate, state.heading);
        node.local.translate = at;
        node.local.scale = 1.0f;
        node.SetAppCulled(false);
        RE::NiUpdateData update{};
        node.Update(update);
        if (!state.shown) {
            state.shown = true;
            SKSE::log::info("teimo: drawn as MSC's Teimo at ({:.0f},{:.0f},{:.0f}), his Skyrim body hidden", at.x, at.y, at.z);
        }
    }

    void Update(RE::PlayerCharacter* a_player, float a_delta, float a_drunk)
    {
        state.clock += a_delta;
        auto* teimo = Find();
        if (!teimo || !teimo->Is3DLoaded() || teimo->IsDisabled() || teimo->IsDead() || !LinkService::Get().IsUp()) {
            talkedTo = false;
            return;
        }
        const float distance = FlatDistance(teimo->GetPosition(), a_player->GetPosition());
        if (talkedTo.exchange(false) && distance <= kAround) {
            if (const auto topic = state.talk.OnTalk(state.clock, a_drunk)) {
                Say(a_player, teimo, *topic, "Kumpo talks to him");
                return;
            }
        }
        if (const auto topic = state.talk.Tick(state.clock, distance, a_drunk)) {
            Say(a_player, teimo, *topic, *topic == Topic::kChat ? "Kumpo hangs about" : "Kumpo walks up");
        }
    }

    bool IsTeimo(const RE::TESObjectREFR* a_ref)
    {
        return a_ref && state.id && a_ref->GetFormID() == state.id;
    }

    void OnHit(RE::PlayerCharacter* a_player, RE::Actor* a_target, bool a_piss)
    {
        if (!IsTeimo(a_target) || a_target->IsDead()) {
            return;
        }
        const auto topic = a_piss ? Topic::kPissed : Topic::kHit;
        if (const auto say = state.talk.OnInsult(topic, state.clock, FlatDistance(a_target->GetPosition(), a_player->GetPosition()))) {
            Say(a_player, a_target, *say, a_piss ? "pissed on" : "hit");
        }
    }

    void OnFinger(RE::PlayerCharacter* a_player, float a_dirX, float a_dirY)
    {
        auto* teimo = Find();
        if (!teimo || !teimo->Is3DLoaded() || teimo->IsDead()) {
            return;
        }
        const auto at = teimo->GetPosition();
        const auto kumpo = a_player->GetPosition();
        if (!SeesFinger(at.x - kumpo.x, at.y - kumpo.y, a_dirX, a_dirY)) {
            return;
        }
        if (const auto say = state.talk.OnInsult(Topic::kFinger, state.clock, FlatDistance(at, kumpo))) {
            Say(a_player, teimo, *say, "the finger");
        }
    }

    bool OnTalk(const RE::Actor* a_actor)
    {
        const auto id = teimoId.load();
        if (!a_actor || id == 0 || a_actor->GetFormID() != id) {
            return false;
        }
        talkedTo = true;
        return true;
    }

    std::string CurrentCaption()
    {
        if (!state.line) {
            return {};
        }
        const auto text = CaptionAt(*state.line, static_cast<float>(state.clock - state.talk.StartedAt()));
        if (text.empty()) {
            state.line = nullptr;
            return {};
        }
        return "Teimo: " + std::string(text);
    }

    Status Describe()
    {
        Status s;
        auto*  teimo = Find();
        s.found = state.id != 0;
        s.loaded = teimo && teimo->Is3DLoaded();
        s.drawn = state.shown;
        s.model = state.model;
        if (teimo) {
            s.found = true;
            s.position = teimo->GetPosition();
            if (auto* player = RE::PlayerCharacter::GetSingleton()) {
                s.distance = FlatDistance(s.position, player->GetPosition());
            }
        }
        s.heading = state.heading;
        s.home = state.home;
        s.visiting = state.talk.Visiting();
        s.lastClip = state.lastClip;
        s.lastTopic = state.lastTopic;
        s.lastAt = state.lastAt;
        s.lines = state.lines;
        s.caption = CurrentCaption();
        return s;
    }

    RE::NiNode* Node()
    {
        return state.shown ? state.node.get() : nullptr;
    }

    RE::Actor* Actor()
    {
        return Find();
    }

    bool DebugSay(RE::PlayerCharacter* a_player, Topic a_topic)
    {
        auto* teimo = Find();
        if (!teimo || !teimo->Is3DLoaded() || !LinkService::Get().IsUp()) {
            return false;
        }
        Say(a_player, teimo, a_topic, "test kit");
        return true;
    }

    std::string_view TopicName(Topic a_topic)
    {
        switch (a_topic) {
        case Topic::kGreet:
            return "greet";
        case Topic::kDrunk:
            return "drunk";
        case Topic::kChat:
            return "chat";
        case Topic::kFinger:
            return "finger";
        case Topic::kPissed:
            return "pissed";
        case Topic::kHit:
            return "hit";
        }
        return "?";
    }

    std::optional<Topic> TopicByName(std::string_view a_name)
    {
        for (std::size_t i = 0; i < kTopicCount; i++) {
            if (TopicName(static_cast<Topic>(i)) == a_name) {
                return static_cast<Topic>(i);
            }
        }
        return std::nullopt;
    }
}
