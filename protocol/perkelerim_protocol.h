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
    inline constexpr std::uint32_t kProtocolVersion = 11;

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
    inline constexpr std::uint32_t kOffsetRingToMsc = 7296;         // RingHeader, data follows
    inline constexpr std::uint32_t kRingToMscCapacity = 4194304;    // 4 MiB: collision chunks
    inline constexpr std::uint32_t kOffsetRingToSkyrim = 4201664;   // 7296 + 64 + 4 MiB
    inline constexpr std::uint32_t kRingToSkyrimCapacity = 262144;  // 256 KiB: events
    inline constexpr std::uint32_t kMappingSize = 4463872;         // 4201664 + 64 + 256 KiB

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
    };

    struct InputSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        InputState    value;
        std::uint8_t  pad[16];
    };

    inline constexpr std::uint32_t kPlayerValid = 0x1;   // PlayerState.flags: MSC is simulating the player
    inline constexpr std::uint32_t kPlayerCamera = 0x2;  // PlayerState.flags: eye, roll and fov are set
    inline constexpr std::uint32_t kPlayerNeeds = 0x4;   // PlayerState.flags: needs are set

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
        GiveBeer = 5,          // Skyrim -> MSC
        ModelChunk = 6,        // MSC -> Skyrim
    };

    struct MessageHeader
    {
        std::uint16_t type;  // MessageType
        std::uint16_t reserved;
        std::uint32_t size;  // payload bytes after this header
    };

    // Payload: this header, then vertexCount * float[3] vertices (Skyrim units, relative to origin),
    // then indexCount uint32 triangle indices. Triangles wind counterclockwise seen from above in
    // Skyrim space (cross(b - a, c - a) points up). A newer chunk with the same x/y and generation
    // replaces the older one.
    struct CollisionChunkHeader
    {
        std::int32_t  chunkX;
        std::int32_t  chunkY;
        std::uint32_t worldspace;  // form id
        std::uint32_t generation;  // WorldspaceChange.generation this chunk belongs to
        float         origin[3];
        std::uint32_t vertexCount;
        std::uint32_t indexCount;
        std::uint32_t reserved;
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

    // GiveBeer: the player picked up mead or ale in Skyrim, which is gone from Skyrim's inventory
    // once this is sent. MSC adds that many bottles of beer to a case near its player (a new case
    // when none has room).
    struct GiveBeer
    {
        std::uint32_t count;
        std::uint32_t reserved;
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
    // top row first), then meshCount meshes (ModelMesh, then vertexCount ModelVertex, then indexCount
    // uint16 triangle indices). Positions and normals are relative to the model's root, in Skyrim axes
    // and units. Front faces wind counterclockwise seen from the front (cross(b - a, c - a) points out),
    // like CollisionChunk. UV (0, 0) is the texture's top left corner.
    struct ModelHeader
    {
        char          name[48];  // kModelNameMax
        std::uint32_t textureCount;
        std::uint32_t meshCount;
        std::uint32_t reserved[2];
    };

    struct ModelTexture
    {
        std::uint32_t width;   // <= kModelTextureSizeMax
        std::uint32_t height;  // <= kModelTextureSizeMax
        std::uint32_t format;  // kModelTexture*
        std::uint32_t reserved;
    };

    struct ModelMesh
    {
        char          name[48];     // kModelNameMax: the MSC object's path below the model's root
        std::int32_t  texture;      // diffuse texture index, -1 = none
        std::uint32_t vertexCount;  // <= kModelMeshVertexMax
        std::uint32_t indexCount;   // 3 per triangle
        std::uint32_t reserved;
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
    static_assert(sizeof(InputState) == 104);
    static_assert(offsetof(InputState, mouseX) == 8);
    static_assert(offsetof(InputState, mouseY) == 16);
    static_assert(offsetof(InputState, horizontal) == 24);
    static_assert(offsetof(InputState, vertical) == 28);
    static_assert(offsetof(InputState, keys) == 32);
    static_assert(offsetof(InputState, mouseWheel) == 96);
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
    static_assert(sizeof(RingHeader) == 64);
    static_assert(offsetof(RingHeader, head) == 8);
    static_assert(offsetof(RingHeader, tail) == 32);
    static_assert(sizeof(MessageHeader) == 8);
    static_assert(offsetof(MessageHeader, size) == 4);
    static_assert(sizeof(CollisionChunkHeader) == 40);
    static_assert(offsetof(CollisionChunkHeader, origin) == 16);
    static_assert(offsetof(CollisionChunkHeader, vertexCount) == 28);
    static_assert(offsetof(CollisionChunkHeader, indexCount) == 32);
    static_assert(sizeof(WorldspaceChange) == 48);
    static_assert(offsetof(WorldspaceChange, flags) == 12);
    static_assert(offsetof(WorldspaceChange, spawn) == 16);
    static_assert(offsetof(WorldspaceChange, spawnHeading) == 28);
    static_assert(offsetof(WorldspaceChange, vehicleSpot) == 32);
    static_assert(offsetof(WorldspaceChange, vehicleHeading) == 44);
    static_assert(sizeof(LookAtText) == 8);
    static_assert(sizeof(GiveBeer) == 8);
    static_assert(sizeof(ModelChunk) == 16);
    static_assert(offsetof(ModelChunk, offset) == 8);
    static_assert(sizeof(ModelHeader) == 64);
    static_assert(offsetof(ModelHeader, textureCount) == 48);
    static_assert(offsetof(ModelHeader, meshCount) == 52);
    static_assert(sizeof(ModelTexture) == 16);
    static_assert(offsetof(ModelTexture, format) == 8);
    static_assert(sizeof(ModelMesh) == 64);
    static_assert(offsetof(ModelMesh, texture) == 48);
    static_assert(offsetof(ModelMesh, vertexCount) == 52);
    static_assert(offsetof(ModelMesh, indexCount) == 56);
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
    static_assert(kOffsetRingToMsc == kOffsetItemSlot + sizeof(ItemSlot));
    static_assert(sizeof(ItemState::items) / sizeof(ItemPose) == kItemCapacity);
    static_assert(kOffsetRingToSkyrim == kOffsetRingToMsc + sizeof(RingHeader) + kRingToMscCapacity);
    static_assert(kMappingSize == kOffsetRingToSkyrim + sizeof(RingHeader) + kRingToSkyrimCapacity);
    static_assert(sizeof(kHayosikoParts) / sizeof(kHayosikoParts[0]) == kHayosikoPartCount);
    static_assert(kHayosikoPartCount <= kVehiclePartCapacity);
    static_assert((kRingToMscCapacity & (kRingToMscCapacity - 1)) == 0);
    static_assert((kRingToSkyrimCapacity & (kRingToSkyrimCapacity - 1)) == 0);
}
