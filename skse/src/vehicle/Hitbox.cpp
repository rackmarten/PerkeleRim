#include "vehicle/Hitbox.h"

#include <unordered_set>

#include "Ini.h"
#include "model/ModelView.h"

namespace PerkeleRim::Hitbox
{
    namespace
    {
        // Skyrim's own Havok glue, which CommonLib doesn't have. The address ids are the ones the
        // Precision mod (ersh1) uses to build its keyframed weapon capsules on SE and AE.
        namespace Havok
        {
            // hkpRigidBodyCinfo as Havok 2010 lays it out: bhkRigidBodyCinfo::hkCinfo.
            struct BodyInfo
            {
                std::uint32_t collisionFilterInfo;  // 00
                std::uint32_t pad04;
                RE::hkpShape* shape;                      // 08
                void*         localFrame;                 // 10
                std::uint8_t  collisionResponse;          // 18
                std::uint8_t  pad19;
                std::uint16_t contactPointCallbackDelay;  // 1A
                std::uint32_t pad1C;
                RE::hkVector4 position;         // 20
                RE::hkVector4 rotation;         // 30, a quaternion
                RE::hkVector4 linearVelocity;   // 40
                RE::hkVector4 angularVelocity;  // 50
                RE::hkVector4 inertiaTensor[3];  // 60
                RE::hkVector4 centerOfMass;      // 90
                float         mass;              // A0
                float         linearDamping;     // A4
                float         angularDamping;    // A8
                float         timeFactor;        // AC
                float         gravityFactor;     // B0
                float         friction;          // B4
                float         rollingFrictionMultiplier;  // B8
                float         restitution;                // BC
                float         maxLinearVelocity;          // C0
                float         maxAngularVelocity;         // C4
                float         allowedPenetrationDepth;    // C8
                std::uint8_t  motionType;                 // CC, hkpMotion::MotionType
                bool          enableDeactivation;         // CD
                std::uint8_t  solverDeactivation;         // CE
                std::uint8_t  qualityType;                // CF, hkpCollidableQualityType
                std::uint8_t  padD0[0x10];
            };
            static_assert(offsetof(BodyInfo, position) == 0x20);
            static_assert(offsetof(BodyInfo, mass) == 0xA0);
            static_assert(offsetof(BodyInfo, motionType) == 0xCC);
            static_assert(sizeof(BodyInfo) == sizeof(RE::bhkRigidBodyCinfo::hkCinfo));

            constexpr std::uint8_t kKeyframedQuality = 1;     // hkpCollidableQualityType
            constexpr int          kDoActivate = 1;           // hkpEntityActivation

