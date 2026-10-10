#include "item/ItemView.h"

#include <unordered_set>

#include "coords.h"
#include "model/ModelView.h"

namespace PerkeleRim::ItemView
{
    namespace
    {
        struct Item
        {
            RE::NiPointer<RE::NiNode> node;  // null while its model hasn't arrived
            std::uint32_t             model = 0;
        };

        struct State
        {
            std::unordered_map<std::uint32_t, Item>                                     items;  // by MSC's item id
            std::unordered_map<std::uint32_t, std::vector<RE::NiPointer<RE::NiNode>>>  spare;  // by model id
            std::size_t                                                                 shown = 0;  // last logged
        } state;

        // The node the player's own 3D hangs in, like VehicleView, so items are lit and culled like the world.
        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }

        void Put(Item& a_item)
        {
            if (!a_item.node) {
                return;
            }
            if (a_item.node->parent) {
                a_item.node->parent->DetachChild(a_item.node.get());
            }
            state.spare[a_item.model].push_back(std::move(a_item.node));
            a_item.node.reset();
        }

        RE::NiPointer<RE::NiNode> Take(std::uint32_t a_model)
        {
            if (auto found = state.spare.find(a_model); found != state.spare.end() && !found->second.empty()) {
                auto node = std::move(found->second.back());
                found->second.pop_back();
                return node;
            }
            return ModelView::Instance(a_model);
        }

        void Pose(RE::NiNode& a_node, const Protocol::ItemPose& a_pose)
        {
            float m[3][3];
            Coords::ToMatrix({ a_pose.rotation[0], a_pose.rotation[1], a_pose.rotation[2], a_pose.rotation[3] }, m);
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    a_node.local.rotate.entry[r][c] = m[r][c];
                }
            }
            a_node.local.translate = RE::NiPoint3(a_pose.position[0], a_pose.position[1], a_pose.position[2]);
            RE::NiUpdateData update{};
            a_node.Update(update);
        }
    }

    void Update(const Protocol::ItemState* a_state, std::uint32_t a_generation)
    {
        if (!a_state || a_state->generation != a_generation) {
            Clear();
            return;
        }
        auto*                              scene = Scene();
        std::unordered_set<std::uint32_t>  seen;
        std::size_t                        shown = 0;
        const std::uint32_t                count = (std::min)(a_state->count, Protocol::kItemCapacity);
        for (std::uint32_t i = 0; i < count; i++) {
            const auto& pose = a_state->items[i];
            if (pose.id == 0) {
                continue;
            }
            seen.insert(pose.id);
            auto& item = state.items[pose.id];
            if (item.node && item.model != pose.model) {
                Put(item);
            }
            item.model = pose.model;
            if (!item.node && pose.model != 0) {
                item.node = Take(pose.model);
            }
            if (!item.node) {
                continue;
            }
            const bool visible = (pose.flags & Protocol::kItemVisible) != 0;
            item.node->SetAppCulled(!visible);
            if (scene && item.node->parent != scene) {
                if (item.node->parent) {
                    item.node->parent->DetachChild(item.node.get());
                }
                scene->AttachChild(item.node.get(), true);
            }
            Pose(*item.node, pose);
            shown += visible ? 1 : 0;
        }
        for (auto it = state.items.begin(); it != state.items.end();) {
            if (seen.contains(it->first)) {
                ++it;
                continue;
            }
            Put(it->second);
            it = state.items.erase(it);
        }
        if (shown != state.shown) {
            state.shown = shown;
            SKSE::log::info("item view: {} items shown of {}", shown, count);
        }
    }

    void Clear()
    {
        if (state.items.empty()) {
            return;
        }
        for (auto& [id, item] : state.items) {
            Put(item);
        }
        state.items.clear();
        state.shown = 0;
        SKSE::log::info("item view: cleared");
    }
}
