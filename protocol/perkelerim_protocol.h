// PerkeleRim link protocol: the layout of the shared memory both halves map.
//
// Source of truth for the layout. msc/src/Link/Protocol.cs mirrors it, and the C# tests parse the
// static_asserts below and check the mirror against them, so keep every size, offset and constant
// as a plain decimal or hex literal. Explained in docs/PROTOCOL.md.
//
// Rules:
// - Everything is packed (no implicit padding), little-endian, fixed size.
// - LinkHeader is frozen across versions, so a peer can always read the other side's version.
// - Any other change to a struct, offset or constant bumps kProtocolVersion.

#pragma once

#include <cstddef>
#include <cstdint>

namespace PerkeleRim::Protocol
{
    inline constexpr std::uint32_t kMagic = 0x4D495250;  // "PRIM" in memory
    inline constexpr std::uint32_t kProtocolVersion = 25;

    // Shared memory section, per Windows session. Whichever game starts first creates it.
    inline constexpr const wchar_t* kMappingName = L"Local\\PerkeleRim.Link";

    // A peer counts as gone when its heartbeat hasn't moved for this long.
    inline constexpr std::uint32_t kPeerTimeoutMs = 2000;

    // Collision chunks are squares of this many Skyrim units on a grid anchored at world (0, 0):
    // chunk (x, y) covers [x * size, (x + 1) * size) on X and the same on Y.
    inline constexpr std::uint32_t kCollisionChunkUnits = 2048;

    // Section layout (byte offsets from the start of the mapping).
    inline constexpr std::uint32_t kOffsetHeader = 0;
    inline constexpr std::uint32_t kOffsetInputSlot = 128;
    inline constexpr std::uint32_t kOffsetPlayerSlot = 256;
    inline constexpr std::uint32_t kOffsetVehicleSlot = 384;
    inline constexpr std::uint32_t kOffsetVehiclePartsSlot = 512;
    inline constexpr std::uint32_t kOffsetItemSlot = 2112;
    inline constexpr std::uint32_t kOffsetHandLayerSlot = 7296;
    inline constexpr std::uint32_t kOffsetActorSlot = 7360;
    inline constexpr std::uint32_t kOffsetRingToMsc = 8960;         // RingHeader, data follows
    inline constexpr std::uint32_t kRingToMscCapacity = 4194304;    // 4 MiB: collision chunks
    inline constexpr std::uint32_t kOffsetRingToSkyrim = 4203328;   // 8960 + 64 + 4 MiB
    inline constexpr std::uint32_t kRingToSkyrimCapacity = 262144;  // 256 KiB: events
    inline constexpr std::uint32_t kMappingSize = 4465536;         // 4203328 + 64 + 256 KiB

    enum class Side : std::uint32_t
    {
        Skyrim = 0,
        Msc = 1,
    };

#pragma pack(push, 1)

    // ---- Header (frozen) ----

    // One per side, written only by that side.
    struct PeerInfo
    {
        std::uint32_t version;    // kProtocolVersion of that side, 0 while not attached
        std::uint32_t pid;        // process id, 0 after a clean detach
        std::uint64_t heartbeat;  // bumped every tick while attached
        std::uint64_t reserved[2];
    };

    struct LinkHeader
    {
        std::uint32_t magic;           // written last by the creator: 0 means "still initializing"
        std::uint32_t headerSize;      // sizeof(LinkHeader)
        std::uint32_t mappingSize;     // kMappingSize of the creator
        std::uint32_t creatorVersion;  // kProtocolVersion of the creator
        PeerInfo      peers[2];        // indexed by Side
        std::uint8_t  reserved[48];
    };

    // ---- Latest-value slots (seqlock) ----
    //
    // Continuous state: the reader only wants the newest value, so there is no queue.
    // Writer: seq += 1 (odd = writing), write value, seq += 1. Reader: read seq, copy value,
    // read seq again; retry if the two differ or seq is odd. seq == 0 means never written.

    inline constexpr std::uint32_t kInputOwnsPlayer = 0x1;  // InputState.flags: MSC should apply this input
    inline constexpr std::uint32_t kInputPaused = 0x2;      // InputState.flags: a Skyrim menu pauses the game, MSC freezes its time

    // Skyrim -> MSC. Keys are Unity KeyCode values (mouse buttons are KeyCode.Mouse0..Mouse6).
    struct InputState
    {
        std::uint32_t frame;       // Skyrim frame counter
        std::uint32_t flags;       // kInput*
        double        mouseX;      // accumulated "Mouse X" axis since Skyrim started; MSC applies the delta
        double        mouseY;      // accumulated "Mouse Y" axis
        float         horizontal;  // "Horizontal" axis, -1..1
        float         vertical;    // "Vertical" axis, -1..1
        std::uint64_t keys[8];     // bit n set = KeyCode n held (n < 512)
        double        mouseWheel;  // accumulated "Mouse ScrollWheel" axis; MSC applies the delta
        std::uint32_t screenWidth;   // Skyrim's back buffer in pixels; MSC renders its hand layer this size
        std::uint32_t screenHeight;
    };