            void NewCapsule(RE::bhkShape* a_this)
            {
                static REL::Relocation<void (*)(RE::bhkShape*)> func{ RELOCATION_ID(76929, 78804) };
                func(a_this);
            }
            void SizeCapsule(RE::bhkShape* a_this, RE::hkVector4& a_from, RE::hkVector4& a_to, float a_radius)
            {
                static REL::Relocation<void (*)(RE::bhkShape*, RE::hkVector4&, RE::hkVector4&, float)> func{ RELOCATION_ID(76925, 78800) };
                func(a_this, a_from, a_to, a_radius);
            }
            void NewBodyInfo(RE::bhkRigidBodyCinfo* a_this)
            {
                static REL::Relocation<void (*)(RE::bhkRigidBodyCinfo*)> func{ RELOCATION_ID(76222, 78042) };
                func(a_this);
            }
            void NewBody(RE::bhkRigidBody* a_this)
            {
                static REL::Relocation<void (*)(RE::bhkRigidBody*)> func{ RELOCATION_ID(76315, 78147) };
                func(a_this);
            }
            void ApplyInfo(RE::bhkRigidBody* a_this, RE::bhkRigidBodyCinfo* a_info)
            {
                static REL::Relocation<void (*)(RE::bhkRigidBody*, RE::bhkRigidBodyCinfo*)> func{ RELOCATION_ID(76240, 78070) };
                func(a_this, a_info);
            }
            void SetActivated(RE::bhkRigidBody* a_this, bool a_active)
            {
                static REL::Relocation<void (*)(RE::bhkRigidBody*, bool)> func{ RELOCATION_ID(76258, 78088) };
                func(a_this, a_active);
            }
            void NewCollisionObject(RE::bhkCollisionObject* a_this, RE::NiNode* a_node)
            {
                static REL::Relocation<void (*)(RE::bhkCollisionObject*, RE::NiNode*)> func{ RELOCATION_ID(76531, 78373) };
                func(a_this, a_node);
            }
            void SetWorldObject(RE::bhkCollisionObject* a_this, RE::bhkWorldObject* a_body)
            {
                static REL::Relocation<void (*)(RE::bhkCollisionObject*, RE::bhkWorldObject*)> func{ RELOCATION_ID(76533, 78375) };
                func(a_this, a_body);
            }
            void AddEntity(RE::hkpWorld* a_world, RE::hkpEntity* a_entity)
            {
                static REL::Relocation<RE::hkpEntity* (*)(RE::hkpWorld*, RE::hkpEntity*, int)> func{ RELOCATION_ID(60492, 61304) };
                func(a_world, a_entity, kDoActivate);
            }
            // hkpWorld::removeEntity returns an hkBool, through a hidden pointer (CommonLib's
            // RemoveEntity leaves it out).
            void RemoveEntity(RE::hkpWorld* a_world, RE::hkpEntity* a_entity)
            {
                static REL::Relocation<bool* (*)(RE::hkpWorld*, bool*, RE::hkpEntity*)> func{ RELOCATION_ID(60493, 61305) };
                bool removed = false;
                func(a_world, &removed, a_entity);
            }
            // Velocities that take a keyframed body to the pose in 1 / a_invDelta seconds.
            void HardKeyFrame(const RE::hkVector4& a_position, const RE::hkQuaternion& a_rotation, float a_invDelta, RE::hkpRigidBody* a_body)
            {
                static REL::Relocation<void (*)(const RE::hkVector4&, const RE::hkQuaternion&, float, RE::hkpRigidBody*)> func{ RELOCATION_ID(61579, 62478) };
                func(a_position, a_rotation, a_invDelta, a_body);
            }
        }

        static_assert(sizeof(RE::bhkShape) == 0x28);  // bhkCapsuleShape adds nothing

        constexpr auto  kLayer = RE::COL_LAYER::kTransparentSmallAnim;
        // [Debug] HitboxLayer=<n> in PerkeleRim.ini tries another layer (the hitbox test), read once.
        std::uint32_t Layer()
        {
            static const std::uint32_t layer = [] {
                const auto value = Ini::Value("HitboxLayer");
                const int  n = value.empty() ? 0 : std::atoi(value.c_str());
                if (n > 0 && n < 64) {
                    SKSE::log::info("hitbox: on layer {} (HitboxLayer in PerkeleRim.ini)", n);
                    return static_cast<std::uint32_t>(n);
                }
                return static_cast<std::uint32_t>(kLayer);
            }();
            return layer;
        }
        constexpr float kTeleport = 300.0f;  // units: a jump this long (a respawn, a new anchor) is a teleport, not a motion
        constexpr int   kHitsLogged = 16;    // the first projectile hits are logged in full

        struct Part
        {
            RE::NiPointer<RE::bhkShape>     shape;
            RE::NiPointer<RE::bhkRigidBody> body;
            RE::NiPointer<RE::NiNode>             node;       // the collision object's, for Skyrim to find the body's owner (none: no reference)
            RE::NiPointer<RE::bhkCollisionObject> collision;  // links node and body
        };

