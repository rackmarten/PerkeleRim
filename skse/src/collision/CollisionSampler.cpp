#include "collision/CollisionSampler.h"

#include "collision.h"
#include "collision/StaticMeshes.h"
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
        constexpr int           kForgetRadius = kRadius + 1;  // from both centers; MSC keeps chunks within 6 of the player, so a forgotten chunk is always resent
        constexpr float         kLookAhead = 2.0f;            // seconds of travel the second center sits ahead of the player
        constexpr float         kMaxAhead = 2.0f * Protocol::kCollisionChunkUnits;  // ... but at most 2 chunks
        constexpr float         kRayAbove = 10000.0f;  // rays run from this far above the player...
        constexpr float         kRayBelow = 10000.0f;  // ...to this far below
        constexpr int           kMaxPassThrough = 32;  // other surfaces a ray may pass through on its way to the terrain (roofs, floors, bridges)
        constexpr auto          kBudget = 2ms;         // sampling time per frame
        constexpr auto          kRetryAfter = 2s;      // before sampling an unfinished chunk again
        constexpr int           kMaxTries = 5;
        constexpr auto          kDoorSwing = 1500ms;   // a door's collision is where it stops after this
        constexpr float         kDoorReach = 256.0f;   // chunks this close to a door that moved are read again

        using Key = std::pair<std::int32_t, std::int32_t>;

        struct Known
        {
            std::uint32_t     sentScore = 0;  // terrain hits + static triangles in what MSC has
            bool              done = false;   // terrain complete and every static's 3D was in
            int               tries = 0;
            Clock::time_point retryAt{};
        };

        struct Stats
        {
            int terrain = 0, passed = 0;
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
            bool                 interior = false;  // no terrain: the chunk is its statics only
            Collision::Heightfield field;
            std::uint32_t        next = 0;  // next sample index in field
            float                rayTop = 0.0f;
            float                rayBottom = 0.0f;
            Stats                stats;

            std::vector<std::uint8_t> pending;  // payload waiting for room in the ring
            Key                       pendingKey{};
            std::uint32_t             pendingScore = 0;

            std::vector<std::pair<RE::NiPoint3, Clock::time_point>> doors;  // doors that moved, and when they will have stopped
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

        // The player's chunks first, then those around the point ahead of it; nearest first in each.
        bool Pick(const Key& a_center, const Key& a_ahead, const RE::NiPoint3& a_player, bool a_interior)
        {
            const auto now = Clock::now();
            for (const Key& center : { a_center, a_ahead }) {
                for (const auto& [dx, dy] : Order()) {
                    const Key  key{ center.first + dx, center.second + dy };
                    const auto it = state.known.find(key);
                    if (it != state.known.end() && (it->second.done || it->second.tries >= kMaxTries || now < it->second.retryAt)) {
                        continue;
                    }
                    state.field.Reset(key.first, key.second, kCells);
                    state.interior = a_interior;
                    state.next = a_interior ? state.field.Samples() : 0;  // interiors have no terrain to sample
                    state.rayTop = a_player.z + kRayAbove;
                    state.rayBottom = a_player.z - kRayBelow;
                    state.stats = {};
                    state.sampling = true;
                    return true;
                }
            }
            return false;
        }

        // The landscape: the terrain layer, or in some cells the ground layer, which then belongs to
        // no reference (statics on the ground layer come with the statics).
        bool IsLand(const RE::hkpCollidable& a_collidable)
        {
            switch (a_collidable.GetCollisionLayer()) {
            case Layer::kTerrain:
                return true;
            case Layer::kGround:
                return !RE::TESHavokUtilities::FindCollidableRef(a_collidable);
            default:
                return false;
            }
        }

        // Land height under (x, y), passing through everything else: statics come as their own
        // triangles, clutter, actors and water stay out.
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
                if (IsLand(*output.rootCollidable)) {
                    a_z = z;
                    state.stats.terrain++;
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

        struct Roads
        {
            std::vector<float> triangles;  // 6 floats each: three world x, y corners
            int                navmeshes = 0;  // navmeshes of the loaded cells
        };

        // The navmesh triangles flagged preferred (Bethesda's roads, for NPC pathing) of the loaded
        // cells that reach into the chunk: MSC only ramps steps on them.
        Roads CollectRoads(const Key& a_key)
        {
            Roads roads;
            auto* tes = RE::TES::GetSingleton();
            if (!tes) {
                return roads;
            }
            tes->ForEachCell([&](RE::TESObjectCELL* a_cell) {
                auto* array = a_cell ? a_cell->GetRuntimeData().navMeshes : nullptr;
                if (!array) {
                    return;
                }
                for (const auto& navmesh : array->navMeshes) {
                    if (!navmesh) {
                        continue;
                    }
                    roads.navmeshes++;
                    const auto& vertices = navmesh->vertices;
                    for (const auto& triangle : navmesh->triangles) {
                        if (!triangle.triangleFlags.all(RE::BSNavmeshTriangle::TriangleFlag::kPreferred) ||
                            triangle.triangleFlags.any(RE::BSNavmeshTriangle::TriangleFlag::kDeleted)) {
                            continue;
                        }
                        float x[3], y[3];
                        bool  valid = true;
                        for (int c = 0; c < 3; c++) {
                            if (triangle.vertices[c] >= vertices.size()) {
                                valid = false;
                                break;
                            }
                            x[c] = vertices[triangle.vertices[c]].location.x;
                            y[c] = vertices[triangle.vertices[c]].location.y;
                        }
                        if (valid && Collision::RoadTouchesChunk(a_key.first, a_key.second, x, y)) {
                            roads.triangles.insert(roads.triangles.end(), { x[0], y[0], x[1], y[1], x[2], y[2] });
                        }
                    }
                }
            });
            return roads;
        }

        bool TrySend()
        {
            if (!LinkService::Get().Send(Protocol::MessageType::CollisionChunk, state.pending.data(), static_cast<std::uint32_t>(state.pending.size()))) {
                return false;
            }
            state.known[state.pendingKey].sentScore = state.pendingScore;
            state.pending.clear();
            return true;
        }

        void Finish()
        {
            state.sampling = false;
            const auto& f = state.field;
            const Key   key{ f.chunkX, f.chunkY };
            auto&       known = state.known[key];
            known.tries++;
            known.retryAt = Clock::now() + kRetryAfter;

            const auto start = Clock::now();
            const auto statics = StaticMeshes::Collect(key.first, key.second);
            const auto collect = Clock::now() - start;
            const auto roads = CollectRoads(key);
            const auto navmesh = Clock::now() - start - collect;
            const auto hits = f.Hits();
            const auto triangles = static_cast<std::uint32_t>(statics.triangles.size() / 9);
            const auto roadCount = static_cast<std::uint32_t>(roads.triangles.size() / 6);
            const auto score = hits + triangles + roadCount;
            // A few land samples always miss (terrain holes at cave mouths), so 99% counts as complete.
            known.done = (state.interior || hits * 100 >= f.Samples() * 99) && statics.pending == 0;

            const bool improved = score > known.sentScore;
            std::uint32_t dropped = 0;
            if (improved) {
                state.pending = Collision::BuildChunkPayload(f, state.worldspace, state.generation, statics.triangles, &dropped, roads.triangles);
                state.pendingKey = key;
                state.pendingScore = score;
            }
            std::string others;
            for (const auto& [layer, count] : statics.otherLayers) {
                others += std::format(" {}x{}", count, layer);
            }
            SKSE::log::info(
                "collision: chunk ({},{}) gen {} try {}: terrain {}/{} (passed through {}), statics {} triangles from {} refs ({} not loaded yet, {} transformed, {} shapes skipped{}{}), roads {} triangles from {} navmeshes, {:.1f} ms over {} frames + {:.1f} ms statics + {:.1f} ms navmesh{}{}",
                key.first, key.second, state.generation, known.tries, hits, state.interior ? 0 : f.Samples(), state.stats.passed,
                triangles, statics.refs, statics.pending, statics.transformed, statics.skipped, dropped ? std::format(", {} over the vertex limit", dropped) : "", others.empty() ? "" : ", bodies left out by layer:" + others,
                roadCount, roads.navmeshes, std::chrono::duration<double, std::milli>(state.stats.time).count(), state.stats.frames,
                std::chrono::duration<double, std::milli>(collect).count(), std::chrono::duration<double, std::milli>(navmesh).count(),
                improved ? ", sent" : ", nothing new", known.done ? "" : ", again later");
            if (improved) {
                TrySend();
            }
        }

        // A chunk is read once, so a door (a cell door, a door without a load door) keeps the
        // collision it had then. Once one has swung, the chunks around it are read and sent again.
        void RefreshDoors()
        {
            const auto now = Clock::now();
            std::erase_if(state.doors, [&](const auto& a_door) {
                if (now < a_door.second) {
                    return false;
                }
                const auto& p = a_door.first;
                for (const float dx : { -kDoorReach, kDoorReach }) {
                    for (const float dy : { -kDoorReach, kDoorReach }) {
                        const auto it = state.known.find({ Collision::ChunkIndex(p.x + dx), Collision::ChunkIndex(p.y + dy) });
                        if (it != state.known.end()) {
                            it->second = {};  // read again, and sent whatever it holds
                        }
                    }
                }
                SKSE::log::info("collision: a door at ({:.0f},{:.0f},{:.0f}) moved, reading its chunks again", p.x, p.y, p.z);
                return true;
            });
        }

        class DoorSink : public RE::BSTEventSink<RE::TESOpenCloseEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::TESOpenCloseEvent* a_event, RE::BSTEventSource<RE::TESOpenCloseEvent>*) override
            {
                auto* ref = a_event ? a_event->ref.get() : nullptr;
                if (state.active && ref && ref->GetBaseObject() && ref->GetBaseObject()->Is(RE::FormType::Door)) {
                    state.doors.emplace_back(ref->GetPosition(), Clock::now() + kDoorSwing);
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void OnDataLoaded()
    {
        static DoorSink doorSink;
        if (auto* events = RE::ScriptEventSourceHolder::GetSingleton()) {
            events->AddEventSink<RE::TESOpenCloseEvent>(&doorSink);
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
        state.doors.clear();
    }

    void Stop()
    {
        state.active = false;
        state.sampling = false;
        state.pending.clear();
        state.known.clear();
        state.doors.clear();
    }

    void Update(RE::PlayerCharacter* a_player, const RE::NiPoint3& a_velocity)
    {
        if (!state.active) {
            return;
        }
        auto*      cell = a_player->GetParentCell();
        const auto pos = a_player->GetPosition();
        const Key  center{ Collision::ChunkIndex(pos.x), Collision::ChunkIndex(pos.y) };
        RE::NiPoint3 ahead(a_velocity.x * kLookAhead, a_velocity.y * kLookAhead, 0.0f);
        if (const float length = ahead.Length(); length > kMaxAhead) {
            ahead *= kMaxAhead / length;
        }
        const Key aheadKey{ Collision::ChunkIndex(pos.x + ahead.x), Collision::ChunkIndex(pos.y + ahead.y) };
        const auto inRange = [&](const Key& a_key, int a_radius) {
            return Distance(a_key, center) <= a_radius || Distance(a_key, aheadKey) <= a_radius;
        };
        std::erase_if(state.known, [&](const auto& a_entry) { return !inRange(a_entry.first, kForgetRadius); });

        RefreshDoors();
        if (!state.pending.empty() && !TrySend()) {
            return;  // ring full, MSC is behind
        }
        if (state.sampling && !inRange({ state.field.chunkX, state.field.chunkY }, kRadius)) {
            state.sampling = false;  // moved away from it
        }
        if (!state.sampling && (!cell || !Pick(center, aheadKey, pos, cell->IsInteriorCell()))) {
            return;
        }

        auto* bhk = cell->GetbhkWorld();
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
            state.stats.time += Clock::now() - start;
            state.stats.frames++;
            if (state.next >= f.Samples()) {
                Finish();  // reads the statics' shapes, so still under the lock
            }
        }
    }
}