    struct InputSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        InputState    value;
        std::uint8_t  pad[8];
    };

    inline constexpr std::uint32_t kPlayerValid = 0x1;   // PlayerState.flags: MSC is simulating the player
    inline constexpr std::uint32_t kPlayerCamera = 0x2;  // PlayerState.flags: eye, roll and fov are set
    inline constexpr std::uint32_t kPlayerNeeds = 0x4;   // PlayerState.flags: needs are set
    inline constexpr std::uint32_t kPlayerGrounded = 0x8;  // PlayerState.flags: MSC's player stands on something
    inline constexpr std::uint32_t kPlayerBlocking = 0x10;  // PlayerState.flags: MSC's player guards (right click), Skyrim blocks

    // PlayerState.needs order, MSC's globals: PlayerThirst, PlayerHunger, PlayerStress, PlayerUrine,
    // PlayerFatigue, PlayerDirtiness (0..100, stress can go past it) and PlayerDrunk (0..8).
    inline constexpr std::uint32_t kNeedThirst = 0;
    inline constexpr std::uint32_t kNeedHunger = 1;
    inline constexpr std::uint32_t kNeedStress = 2;
    inline constexpr std::uint32_t kNeedUrine = 3;
    inline constexpr std::uint32_t kNeedFatigue = 4;
    inline constexpr std::uint32_t kNeedDirtiness = 5;
    inline constexpr std::uint32_t kNeedDrunk = 6;
    inline constexpr std::uint32_t kNeedCount = 7;

    // MSC -> Skyrim. Skyrim world space: game units, Z up, heading 0 = north, clockwise.
    struct PlayerState
    {
        std::uint32_t frame;        // MSC FixedUpdate counter
        std::uint32_t flags;        // kPlayer*
        float         position[3];  // feet
        float         velocity[3];  // units per second
        float         heading;      // radians
        float         pitch;        // radians, positive looks down
        std::uint8_t  stance;       // 0 standing, 1 crouching, 2 prone
        std::uint8_t  reserved[3];
        std::uint32_t generation;   // WorldspaceChange.generation this pose belongs to
        // MSC's camera, with kPlayerCamera. Leaning, reaching, sitting and crouching move it, and
        // Skyrim puts its first-person camera there.
        float eye[3];  // camera position relative to position (the feet), Skyrim axes and units
        float roll;    // radians about the view direction, positive tilts the view right (right ear down)
        float fov;     // vertical field of view, degrees (MSC's Ctrl zoom narrows it)
        float needs[7];  // kNeedCount values in kNeed* order, with kPlayerNeeds
    };

    struct PlayerSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        PlayerState   value;
        std::uint8_t  pad[24];
    };

    inline constexpr std::uint32_t kVehicleValid = 0x1;   // VehicleState.flags: the vehicle is on the current island
    inline constexpr std::uint32_t kVehicleDriven = 0x2;  // VehicleState.flags: the player sits in it

    inline constexpr std::uint32_t kVehicleHayosiko = 1;  // VehicleState.vehicle: the Hayosiko van

    // MSC -> Skyrim. One vehicle for now. Skyrim world space like PlayerState; the rotation is a
    // quaternion in Skyrim space, so the vehicle's local +Y is forward and +Z is up.
    struct VehicleState
    {
        std::uint32_t frame;         // MSC FixedUpdate counter
        std::uint32_t flags;         // kVehicle*
        std::uint32_t vehicle;       // kVehicle* id
        std::uint32_t generation;    // WorldspaceChange.generation this pose belongs to
        float         position[3];   // origin of the vehicle's body
        float         rotation[4];   // x, y, z, w
        float         velocity[3];   // units per second
        float         steer;         // front wheel angle, radians, positive turns right
        float         wheelSpin[4];  // radians about the axle, in [-pi, pi], positive rolls forward:
                                     // front left, front right, rear left, rear right
        std::uint32_t reserved;
    };

    struct VehicleSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        VehicleState  value;
        std::uint8_t  pad[40];
    };

    inline constexpr std::uint32_t kVehiclePartCapacity = 48;  // VehicleParts.parts
    inline constexpr std::uint32_t kPartVisible = 0x1;         // VehiclePart.flags: drawn (MSC shows it)

    // The Hayosiko's moving and switchable parts, in VehicleParts.parts order: paths below the van's
    // root in MSC's hierarchy (tools/nif/hayosiko.json lists the same paths, and the converted NIF
    // has one node per part named by its path). Changing the list bumps kProtocolVersion.
    inline constexpr const char* kHayosikoParts[] = {
        // Doors
        "DriverDoors/doorl",
        "DriverDoors/doorr",
        "SideDoor/door",
        "RearDoor/doorear",
        // Controls
        "LOD/Dashboard/Steering/VanSteeringPivot",
        "LOD/Dashboard/KeyHole/Keys/Key",
        "LOD/Dashboard/Gauges/NeedleSpeedometer/Needle",
        "LOD/Dashboard/Gauges/NeedleFuel/Needle",
        "LOD/Dashboard/Gauges/NeedleTemp/Needle",
        "LOD/Dashboard/Pedals 1/clutch",
        "LOD/Dashboard/Pedals 1/brake",
        "LOD/Dashboard/Pedals 1/throttle",
        "LOD/Dashboard/GearShifter/lever",
        "LOD/Dashboard/ParkingBrake/lever",
        "LOD/Dashboard/BlinkerLever/lever",
        "LOD/Dashboard/WipersLever/lever",
        "LOD/Wipers/Pivot/WiperLeftPivot",
        "LOD/Wipers/Pivot/WiperRightPivot",
        "LOD/FuelFiller/Pivot",
        "LOD/FuelFiller/OpenCap",
        "RadioPivot/Radio/TunerPivot/needle",
        "LOD/seatbelts/HandleUpPivot/SeatbeltHandle",
        "LOD/seatbelts/Fastened_mesh",
        // Lights (lit meshes, shown while on)
        "LOD/Electricity/PowerON/Driving/BeamsShort/drive",
        "LOD/Electricity/PowerON/Driving/BeamsLong/drive",
        "LOD/Electricity/PowerON/Brake/brake",
        "LOD/Electricity/PowerON/Reverse/reverse",
        "LOD/Electricity/PowerON/Blinkers/Left/indicator_left",
        "LOD/Electricity/PowerON/Blinkers/Right/indicator_right",
        "LOD/Dashboard/Gauges/GaugesLights",
        "LOD/Dashboard/Indicators/indicator_battery",
        "LOD/Dashboard/Indicators/indicator_brake",
        "LOD/Dashboard/Indicators/indicator_filter",
        "LOD/Dashboard/Indicators/indicator_glowplug",
        "LOD/Dashboard/Indicators/indicator_hazards",
        "LOD/Dashboard/Indicators/indicator_left",
        "LOD/Dashboard/Indicators/indicator_lights",
        "LOD/Dashboard/Indicators/indicator_oil",
        "LOD/Dashboard/Indicators/indicator_preheat",
        "LOD/Dashboard/Indicators/indicator_right",
    };
    inline constexpr std::uint32_t kHayosikoPartCount = 40;

    // A part's pose relative to the vehicle's body (VehicleState position and rotation): Skyrim axes
    // and units, vehicle +Y forward, +Z up.
    struct VehiclePart
    {
        float         rotation[4];     // x, y, z, w
        float         translation[3];  // the part's pivot
        std::uint32_t flags;           // kPart*
    };

    // MSC -> Skyrim, written right after VehicleState. parts[i] is part i of the vehicle's list
    // (kHayosikoParts); count is that list's length, the rest is zero.
    struct VehicleParts
    {
        std::uint32_t frame;       // MSC frame counter
        std::uint32_t vehicle;     // kVehicle* id, 0 = none
        std::uint32_t generation;  // WorldspaceChange.generation
        std::uint32_t count;
        VehiclePart   parts[48];   // kVehiclePartCapacity
    };

    struct VehiclePartsSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        VehicleParts  value;
        std::uint8_t  pad[40];
    };

    inline constexpr std::uint32_t kItemCapacity = 128;  // ItemState.items
    inline constexpr std::uint32_t kItemVisible = 0x1;   // ItemPose.flags: MSC draws it
    inline constexpr std::uint32_t kItemHeld = 0x2;      // ItemPose.flags: in the player's hands

    // One MSC physics item (or one piece of it: a bottle in a case is its own item). Skyrim world
    // space like VehicleState: the model's root sits at position, turned by rotation (Skyrim space).
    struct ItemPose
    {
        std::uint32_t id;           // MSC's, stable while the item exists, never 0
        std::uint32_t model;        // ModelChunk.model of its look, 0 = nothing to draw
        std::uint32_t flags;        // kItem*
        float         position[3];
        float         rotation[4];  // x, y, z, w
    };

    // MSC -> Skyrim. The items near MSC's player, closest first; count of them, the rest is zero.
    // An item missing from one frame to the next is gone (destroyed, or too far away).
    struct ItemState
    {
        std::uint32_t frame;       // MSC frame counter
        std::uint32_t generation;  // WorldspaceChange.generation
        std::uint32_t count;       // <= kItemCapacity
        std::uint32_t reserved;
        ItemPose      items[128];  // kItemCapacity
    };

    struct ItemSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        ItemState     value;
        std::uint8_t  pad[40];
    };

    inline constexpr std::uint32_t kHandLayerValid = 0x1;  // HandLayer.flags: MSC draws into the texture every frame

    // MSC -> Skyrim. MSC's first-person layer (what its HandCamera draws: hands, drinks, tools in
    // hand) in a D3D11 texture both games share on the GPU: transparent where nothing is drawn,
    // colors already multiplied by alpha, meant to go over Skyrim's world as is. MSC redraws it in
    // place every frame.
    struct HandLayer
    {
        std::uint32_t frame;       // MSC frame counter of the last copy into the texture
        std::uint32_t flags;       // kHandLayer*
        std::uint64_t texture;     // DXGI shared handle (IDXGIResource::GetSharedHandle), 0 = none
        std::uint32_t width;       // pixels
        std::uint32_t height;
        std::uint32_t format;      // DXGI_FORMAT of the texture
        std::uint32_t costMicros;  // MSC's time to render and copy the layer, last frame
    };

    struct HandLayerSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        HandLayer     value;
        std::uint8_t  pad[24];
    };

    inline constexpr std::uint32_t kActorCapacity = 32;  // ActorList.actors

    // One Skyrim actor near the player, as an upright capsule MSC's player bumps into and punches.
    // Skyrim world space like PlayerState.
    struct ActorPose
    {
        std::uint32_t id;           // the actor reference's form id, never 0
        float         position[3];  // feet
        float         heading;      // radians
        float         velocity[3];  // units per second
        float         radius;       // capsule radius, units
        float         height;       // capsule height from the feet, units
        float         mass;         // kilograms, a guess from its bounds (Damage::Mass); decides what a vehicle does to it
        std::uint32_t reserved;
    };

    // Skyrim -> MSC, while MSC has the player. The living actors near the player that stand (not
    // dead, knocked down or ragdolled), closest first; count of them, the rest is zero. An actor
    // missing from one list to the next has gone away (too far, dead, knocked down).
    struct ActorList
    {
        std::uint32_t frame;       // Skyrim frame counter
        std::uint32_t generation;  // WorldspaceChange.generation the positions are for
        std::uint32_t count;       // <= kActorCapacity
        std::uint32_t reserved;
        ActorPose     actors[32];  // kActorCapacity
    };

    struct ActorSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        ActorList     value;
        std::uint8_t  pad[40];
    };

    // ---- Rings (single producer, single consumer) ----
    //
    // head and tail are monotonic byte counts; position = count % capacity. The producer owns head,
    // the consumer owns tail. A record is a MessageHeader plus payload and may wrap around the end.
    // The consumer drops whatever is left in its ring when the link comes up, so stale messages from
    // an earlier session never arrive.

    struct RingHeader
    {
        std::uint32_t capacity;
        std::uint32_t reserved0;
        std::uint64_t head;
        std::uint8_t  reserved1[16];
        std::uint64_t tail;  // own 32-byte half, away from head
        std::uint8_t  reserved2[24];
    };

    enum class MessageType : std::uint16_t
    {
        CollisionChunk = 1,    // Skyrim -> MSC
        WorldspaceChange = 2,  // Skyrim -> MSC
        LookAtText = 3,        // MSC -> Skyrim
        Subtitle = 4,          // MSC -> Skyrim
        // 5 was GiveBeer (until v16): mead and ale now become PerkeleRim.esp beer in Skyrim's inventory.
        ModelChunk = 6,        // MSC -> Skyrim
        PlayerDamage = 7,      // MSC -> Skyrim
        PlayerDeath = 8,       // Skyrim -> MSC
        MscSave = 9,           // Skyrim -> MSC
        MscLoad = 10,          // Skyrim -> MSC
        PunchEvent = 11,       // MSC -> Skyrim
        Consumed = 12,         // MSC -> Skyrim
        PocketChange = 13,     // MSC -> Skyrim
        PocketCounts = 14,     // Skyrim -> MSC
        ImpactEvent = 15,      // MSC -> Skyrim
        // 16 was MiddleFinger (until v23): the finger no longer shouts, Z is Skyrim's own shout key.
        PlayerShout = 17,      // Skyrim -> MSC
        PlayerHurt = 18,       // Skyrim -> MSC
    };

    struct MessageHeader
    {
        std::uint16_t type;  // MessageType
        std::uint16_t reserved;
        std::uint32_t size;  // payload bytes after this header
    };

    // Payload: this header, then vertexCount * float[3] vertices (Skyrim units, relative to origin),
    // then indexCount uint32 triangle indices, then roadCount road triangles of float[6] (three x, y
    // corners, Skyrim units relative to origin, either winding). Triangles wind counterclockwise seen
    // from above in Skyrim space (cross(b - a, c - a) points up). Road triangles are the navmesh
    // triangles flagged preferred that reach into the chunk. A newer chunk with the same x/y and
    // generation replaces the older one.
    struct CollisionChunkHeader
    {
        std::int32_t  chunkX;
        std::int32_t  chunkY;
        std::uint32_t worldspace;  // form id
        std::uint32_t generation;  // WorldspaceChange.generation this chunk belongs to
        float         origin[3];
        std::uint32_t vertexCount;
        std::uint32_t indexCount;
        std::uint32_t roadCount;
    };

    inline constexpr std::uint32_t kAnchorVehicleSpot = 0x1;  // WorldspaceChange.flags: park the van at vehicleSpot

    struct WorldspaceChange
    {
        std::uint32_t worldspace;      // form id
        std::uint32_t cell;            // interior cell form id, 0 in an exterior
        std::uint32_t generation;      // bumped on every change; older chunks are dropped
        std::uint32_t flags;           // kAnchorVehicleSpot
        float         spawn[3];
        float         spawnHeading;
        float         vehicleSpot[3];  // ground under the van's middle, with kAnchorVehicleSpot
        float         vehicleHeading;  // the way the van faces there
    };

    // MSC's interaction prompt for what its crosshair is on: the text ("IGNITION", an item name) and
    // the hand icon it shows for things to use (doors, hatches). Sent when either changes and once
    // more each time MSC takes the player. Payload: this header, then length bytes of UTF-8 text (no
    // terminator). length 0 and no flags clears the prompt.
    inline constexpr std::uint32_t kLookAtTextMax = 255;
    inline constexpr std::uint32_t kLookAtUse = 0x1;  // LookAtText.flags: MSC shows its hand icon (global GUIuse)

    struct LookAtText
    {
        std::uint32_t length;  // <= kLookAtTextMax
        std::uint32_t flags;   // kLookAt*
    };

    // Subtitle: MSC's subtitle line (global GUIsubtitle: the player's swearing and other speech),
    // sent when it changes and once more each time MSC takes the player. Same payload as LookAtText
    // with flags 0; no text clears it.

    // PlayerDamage: something hit MSC's player that MSC would have died of (a crash, drowning), or a
    // crash it survives (a jump, a flip, a rock; severity below 1, scaled by the impact). MSC never
    // lets its player die while Skyrim has handed it over; Skyrim's health takes the hit instead,
    // severity 1 being one such death.
    inline constexpr std::uint32_t kDamageCrash = 1;  // PlayerDamage.cause: a vehicle crash (MSC's DEATH event, or a survivable one)
    inline constexpr std::uint32_t kDamageOther = 2;  // PlayerDamage.cause: MSC started its death screen some other way

    struct PlayerDamage
    {
        std::uint32_t cause;  // kDamage*
        float         severity;
    };

    // PlayerDeath: the player died in Skyrim while MSC had it (MSC plays its death sound). Skyrim
    // takes the player back right before sending it. Payload: this struct, all zero.
    struct PlayerDeath
    {
        std::uint32_t reserved[2];
    };

    // MscSave: Skyrim is saving. MSC saves its game the way its own save does (global event
    // SAVEGAME) and copies its save files into the folder named here, together with the key; the
    // Skyrim save keeps the key in its cosave. MscLoad: a Skyrim save with a key was loaded. When the
    // folder holds that key, MSC puts its save files back from it and reloads its game; otherwise MSC
    // stays as it is. Payload for both: this header, then length bytes of UTF-8 folder path (no
    // terminator).
    inline constexpr std::uint32_t kMscSnapshotPathMax = 1024;

    struct MscSnapshot
    {
        std::uint64_t key;     // never 0
        std::uint32_t length;  // path bytes, <= kMscSnapshotPathMax
        std::uint32_t reserved;
    };

    // PunchEvent: MSC's player punched (its Hit key) and the fist landed on an actor's capsule
    // from the latest ActorList. Skyrim hits that actor as the player.
    struct PunchEvent
    {
        std::uint32_t actor;         // ActorPose.id
        float         direction[3];  // the punch's way, unit vector in Skyrim space
    };

    // ImpactEvent: something of MSC's player other than its fist reached an actor's capsule from
    // the latest ActorList: an item it threw (kImpactThrown, with its speed at the hit), its piss
    // stream (kImpactPiss, sent again every so often while the stream stays on that actor) or the
    // vehicle it drives (kImpactVehicle, with the vehicle's speed). Skyrim hits that actor as the
    // player.
    inline constexpr std::uint32_t kImpactThrown = 1;  // ImpactEvent.kind
    inline constexpr std::uint32_t kImpactPiss = 2;
    inline constexpr std::uint32_t kImpactVehicle = 3;

    struct ImpactEvent
    {
        std::uint32_t actor;         // ActorPose.id
        std::uint32_t kind;          // kImpact*
        float         speed;         // meters per second (kImpactThrown, kImpactVehicle)
        float         direction[3];  // the way it moved, unit vector in Skyrim space
    };

    // PlayerShout: the player shouted in Skyrim while MSC had it (Z, Skyrim's own shout with its
    // cooldown; Skyrim's shout voice is muted meanwhile). MSC yells instead: "Perkele!" for
    // Unrelenting Force (kind kShoutUnrelentingForce), a swear for any other shout.
    inline constexpr std::uint32_t kShoutOther = 0;  // PlayerShout.kind
    inline constexpr std::uint32_t kShoutUnrelentingForce = 1;

    struct PlayerShout
    {
        std::uint32_t kind;   // kShout*
        std::uint32_t shout;  // the shout's form id, for the log
        std::uint32_t words;  // 1..3, how many words were shouted
        std::uint32_t reserved;
    };

    // PlayerHurt: something hurt the player in Skyrim while MSC had it (Skyrim's own grunts are
    // muted meanwhile). MSC grunts or swears instead (msc/src/Link/HurtLine.cs). Hits come as
    // Skyrim's hit events, falls and crashes as the damage Skyrim takes off for them, kHurtLow once
    // when health first drops low (protocol/cpp/hurt.h). A lethal one isn't sent: PlayerDeath is.
    inline constexpr std::uint32_t kHurtHit = 0;      // PlayerHurt.cause: someone hit the player
    inline constexpr std::uint32_t kHurtBlocked = 1;  // the same, but the player's guard took it
    inline constexpr std::uint32_t kHurtFall = 2;     // fall damage
    inline constexpr std::uint32_t kHurtCrash = 3;    // a crash MSC's player survived
    inline constexpr std::uint32_t kHurtLow = 4;      // health just dropped low

    struct PlayerHurt
    {
        std::uint32_t cause;   // kHurt*
        float         damage;  // share of max health it cost, 0..1 (0 when not known: hits)
        float         health;  // share of max health left, 0..1
        std::uint32_t reserved;
    };

    // Consumed: MSC's player finished an alcoholic drink, lit a cigarette or ate something. Skyrim
    // heals by kind (protocol/cpp/damage.h). Drinks without alcohol aren't sent.
    inline constexpr std::uint32_t kConsumedBeer = 1;       // Consumed.kind
    inline constexpr std::uint32_t kConsumedShot = 2;       // a glass of booze
    inline constexpr std::uint32_t kConsumedKilju = 3;
    inline constexpr std::uint32_t kConsumedBooze = 4;      // a bottle of booze (viina)
    inline constexpr std::uint32_t kConsumedSpirit = 5;     // denatured spirit
    inline constexpr std::uint32_t kConsumedCigarette = 6;  // lit, not finished
    inline constexpr std::uint32_t kConsumedFood = 7;

    struct Consumed
    {
        std::uint32_t kind;  // kConsumed*
        std::uint32_t reserved;
    };

    // The pocket: Skyrim's inventory holds MSC's beer, empty bottles, food and cigarettes as
    // PerkeleRim.esp items, one kind each (kPocket*). Skyrim's counts are the truth.
    inline constexpr std::uint32_t kPocketBeer = 0;        // PocketChange.kind, PocketCounts.counts index
    inline constexpr std::uint32_t kPocketBottle = 1;      // an empty beer bottle
    inline constexpr std::uint32_t kPocketSausages = 2;
    inline constexpr std::uint32_t kPocketPizza = 3;
    inline constexpr std::uint32_t kPocketChips = 4;       // potato chips
    inline constexpr std::uint32_t kPocketMacaron = 5;     // a macaron box
    inline constexpr std::uint32_t kPocketCigarettes = 6;  // MSC's PlayerCigarettes, one item per cigarette
    inline constexpr std::uint32_t kPocketKinds = 7;

    // PocketChange: MSC put delta of a kind into the pocket (> 0) or took it out (< 0). seq counts
    // up from 1 per session (MSC picks a new nonzero session each time its link comes up).
    struct PocketChange
    {
        std::uint32_t session;
        std::uint32_t seq;
        std::uint32_t kind;   // kPocket*
        std::int32_t  delta;
    };

    // PocketCounts: what Skyrim's inventory holds, sent when it changes, after each PocketChange and
    // when the link comes up. appliedSeq is the last PocketChange of session applied (0: none).
    struct PocketCounts
    {
        std::uint32_t session;
        std::uint32_t appliedSeq;
        std::uint32_t counts[7];  // by kPocket*
    };

    // ---- Models (MSC -> Skyrim) ----
    //
    // MSC sends its own meshes and textures, read from the running game, and Skyrim builds them in
    // memory. A model is one blob (layout below) cut into ModelChunk messages, so a model of any size
    // fits through the ring. Chunks of one model arrive in order, offset 0 first; a chunk that doesn't
    // continue the model being received drops it.

    inline constexpr std::uint32_t kModelChunkMax = 49152;     // ModelChunk.size limit
    inline constexpr std::uint32_t kModelSizeMax = 67108864;   // ModelChunk.totalSize limit (64 MiB)
    inline constexpr std::uint32_t kModelNameMax = 48;         // name bytes, UTF-8, zero padded
    inline constexpr std::uint32_t kModelTextureRgba8 = 1;     // ModelTexture.format
    inline constexpr std::uint32_t kModelTextureSizeMax = 2048;  // width and height limit
    inline constexpr std::uint32_t kModelMeshVertexMax = 65535;  // 16-bit indices

    // Payload: this header, then size bytes of the blob, starting at offset.
    struct ModelChunk
    {
        std::uint32_t model;      // MSC's id for the model (ItemPose.model), never 0; a resend replaces it
        std::uint32_t totalSize;  // blob bytes
        std::uint32_t offset;
        std::uint32_t size;       // <= kModelChunkMax
    };

    // The blob: this header, textureCount textures (ModelTexture, then width * height RGBA8 pixels,
    // top row first), then materialCount ModelMaterial, then meshCount meshes (ModelMesh, then
    // vertexCount ModelVertex, then indexCount uint16 triangle indices). Positions and normals are
    // relative to the model's root, in Skyrim axes and units. Front faces wind counterclockwise seen
    // from the front (cross(b - a, c - a) points out), like CollisionChunk. UV (0, 0) is the texture's
    // top left corner.
    struct ModelHeader
    {
        char          name[48];  // kModelNameMax
        std::uint32_t textureCount;
        std::uint32_t meshCount;
        std::uint32_t materialCount;
        std::uint32_t reserved;
    };

    struct ModelTexture
    {
        std::uint32_t width;   // <= kModelTextureSizeMax
        std::uint32_t height;  // <= kModelTextureSizeMax
        std::uint32_t format;  // kModelTexture*
        std::uint32_t reserved;
    };

    // How a mesh is drawn, from MSC's material. MSC bakes its color tint (and a metal's darker
    // diffuse) into the texture, so the texture is the final diffuse color and alpha.
    inline constexpr std::uint32_t kMaterialCutout = 0x1;   // ModelMaterial.flags: alpha below alphaCutoff isn't drawn
    inline constexpr std::uint32_t kMaterialBlend = 0x2;    // ModelMaterial.flags: alpha blended (glass, overlays)
    inline constexpr std::uint32_t kMaterialUnlit = 0x4;    // ModelMaterial.flags: shows its texture as is, no lighting
    inline constexpr std::uint32_t kMaterialOverlay = 0x8;  // ModelMaterial.flags: drawn over everything (no depth test)

    struct ModelMaterial
    {
        std::uint32_t flags;        // kMaterial*
        float         alphaCutoff;  // 0..1, with kMaterialCutout
        float         specular[3];  // highlight color, 0 = none
        float         glossiness;   // 0 (broad highlight) .. 1 (sharp)
        float         emissive[3];  // glow color added to the lighting
        std::uint32_t reserved;
    };

    struct ModelMesh
    {
        char          name[48];     // kModelNameMax: the MSC object's path below the model's root
        std::int32_t  texture;      // diffuse texture index, -1 = none
        std::uint32_t vertexCount;  // <= kModelMeshVertexMax
        std::uint32_t indexCount;   // 3 per triangle
        std::int32_t  material;     // material index, -1 = default (opaque, lit, no highlight)
    };

    struct ModelVertex
    {
        float position[3];
        float normal[3];
        float uv[2];
    };