        struct Body
        {
            std::string                 name;
            std::uint32_t               model = 0;  // ModelChunk.model the parts were built from
            std::vector<Part>           parts;
            RE::NiPointer<RE::bhkWorld> world;       // the world the parts are in, null while out
            std::uint32_t               group = 0;   // the player's collision group they are in
            bool                        solid = true;  // by Hitbox::Solid
            RE::NiPoint3                last;          // where it was keyframed last
            float                       lastRotation[4]{};
            bool                        moving = true;  // keyframed with a velocity last frame
            std::string                 out;           // why it is out of the world, as last logged
            Box                         box{};
            Protocol::VehicleState      state{};       // the pose it follows, this frame
            std::uint32_t               hits = 0;      // projectiles that hit it
            std::unordered_set<std::uint32_t> counted;  // projectiles counted, by handle, while they last
        };
        int hitsLogged = 0;
        // Never destroyed, like ModelView's nodes: at exit the game's destructors would run after its
        // heap and Havok are gone. Parts of a model MSC replaced (MSC restarted) go to retired, out of the world.
        auto& bodies = *new std::array<Body, Protocol::kVehicleCount>();
        auto& retired = *new std::vector<Part>();

        RE::hkVector4 HavokPosition(const float (&a_position)[3])
        {
            const float scale = RE::bhkWorld::GetWorldScale();
            return RE::hkVector4(a_position[0] * scale, a_position[1] * scale, a_position[2] * scale, 0.0f);
        }

        RE::hkQuaternion HavokRotation(const float (&a_rotation)[4])
        {
            RE::hkQuaternion q;
            q.vec = RE::hkVector4(a_rotation[0], a_rotation[1], a_rotation[2], a_rotation[3]);
            return q;
        }

        // One capsule as a keyframed body of its own, not in any world yet. Skyrim's capsule shape
        // wraps Havok's; its material makes hits on it sound and spark like metal.
        std::optional<Part> Make(const Capsule& a_capsule, const Protocol::VehicleState& a_state)
        {
            const float scale = RE::bhkWorld::GetWorldScale();
            auto*       shape = static_cast<RE::bhkShape*>(RE::malloc(sizeof(RE::bhkShape)));
            auto*       body = static_cast<RE::bhkRigidBody*>(RE::malloc(sizeof(RE::bhkRigidBody)));
            auto*       collision = static_cast<RE::bhkCollisionObject*>(RE::malloc(sizeof(RE::bhkCollisionObject)));
            if (!shape || !body || !collision) {
                return std::nullopt;
            }
            std::memset(shape, 0, sizeof(RE::bhkShape));
            Havok::NewCapsule(shape);
            RE::hkVector4 from(a_capsule.a[0] * scale, a_capsule.a[1] * scale, a_capsule.a[2] * scale, 0.0f);
            RE::hkVector4 to(a_capsule.b[0] * scale, a_capsule.b[1] * scale, a_capsule.b[2] * scale, 0.0f);
            Havok::SizeCapsule(shape, from, to, a_capsule.radius * scale);
            shape->materialID = RE::MATERIAL_ID::kMetalSolid;
            auto* hkShape = static_cast<RE::hkpShape*>(shape->referencedObject.get());
            if (!hkShape) {
                return std::nullopt;
            }

            alignas(16) RE::bhkRigidBodyCinfo info;
            Havok::NewBodyInfo(&info);
            auto& hk = *reinterpret_cast<Havok::BodyInfo*>(info.hkCinfo);
            info.collisionFilterInfo = hk.collisionFilterInfo = Layer();
            info.shape = hk.shape = hkShape;
            hk.motionType = static_cast<std::uint8_t>(RE::hkpMotion::MotionType::kKeyframed);
            // Parked, it may fall asleep, which is when Skyrim cuts the navmesh for a body on a
            // navmesh-obstacle layer (L_TRANSPARENT_SMALL_ANIM is one); Follow wakes it to move it.
            hk.enableDeactivation = true;
            hk.qualityType = Havok::kKeyframedQuality;
            hk.position = HavokPosition(a_state.position);
            hk.rotation = HavokRotation(a_state.rotation).vec;

            std::memset(body, 0, sizeof(RE::bhkRigidBody));
            Havok::NewBody(body);
            Havok::ApplyInfo(body, &info);
            if (!body->GetRigidBody()) {
                return std::nullopt;
            }

            Part part;
            part.shape.reset(shape);
            part.body.reset(body);
            part.node.reset(RE::NiNode::Create(0));
            part.node->name = "PerkeleRimHitbox";
            std::memset(collision, 0, sizeof(RE::bhkCollisionObject));
            Havok::NewCollisionObject(collision, part.node.get());
            static REL::Relocation<std::uintptr_t> vtable{ RE::VTABLE_bhkCollisionObject[0] };
            *reinterpret_cast<std::uintptr_t*>(collision) = vtable.address();
            Havok::SetWorldObject(collision, body);
            part.collision.reset(collision);
            return part;
        }

