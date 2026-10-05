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
    inline constexpr std::uint32_t kProtocolVersion = 1;

    // Shared memory section, per Windows session. Whichever game starts first creates it.
    inline constexpr const wchar_t* kMappingName = L"Local\\PerkeleRim.Link";

    // A peer counts as gone when its heartbeat hasn't moved for this long.
    inline constexpr std::uint32_t kPeerTimeoutMs = 2000;

    // Section layout (byte offsets from the start of the mapping).
    inline constexpr std::uint32_t kOffsetHeader = 0;
    inline constexpr std::uint32_t kOffsetInputSlot = 128;
    inline constexpr std::uint32_t kOffsetPlayerSlot = 256;
    inline constexpr std::uint32_t kOffsetRingToMsc = 320;          // RingHeader, data follows
    inline constexpr std::uint32_t kRingToMscCapacity = 4194304;    // 4 MiB: collision chunks
    inline constexpr std::uint32_t kOffsetRingToSkyrim = 4194688;   // 320 + 64 + 4 MiB
    inline constexpr std::uint32_t kRingToSkyrimCapacity = 262144;  // 256 KiB: events
    inline constexpr std::uint32_t kMappingSize = 4456896;         // 4194688 + 64 + 256 KiB

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
    };

    struct InputSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        InputState    value;
        std::uint8_t  pad[24];
    };

    inline constexpr std::uint32_t kPlayerValid = 0x1;  // PlayerState.flags: MSC is simulating the player

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
        std::uint8_t  reserved[7];
    };

    struct PlayerSlot
    {
        std::uint32_t seq;
        std::uint32_t reserved;
        PlayerState   value;
        std::uint8_t  pad[8];
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
    };

    struct MessageHeader
    {
        std::uint16_t type;  // MessageType
        std::uint16_t reserved;
        std::uint32_t size;  // payload bytes after this header
    };

    // Payload: this header, then vertexCount * float[3] vertices (Skyrim units, relative to origin),
    // then indexCount uint32 triangle indices.
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

    struct WorldspaceChange
    {
        std::uint32_t worldspace;  // form id
        std::uint32_t cell;        // interior cell form id, 0 in an exterior
        std::uint32_t generation;  // bumped on every change; older chunks are dropped
        std::uint32_t reserved;
        float         spawn[3];
        float         spawnHeading;
    };

#pragma pack(pop)

    static_assert(sizeof(PeerInfo) == 32);
    static_assert(offsetof(PeerInfo, heartbeat) == 8);
    static_assert(sizeof(LinkHeader) == 128);
    static_assert(offsetof(LinkHeader, creatorVersion) == 12);
    static_assert(offsetof(LinkHeader, peers) == 16);
    static_assert(sizeof(InputState) == 96);
    static_assert(offsetof(InputState, mouseX) == 8);
    static_assert(offsetof(InputState, mouseY) == 16);
    static_assert(offsetof(InputState, horizontal) == 24);
    static_assert(offsetof(InputState, vertical) == 28);
    static_assert(offsetof(InputState, keys) == 32);
    static_assert(sizeof(InputSlot) == 128);
    static_assert(offsetof(InputSlot, value) == 8);
    static_assert(sizeof(PlayerState) == 48);
    static_assert(offsetof(PlayerState, position) == 8);
    static_assert(offsetof(PlayerState, velocity) == 20);
    static_assert(offsetof(PlayerState, heading) == 32);
    static_assert(offsetof(PlayerState, pitch) == 36);
    static_assert(offsetof(PlayerState, stance) == 40);
    static_assert(sizeof(PlayerSlot) == 64);
    static_assert(offsetof(PlayerSlot, value) == 8);
    static_assert(sizeof(RingHeader) == 64);
    static_assert(offsetof(RingHeader, head) == 8);
    static_assert(offsetof(RingHeader, tail) == 32);
    static_assert(sizeof(MessageHeader) == 8);
    static_assert(offsetof(MessageHeader, size) == 4);
    static_assert(sizeof(CollisionChunkHeader) == 40);
    static_assert(offsetof(CollisionChunkHeader, origin) == 16);
    static_assert(offsetof(CollisionChunkHeader, vertexCount) == 28);
    static_assert(offsetof(CollisionChunkHeader, indexCount) == 32);
    static_assert(sizeof(WorldspaceChange) == 32);
    static_assert(offsetof(WorldspaceChange, spawn) == 16);
    static_assert(offsetof(WorldspaceChange, spawnHeading) == 28);

    // Section layout follows from the struct sizes.
    static_assert(kOffsetInputSlot == kOffsetHeader + sizeof(LinkHeader));
    static_assert(kOffsetPlayerSlot == kOffsetInputSlot + sizeof(InputSlot));
    static_assert(kOffsetRingToMsc == kOffsetPlayerSlot + sizeof(PlayerSlot));
    static_assert(kOffsetRingToSkyrim == kOffsetRingToMsc + sizeof(RingHeader) + kRingToMscCapacity);
    static_assert(kMappingSize == kOffsetRingToSkyrim + sizeof(RingHeader) + kRingToSkyrimCapacity);
    static_assert((kRingToMscCapacity & (kRingToMscCapacity - 1)) == 0);
    static_assert((kRingToSkyrimCapacity & (kRingToSkyrimCapacity - 1)) == 0);
}
