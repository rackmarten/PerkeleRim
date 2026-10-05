#include "collision/CollisionSampler.h"

#include "collision.h"
#include "link/LinkService.h"

namespace PerkeleRim::CollisionSampler
{
    namespace
    {
        using Clock = std::chrono::steady_clock;
        using Layer = RE::COL_LAYER;

        constexpr std::uint32_t kCells = 32;          // 64 units (about 0.9 m) between samples
        constexpr std::uint32_t kSamples = (kCells + 1) * (kCells + 1);
        constexpr int           kRadius = 2;          // chunks around the player's chunk: 5 x 5, about 70 m each way
        constexpr int           kForgetRadius = kRadius + 1;  // MSC keeps chunks one further, so a forgotten chunk is always resent
        constexpr float         kRayAbove = 10000.0f;  // rays run from this far above the player...
        constexpr float         kRayBelow = 10000.0f;  // ...to this far below
        constexpr int           kMaxPassThrough = 8;   // unwanted surfaces a ray may pass through
        constexpr auto          kBudget = 2ms;         // sampling time per frame
        constexpr auto          kRetryAfter = 2s;      // before sampling a chunk with holes again
        constexpr int           kMaxTries = 5;

        bool Wanted(Layer a_layer)
        {
            switch (a_layer) {
            case Layer::kStatic:
            case Layer::kAnimStatic:
            case Layer::kTerrain:
            case Layer::kGround:
            case Layer::kTrees:
            case Layer::kStairHelper:
                return true;
            default:
                return false;
            }
        }

        using Key = std::pair<std::int32_t, std::int32_t>;

        struct Known
        {
            std::uint32_t     sentHits = 0;
            int               tries = 0;
            Clock::time_point retryAt{};
        };

        struct Stats
        {
            int terrain = 0, statics = 0, trees = 0, passed = 0;
            int frames = 0;
            Clock::duration time{};
        };

        struct State
        {
            bool          active = false;
            std::uint32_t generation = 0;
            std::uint32_t worldspace = 0;
            std::map<Key, Known> known;

            bool                 sampling = false;
            Collision::Heightfield field;
            std::uint32_t        next = 0;  // next sample index in field
            float                rayTop = 0.0f;
            float                rayBottom = 0.0f;
            Stats                stats;

            std::vector<std::uint8_t> pending;  // payload waiting for room in the ring
            Key                       pendingKey{};
            std::uint32_t             pendingHits = 0;
        } state;

        // Chunk offsets around the player's chunk, nearest first.
        const std::vector<Key>& Order()
        {
            static const std::vector<Key> order = [] {
                std::vector<Key> v;
                for (int dy = -kRadius; dy <= kRadius; dy++) {
                    for (int dx = -kRadius; dx <= kRadius; dx++) {
                        v.emplace_back(dx, dy);
                    }
                }
                std::ranges::stable_sort(v, {}, [](const Key& a_k) { return a_k.first * a_k.first + a_k.second * a_k.second; });
                return v;
            }();
            return order;
        }

        int Distance(const Key& a_a, const Key& a_b)
        {
            return (std::max)(std::abs(a_a.first - a_b.first), std::abs(a_a.second - a_b.second));
        }

        bool Pick(const Key& a_center, const RE::NiPoint3& a_player)
        {
            const auto now = Clock::now();
            for (const auto& [dx, dy] : Order()) {
                const Key key{ a_center.first + dx, a_center.second + dy };
                const auto it = state.known.find(key);
                if (it != state.known.end() && (it->second.sentHits >= kSamples || it->second.tries >= kMaxTries || now < it->second.retryAt)) {
                    continue;
                }
                state.field.Reset(key.first, key.second, kCells);
                state.next = 0;
                state.rayTop = a_player.z + kRayAbove;
                state.rayBottom = a_player.z - kRayBelow;
                state.stats = {};
                state.sampling = true;
                return true;
            }
            return false;
        }