        void Build(Body& a_body, std::uint32_t a_model, const Protocol::VehicleState& a_state)
        {
            a_body.model = a_model;
            const auto* model = ModelView::Get(a_model);
            Box         box{};
            if (!model || !BoundsOf(*model, box)) {
                SKSE::log::warn("hitbox: the {} model {:08X} has no vertices, it stays without a hitbox", a_body.name, a_model);
                return;
            }
            a_body.box = box;
            const auto capsules = Capsules(box);
            if (capsules.empty()) {
                SKSE::log::warn("hitbox: the {} model {:08X} is {:.0f} x {:.0f} x {:.0f} units, no hitbox for that", a_body.name, a_model,
                    box.hi[0] - box.lo[0], box.hi[1] - box.lo[1], box.hi[2] - box.lo[2]);
                return;
            }
            for (const auto& capsule : capsules) {
                auto part = Make(capsule, a_state);
                if (!part) {
                    SKSE::log::error("hitbox: Skyrim didn't build a capsule for the {}, it stays without a hitbox", a_body.name);
                    a_body.parts.clear();
                    return;
                }
                a_body.parts.push_back(std::move(*part));
            }
            SKSE::log::info("hitbox: the {} model {:08X} is ({:.0f},{:.0f},{:.0f}) to ({:.0f},{:.0f},{:.0f}): {} capsules of radius {:.0f}",
                a_body.name, a_model, box.lo[0], box.lo[1], box.lo[2], box.hi[0], box.hi[1], box.hi[2], capsules.size(), capsules[0].radius);
        }

        void Remove(Body& a_body, std::string a_why)
        {
            if (a_body.world) {
                {
                    RE::BSWriteLockGuard lock(a_body.world->worldLock);
                    for (auto& part : a_body.parts) {
                        auto* rb = part.body->GetRigidBody();
                        if (rb && rb->world) {
                            Havok::RemoveEntity(rb->world, rb);
                        }
                    }
                }
                a_body.world.reset();
            }
            if (a_why != a_body.out) {
                SKSE::log::info("hitbox: the {} is out of Skyrim's world ({})", a_body.name, a_why);
                a_body.out = std::move(a_why);
            }
        }

        void Add(Body& a_body, RE::bhkWorld* a_world, std::uint32_t a_group, const Protocol::VehicleState& a_state)
        {
            auto* hkWorld = static_cast<RE::hkpWorld*>(a_world->referencedObject.get());
            if (!hkWorld) {
                return;
            }
            auto position = HavokPosition(a_state.position);
            auto rotation = HavokRotation(a_state.rotation);
            {
                RE::BSWriteLockGuard lock(a_world->worldLock);
                for (auto& part : a_body.parts) {
                    auto* rb = part.body->GetRigidBody();
                    rb->collidable.broadPhaseHandle.collisionFilterInfo.filter = (a_group << 16) | Layer();
                    part.body->SetPositionAndRotation(position, rotation);
                    part.body->SetLinearVelocity(RE::hkVector4(0.0f, 0.0f, 0.0f, 0.0f));
                    part.body->SetAngularVelocity(RE::hkVector4(0.0f, 0.0f, 0.0f, 0.0f));
                    Havok::SetActivated(part.body.get(), true);
                    Havok::AddEntity(hkWorld, rb);
                }
            }
            a_body.world.reset(a_world);
            a_body.group = a_group;
            a_body.last = RE::NiPoint3(a_state.position[0], a_state.position[1], a_state.position[2]);
            std::copy(a_state.rotation, a_state.rotation + 4, a_body.lastRotation);
            a_body.moving = true;
            a_body.out.clear();
            SKSE::log::info("hitbox: the {} is solid in Skyrim's world at ({:.0f},{:.0f},{:.0f}), {} capsules in the player's collision group {}", a_body.name,
                a_state.position[0], a_state.position[1], a_state.position[2], a_body.parts.size(), a_group);
            // Where Havok has them, back in game units, to tell a scale or world mix-up from the van's pose.
            const float scale = RE::bhkWorld::GetWorldScale();
            int         i = 0;
            for (auto& part : a_body.parts) {
                auto*       rb = part.body->GetRigidBody();
                float       t[4];
                _mm_storeu_ps(t, rb->motion.motionState.transform.translation.quad);
                SKSE::log::info("hitbox: the {}'s capsule {} is {} Havok's world at ({:.0f},{:.0f},{:.0f}), filter {:08X}, world scale {}", a_body.name, i++,
                    rb->world == hkWorld ? "in" : "NOT in", t[0] / scale, t[1] / scale, t[2] / scale, rb->collidable.broadPhaseHandle.collisionFilterInfo.filter, scale);
            }
        }