#pragma pack(pop)

    static_assert(sizeof(PeerInfo) == 32);
    static_assert(offsetof(PeerInfo, heartbeat) == 8);
    static_assert(sizeof(LinkHeader) == 128);
    static_assert(offsetof(LinkHeader, creatorVersion) == 12);
    static_assert(offsetof(LinkHeader, peers) == 16);
    static_assert(sizeof(InputState) == 112);
    static_assert(offsetof(InputState, mouseX) == 8);
    static_assert(offsetof(InputState, mouseY) == 16);
    static_assert(offsetof(InputState, horizontal) == 24);
    static_assert(offsetof(InputState, vertical) == 28);
    static_assert(offsetof(InputState, keys) == 32);
    static_assert(offsetof(InputState, mouseWheel) == 96);
    static_assert(offsetof(InputState, screenWidth) == 104);
    static_assert(sizeof(InputSlot) == 128);
    static_assert(offsetof(InputSlot, value) == 8);
    static_assert(sizeof(PlayerState) == 96);
    static_assert(offsetof(PlayerState, position) == 8);
    static_assert(offsetof(PlayerState, velocity) == 20);
    static_assert(offsetof(PlayerState, heading) == 32);
    static_assert(offsetof(PlayerState, pitch) == 36);
    static_assert(offsetof(PlayerState, stance) == 40);
    static_assert(offsetof(PlayerState, generation) == 44);
    static_assert(offsetof(PlayerState, eye) == 48);
    static_assert(offsetof(PlayerState, roll) == 60);
    static_assert(offsetof(PlayerState, fov) == 64);
    static_assert(offsetof(PlayerState, needs) == 68);
    static_assert(sizeof(PlayerSlot) == 128);
    static_assert(offsetof(PlayerSlot, value) == 8);
    static_assert(sizeof(VehicleState) == 80);
    static_assert(offsetof(VehicleState, generation) == 12);
    static_assert(offsetof(VehicleState, position) == 16);
    static_assert(offsetof(VehicleState, rotation) == 28);
    static_assert(offsetof(VehicleState, velocity) == 44);
    static_assert(offsetof(VehicleState, steer) == 56);
    static_assert(offsetof(VehicleState, wheelSpin) == 60);
    static_assert(sizeof(VehicleSlot) == 128);
    static_assert(offsetof(VehicleSlot, value) == 8);
    static_assert(sizeof(VehiclePart) == 32);
    static_assert(offsetof(VehiclePart, translation) == 16);
    static_assert(offsetof(VehiclePart, flags) == 28);
    static_assert(sizeof(VehicleParts) == 1552);
    static_assert(offsetof(VehicleParts, count) == 12);
    static_assert(offsetof(VehicleParts, parts) == 16);
    static_assert(sizeof(VehiclePartsSlot) == 1600);
    static_assert(offsetof(VehiclePartsSlot, value) == 8);
    static_assert(sizeof(ItemPose) == 40);
    static_assert(offsetof(ItemPose, flags) == 8);
    static_assert(offsetof(ItemPose, position) == 12);
    static_assert(offsetof(ItemPose, rotation) == 24);
    static_assert(sizeof(ItemState) == 5136);
    static_assert(offsetof(ItemState, count) == 8);
    static_assert(offsetof(ItemState, items) == 16);
    static_assert(sizeof(ItemSlot) == 5184);
    static_assert(offsetof(ItemSlot, value) == 8);
    static_assert(sizeof(HandLayer) == 32);
    static_assert(offsetof(HandLayer, texture) == 8);
    static_assert(offsetof(HandLayer, width) == 16);
    static_assert(offsetof(HandLayer, format) == 24);
    static_assert(offsetof(HandLayer, costMicros) == 28);
    static_assert(sizeof(HandLayerSlot) == 64);
    static_assert(offsetof(HandLayerSlot, value) == 8);
    static_assert(sizeof(ActorPose) == 48);
    static_assert(offsetof(ActorPose, position) == 4);
    static_assert(offsetof(ActorPose, heading) == 16);
    static_assert(offsetof(ActorPose, velocity) == 20);
    static_assert(offsetof(ActorPose, radius) == 32);
    static_assert(offsetof(ActorPose, height) == 36);
    static_assert(offsetof(ActorPose, mass) == 40);
    static_assert(sizeof(ActorList) == 1552);
    static_assert(offsetof(ActorList, count) == 8);
    static_assert(offsetof(ActorList, actors) == 16);
    static_assert(sizeof(ActorSlot) == 1600);
    static_assert(offsetof(ActorSlot, value) == 8);
    static_assert(sizeof(RingHeader) == 64);
    static_assert(offsetof(RingHeader, head) == 8);
    static_assert(offsetof(RingHeader, tail) == 32);
    static_assert(sizeof(MessageHeader) == 8);
    static_assert(offsetof(MessageHeader, size) == 4);
    static_assert(sizeof(CollisionChunkHeader) == 40);
    static_assert(offsetof(CollisionChunkHeader, origin) == 16);
    static_assert(offsetof(CollisionChunkHeader, vertexCount) == 28);
    static_assert(offsetof(CollisionChunkHeader, indexCount) == 32);
    static_assert(offsetof(CollisionChunkHeader, roadCount) == 36);
    static_assert(sizeof(WorldspaceChange) == 48);
    static_assert(offsetof(WorldspaceChange, flags) == 12);
    static_assert(offsetof(WorldspaceChange, spawn) == 16);
    static_assert(offsetof(WorldspaceChange, spawnHeading) == 28);
    static_assert(offsetof(WorldspaceChange, vehicleSpot) == 32);
    static_assert(offsetof(WorldspaceChange, vehicleHeading) == 44);
    static_assert(sizeof(LookAtText) == 8);
    static_assert(sizeof(PlayerDamage) == 8);
    static_assert(offsetof(PlayerDamage, severity) == 4);
    static_assert(sizeof(PlayerDeath) == 8);
    static_assert(sizeof(MscSnapshot) == 16);
    static_assert(offsetof(MscSnapshot, length) == 8);
    static_assert(sizeof(PunchEvent) == 16);
    static_assert(offsetof(PunchEvent, direction) == 4);
    static_assert(sizeof(ImpactEvent) == 24);
    static_assert(offsetof(ImpactEvent, speed) == 8);
    static_assert(offsetof(ImpactEvent, direction) == 12);
    static_assert(sizeof(PlayerShout) == 16);
    static_assert(offsetof(PlayerShout, words) == 8);
    static_assert(sizeof(PlayerHurt) == 16);
    static_assert(offsetof(PlayerHurt, health) == 8);
    static_assert(sizeof(Consumed) == 8);
    static_assert(sizeof(PocketChange) == 16);
    static_assert(offsetof(PocketChange, delta) == 12);
    static_assert(sizeof(PocketCounts) == 36);
    static_assert(offsetof(PocketCounts, counts) == 8);
    static_assert(sizeof(ModelChunk) == 16);
    static_assert(offsetof(ModelChunk, offset) == 8);
    static_assert(sizeof(ModelHeader) == 64);
    static_assert(offsetof(ModelHeader, textureCount) == 48);
    static_assert(offsetof(ModelHeader, meshCount) == 52);
    static_assert(offsetof(ModelHeader, materialCount) == 56);
    static_assert(sizeof(ModelTexture) == 16);
    static_assert(offsetof(ModelTexture, format) == 8);
    static_assert(sizeof(ModelMaterial) == 40);
    static_assert(offsetof(ModelMaterial, alphaCutoff) == 4);
    static_assert(offsetof(ModelMaterial, specular) == 8);
    static_assert(offsetof(ModelMaterial, glossiness) == 20);
    static_assert(offsetof(ModelMaterial, emissive) == 24);
    static_assert(sizeof(ModelMesh) == 64);
    static_assert(offsetof(ModelMesh, texture) == 48);
    static_assert(offsetof(ModelMesh, vertexCount) == 52);
    static_assert(offsetof(ModelMesh, indexCount) == 56);
    static_assert(offsetof(ModelMesh, material) == 60);
    static_assert(sizeof(ModelVertex) == 32);
    static_assert(offsetof(ModelVertex, normal) == 12);
    static_assert(offsetof(ModelVertex, uv) == 24);
    static_assert(kModelChunkMax + sizeof(ModelChunk) + sizeof(MessageHeader) < kRingToSkyrimCapacity / 4);

    // Section layout follows from the struct sizes.
    static_assert(kOffsetInputSlot == kOffsetHeader + sizeof(LinkHeader));
    static_assert(kOffsetPlayerSlot == kOffsetInputSlot + sizeof(InputSlot));
    static_assert(kOffsetVehicleSlot == kOffsetPlayerSlot + sizeof(PlayerSlot));
    static_assert(kOffsetVehiclePartsSlot == kOffsetVehicleSlot + sizeof(VehicleSlot));
    static_assert(kOffsetItemSlot == kOffsetVehiclePartsSlot + sizeof(VehiclePartsSlot));
    static_assert(kOffsetHandLayerSlot == kOffsetItemSlot + sizeof(ItemSlot));
    static_assert(kOffsetActorSlot == kOffsetHandLayerSlot + sizeof(HandLayerSlot));
    static_assert(kOffsetRingToMsc == kOffsetActorSlot + sizeof(ActorSlot));
    static_assert(sizeof(ItemState::items) / sizeof(ItemPose) == kItemCapacity);
    static_assert(sizeof(ActorList::actors) / sizeof(ActorPose) == kActorCapacity);
    static_assert(kOffsetRingToSkyrim == kOffsetRingToMsc + sizeof(RingHeader) + kRingToMscCapacity);
    static_assert(kMappingSize == kOffsetRingToSkyrim + sizeof(RingHeader) + kRingToSkyrimCapacity);
    static_assert(sizeof(kHayosikoParts) / sizeof(kHayosikoParts[0]) == kHayosikoPartCount);
    static_assert(kHayosikoPartCount <= kVehiclePartCapacity);
    static_assert((kRingToMscCapacity & (kRingToMscCapacity - 1)) == 0);
    static_assert((kRingToSkyrimCapacity & (kRingToSkyrimCapacity - 1)) == 0);
}