        // Top wanted surface under (x, y), passing through clutter, actors and the like.
        bool Cast(const RE::hkpWorld* a_world, float a_x, float a_y, float& a_z)
        {
            const float              scale = RE::bhkWorld::GetWorldScale();
            RE::hkpWorldRayCastInput input;
            input.filterInfo.SetCollisionLayer(Layer::kLOS);
            float top = state.rayTop;
            for (int i = 0; i <= kMaxPassThrough; i++) {
                input.from = RE::hkVector4(a_x * scale, a_y * scale, top * scale, 0.0f);
                input.to = RE::hkVector4(a_x * scale, a_y * scale, state.rayBottom * scale, 0.0f);
                RE::hkpWorldRayCastOutput output;
                a_world->CastRay(input, output);
                if (!output.HasHit()) {
                    return false;
                }
                const float z = top + (state.rayBottom - top) * output.hitFraction;
                const auto  layer = output.rootCollidable->GetCollisionLayer();
                if (Wanted(layer)) {
                    a_z = z;
                    (layer == Layer::kTerrain ? state.stats.terrain : layer == Layer::kTrees ? state.stats.trees : state.stats.statics)++;
                    return true;
                }
                state.stats.passed++;
                top = z - 1.0f;
                if (top <= state.rayBottom) {
                    return false;
                }
            }
            return false;
        }

        bool TrySend()
        {
            if (!LinkService::Get().Send(Protocol::MessageType::CollisionChunk, state.pending.data(), static_cast<std::uint32_t>(state.pending.size()))) {
                return false;
            }
            state.known[state.pendingKey].sentHits = state.pendingHits;
            state.pending.clear();
            return true;
        }

        void Finish()
        {
            state.sampling = false;
            const auto& f = state.field;
            const Key   key{ f.chunkX, f.chunkY };
            auto&       known = state.known[key];
            const auto  hits = f.Hits();
            known.tries++;
            known.retryAt = Clock::now() + kRetryAfter;

            const bool improved = hits > known.sentHits;
            SKSE::log::info(
                "collision: chunk ({},{}) gen {} try {}: {}/{} hit (terrain {}, static {}, trees {}, passed through {}), {:.1f} ms over {} frames{}",
                key.first, key.second, state.generation, known.tries, hits, f.Samples(), state.stats.terrain, state.stats.statics,
                state.stats.trees, state.stats.passed, std::chrono::duration<double, std::milli>(state.stats.time).count(),
                state.stats.frames, improved ? ", sent" : ", nothing new");
            if (!improved) {
                return;
            }
            state.pending = Collision::BuildChunkPayload(f, state.worldspace, state.generation);
            state.pendingKey = key;
            state.pendingHits = hits;
            TrySend();
        }
    }

    void Reset(std::uint32_t a_generation, std::uint32_t a_worldspace)
    {
        state.active = true;
        state.generation = a_generation;
        state.worldspace = a_worldspace;
        state.known.clear();
        state.sampling = false;
        state.pending.clear();
    }

    void Stop()
    {
        state.active = false;
        state.sampling = false;
        state.pending.clear();
        state.known.clear();
    }

    void Update(RE::PlayerCharacter* a_player)
    {
        if (!state.active) {
            return;
        }
        const auto pos = a_player->GetPosition();
        const Key  center{ Collision::ChunkIndex(pos.x), Collision::ChunkIndex(pos.y) };
        std::erase_if(state.known, [&](const auto& a_entry) { return Distance(a_entry.first, center) > kForgetRadius; });

        if (!state.pending.empty() && !TrySend()) {
            return;  // ring full, MSC is behind
        }
        if (state.sampling && Distance({ state.field.chunkX, state.field.chunkY }, center) > kRadius) {
            state.sampling = false;  // walked away from it
        }
        if (!state.sampling && !Pick(center, pos)) {
            return;
        }

        auto* cell = a_player->GetParentCell();
        auto* bhk = cell ? cell->GetbhkWorld() : nullptr;
        auto* world = bhk ? bhk->GetWorld1() : nullptr;
        if (!world) {
            return;
        }

        const auto start = Clock::now();
        {
            RE::BSReadLockGuard lock(bhk->worldLock);
            auto&               f = state.field;
            const auto          side = f.Side();
            while (state.next < f.Samples()) {
                const std::uint32_t k = state.next++;
                float               z = 0.0f;
                if (Cast(world, f.X(k % side), f.Y(k / side), z)) {
                    f.heights[k] = z;
                    f.hit[k] = 1;
                }
                if ((state.next & 15) == 0 && Clock::now() - start >= kBudget) {
                    break;
                }
            }
        }
        state.stats.time += Clock::now() - start;
        state.stats.frames++;
        if (state.next >= state.field.Samples()) {
            Finish();
        }
    }
}