        // Every projectile Skyrim stopped at one of the capsules (its impact names the capsule's
        // collision object): counted once and logged, so a test can tell an arrow at the van from one
        // that fell short.
        void CountHits(Body& a_body)
        {
            auto* manager = RE::Projectile::Manager::GetSingleton();
            if (!manager) {
                return;
            }
            std::unordered_set<std::uint32_t> seen;
            RE::BSSpinLockGuard               guard(manager->projectileLock);
            for (auto* list : { &manager->limited, &manager->unlimited }) {
                for (auto& handle : *list) {
                    auto  ptr = handle.get();
                    auto* projectile = ptr.get();
                    if (!projectile) {
                        continue;
                    }
                    const std::uint32_t id = handle.native_handle();
                    seen.insert(id);
                    if (a_body.counted.contains(id)) {
                        continue;
                    }
                    bool hit = false;
                    for (auto* impact : projectile->GetProjectileRuntimeData().impacts) {
                        for (const auto& part : a_body.parts) {
                            hit = hit || (impact && impact->colObj && impact->colObj.get() == part.collision.get());
                        }
                    }
                    if (!hit) {
                        continue;
                    }
                    a_body.counted.insert(id);
                    a_body.hits++;
                    if (hitsLogged < kHitsLogged) {
                        hitsLogged++;
                        const auto shooter = projectile->GetProjectileRuntimeData().shooter.get();
                        auto*      base = projectile->GetProjectileBase();
                        const auto at = projectile->GetPosition();
                        SKSE::log::info("hitbox: {}'s {} hit the {} at ({:.0f},{:.0f},{:.0f}), hit {} on it", shooter ? shooter->GetName() : "someone",
                            base ? base->GetName() : "projectile", a_body.name, at.x, at.y, at.z, a_body.hits);
                    }
                }
            }
            std::erase_if(a_body.counted, [&](std::uint32_t a_id) { return !seen.contains(a_id); });
        }

        // Each capsule moves to the vehicle's pose over the next frame (teleports after a jump).
        void Follow(Body& a_body, const Protocol::VehicleState& a_state)
        {
            const RE::NiPoint3 at(a_state.position[0], a_state.position[1], a_state.position[2]);
            const float        moved = at.GetDistance(a_body.last);
            float              turned = 0.0f;
            for (int k = 0; k < 4; k++) {
                turned = (std::max)(turned, std::fabs(a_state.rotation[k] - a_body.lastRotation[k]));
            }
            const bool jump = moved > kTeleport;
            const bool still = moved < 0.05f && turned < 1e-5f;
            if (still && !a_body.moving) {
                return;  // left asleep
            }
            a_body.moving = !still;
            a_body.last = at;
            std::copy(a_state.rotation, a_state.rotation + 4, a_body.lastRotation);
            auto        position = HavokPosition(a_state.position);
            auto        rotation = HavokRotation(a_state.rotation);
            const float delta = std::clamp(RE::GetSecondsSinceLastFrame(), 1.0f / 240.0f, 0.1f);
            RE::BSWriteLockGuard lock(a_body.world->worldLock);
            for (auto& part : a_body.parts) {
                if (!still) {
                    Havok::SetActivated(part.body.get(), true);
                }
                if (jump) {
                    part.body->SetPositionAndRotation(position, rotation);
                    part.body->SetLinearVelocity(RE::hkVector4(0.0f, 0.0f, 0.0f, 0.0f));
                    part.body->SetAngularVelocity(RE::hkVector4(0.0f, 0.0f, 0.0f, 0.0f));
                } else {
                    Havok::HardKeyFrame(position, rotation, 1.0f / delta, part.body->GetRigidBody());
                }
            }
        }
    }

    void Show(std::uint32_t a_vehicle, std::string_view a_name, std::uint32_t a_model, const Protocol::VehicleState& a_state)
    {
        if (a_vehicle < 1 || a_vehicle > Protocol::kVehicleCount) {
            return;
        }
        if (a_model == 0) {
            Hide(a_vehicle);
            return;
        }
        auto& body = bodies[a_vehicle - 1];
        if (body.model != a_model) {
            body.name = a_name;
            if (!body.parts.empty()) {
                Remove(body, "its model changed");
            }
            std::move(body.parts.begin(), body.parts.end(), std::back_inserter(retired));
            body.parts.clear();
            Build(body, a_model, a_state);
        }
        if (body.parts.empty()) {
            return;
        }

        const float speed = std::sqrt(a_state.velocity[0] * a_state.velocity[0] + a_state.velocity[1] * a_state.velocity[1] +
                                      a_state.velocity[2] * a_state.velocity[2]) *
                            static_cast<float>(Coords::kMetersPerUnit);
        body.solid = Solid(speed, body.solid);
        auto*      player = RE::PlayerCharacter::GetSingleton();
        auto*      cell = player ? player->GetParentCell() : nullptr;
        auto*      world = cell ? cell->GetbhkWorld() : nullptr;
        RE::CFilter filter{};
        if (player) {
            player->GetCollisionFilterInfo(filter);
        }
        const std::uint32_t group = filter.GetSystemGroup();
        const char*         why = !body.solid ? "moving at run-over speed" : !world ? "no Havok world" : group == 0 ? "the player has no collision group yet" : nullptr;
        if (why) {
            Remove(body, why);
            return;
        }
        if (body.world && (body.world.get() != world || body.group != group)) {
            Remove(body, body.world.get() != world ? "the player's world changed" : "the player's collision group changed");
        }
        body.state = a_state;
        if (!body.world) {
            Add(body, world, group, a_state);
        } else {
            Follow(body, a_state);
        }
        CountHits(body);
    }

    void Hide(std::uint32_t a_vehicle)
    {
        if (a_vehicle >= 1 && a_vehicle <= Protocol::kVehicleCount && !bodies[a_vehicle - 1].parts.empty()) {
            Remove(bodies[a_vehicle - 1], "not drawn");
        }
    }
}

namespace PerkeleRim::Hitbox
{
    bool Where(std::uint32_t a_vehicle, Protocol::VehicleState& a_state, Box& a_box)
    {
        if (a_vehicle < 1 || a_vehicle > Protocol::kVehicleCount || !bodies[a_vehicle - 1].world) {
            return false;
        }
        a_state = bodies[a_vehicle - 1].state;
        a_box = bodies[a_vehicle - 1].box;
        return true;
    }

    std::uint32_t Hits(std::uint32_t a_vehicle)
    {
        return a_vehicle >= 1 && a_vehicle <= Protocol::kVehicleCount ? bodies[a_vehicle - 1].hits : 0;
    }

    bool Owns(const RE::bhkCollisionObject* a_object)
    {
        for (const auto& body : bodies) {
            for (const auto& part : body.parts) {
                if (a_object && part.collision.get() == a_object) {
                    return true;
                }
            }
        }
        return false;
    }

    bool Owns(const RE::hkpCollidable* a_collidable)
    {
        for (const auto& body : bodies) {
            for (const auto& part : body.parts) {
                auto* rb = part.body->GetRigidBody();
                if (a_collidable && rb && rb->GetCollidable() == a_collidable) {
                    return true;
                }
            }
        }
        return false;
    }
}
