using System.Runtime.InteropServices;

// C# mirror of protocol/perkelerim_protocol.h, which is the source of truth (and explains the
// fields). Field names match the header; msc/tests checks every size, offset and constant the
// header asserts. An inline array of structs in C++ (LinkHeader.peers) becomes numbered fields.
namespace PerkeleRim.Protocol
{
    public static class Wire
    {
        public const uint Magic = 0x4D495250;
        public const uint ProtocolVersion = 44;
        public const string MappingName = @"Local\PerkeleRim.Link";
        public const uint PeerTimeoutMs = 2000;
        public const uint CollisionChunkUnits = 2048;

        public const uint OffsetHeader = 0;
        public const uint OffsetInputSlot = 128;
        public const uint OffsetPlayerSlot = 256;
        public const uint OffsetVehicleSlot = 384;
        public const uint OffsetVehiclePartsSlot = 768;
        public const uint OffsetItemSlot = 25536;
        public const uint OffsetHandLayerSlot = 30720;
        public const uint OffsetActorSlot = 30784;
        public const uint OffsetParticleSlot = 32384;
        public const uint OffsetRingToMsc = 48832;
        public const uint RingToMscCapacity = 4194304;
        public const uint OffsetRingToSkyrim = 4243200;
        public const uint RingToSkyrimCapacity = 262144;
        public const uint MappingSize = 4505408;

        public const uint InputOwnsPlayer = 0x1;
        public const uint InputPaused = 0x2;
        public const uint InputMscMenu = 0x4;
        public const uint PlayerValid = 0x1;
        public const uint PlayerCamera = 0x2;
        public const uint PlayerNeeds = 0x4;
        public const uint PlayerGrounded = 0x8;
        public const uint PlayerBlocking = 0x10;
        public const uint PlayerChaseCamera = 0x20;
        public const uint PlayerInGame = 0x40;
        public const uint PlayerTools = 0x80;
        public const uint PlayerMscMenu = 0x100;
        public const uint ConsumeDrink = 0x1;
        public const uint NeedThirst = 0;
        public const uint NeedHunger = 1;
        public const uint NeedStress = 2;
        public const uint NeedUrine = 3;
        public const uint NeedFatigue = 4;
        public const uint NeedDirtiness = 5;
        public const uint NeedDrunk = 6;
        public const uint NeedCount = 7;
        public const uint VehicleValid = 0x1;
        public const uint VehicleDriven = 0x2;
        public const uint VehicleSeat = 0x4;
        public const uint VehicleHayosiko = 1;
        public const uint VehicleJonnez = 2;
        public const uint VehicleKekmet = 3;
        public const uint VehicleCount = 3;
        public const uint AnchorVehicleSpot = 0x1;
        public const uint VehiclePartCapacity = 256;
        public const uint PartVisible = 0x1;
        public const uint ItemCapacity = 128;
        public const uint ItemVisible = 0x1;
        public const uint ItemHeld = 0x2;
        public const uint HandLayerValid = 0x1;
        public const uint HandLayerMenu = 0x2;
        public const uint ActorCapacity = 32;
        public const uint ParticleCapacity = 512;
        public const uint LookAtTextMax = 255;
        public const uint LookAtUse = 0x1;
        public const uint ModelChunkMax = 49152;
        public const uint ModelSizeMax = 67108864;
        public const uint ModelNameMax = 48;
        public const uint ModelNodeNameMax = 64;
        public const uint ModelLightMax = 32;
        public const uint LightLow = 1;
        public const uint LightHigh = 2;
        public const uint LightTail = 3;
        public const uint LightBrake = 4;
        public const uint LightReverse = 5;
        public const uint LightInterior = 6;
        public const uint ModelTextureRgba8 = 1;
        public const uint ModelTextureSizeMax = 2048;
        public const uint ModelMeshVertexMax = 65535;
        public const uint DamageCrash = 1;
        public const uint DamageOther = 2;
        public const uint ConsumedBeer = 1;
        public const uint ConsumedShot = 2;
        public const uint ConsumedKilju = 3;
        public const uint ConsumedBooze = 4;
        public const uint ConsumedSpirit = 5;
        public const uint ConsumedCigarette = 6;
        public const uint ConsumedFood = 7;
        public const uint ImpactThrown = 1;
        public const uint ImpactPiss = 2;
        public const uint ImpactVehicle = 3;
        public const uint ImpactPush = 4;
        public const uint IntroBlackout = 1;
        public const uint IntroWake = 2;
        public const uint IntroHangover = 3;
        public const uint ShoutOther = 0;
        public const uint ShoutUnrelentingForce = 1;
        public const uint HurtHit = 0;
        public const uint HurtBlocked = 1;
        public const uint HurtFall = 2;
        public const uint HurtCrash = 3;
        public const uint HurtLow = 4;
        public const uint PocketBeer = 0;
        public const uint PocketBottle = 1;
        public const uint PocketSausages = 2;
        public const uint PocketPizza = 3;
        public const uint PocketChips = 4;
        public const uint PocketMacaron = 5;
        public const uint PocketCigarettes = 6;
        public const uint PocketGold = 7;
        public const uint PocketKinds = 8;
        public const uint MscSnapshotPathMax = 1024;
        public const uint ScreenshotPathMax = 1024;
        public const uint TimeBed = 0x1;
        public const uint StockSpotsMax = 16;
        public const uint MaterialCutout = 0x1;
        public const uint MaterialBlend = 0x2;
        public const uint MaterialUnlit = 0x4;
        public const uint MaterialOverlay = 0x8;    }

    public enum Side : uint
    {
        Skyrim = 0,
        Msc = 1,
    }

    public enum MessageType : ushort
    {
        CollisionChunk = 1,
        WorldspaceChange = 2,
        LookAtText = 3,
        Subtitle = 4,
        ModelChunk = 6,
        PlayerDamage = 7,
        PlayerDeath = 8,
        MscSave = 9,
        MscLoad = 10,
        PunchEvent = 11,
        Consumed = 12,
        PocketChange = 13,
        PocketCounts = 14,
        ImpactEvent = 15,
        MiddleFinger = 16,
        PlayerShout = 17,
        PlayerHurt = 18,
        StockShelves = 19,
        HornEvent = 20,
        ScreenshotEvent = 21,
        ThrownPath = 22,
        LoadReset = 23,
        TimePassed = 24,
        GearText = 25,
        PlayerWashed = 26,
        DebugCommand = 27,
        IntroCue = 28,
        ConsumeEvent = 29,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PeerInfo
    {
        public uint version;
        public uint pid;
        public ulong heartbeat;
        public ulong reserved0;
        public ulong reserved1;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct LinkHeader
    {
        public uint magic;
        public uint headerSize;
        public uint mappingSize;
        public uint creatorVersion;
        public PeerInfo peers0;  // Side.Skyrim
        public PeerInfo peers1;  // Side.Msc
        public fixed byte reserved[48];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct InputState
    {
        public uint frame;
        public uint flags;
        public double mouseX;
        public double mouseY;
        public float horizontal;
        public float vertical;
        public fixed ulong keys[8];
        public double mouseWheel;
        public uint screenWidth;
        public uint screenHeight;
        public float cursorX;
        public float cursorY;

        public bool IsKeyDown(int keyCode)
        {
            if (keyCode < 0 || keyCode >= 512) return false;
            fixed (ulong* k = keys) return (k[keyCode >> 6] & (1UL << (keyCode & 63))) != 0;
        }

        public void SetKey(int keyCode, bool down)
        {
            if (keyCode < 0 || keyCode >= 512) return;
            fixed (ulong* k = keys)
            {
                if (down) k[keyCode >> 6] |= 1UL << (keyCode & 63);
                else k[keyCode >> 6] &= ~(1UL << (keyCode & 63));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct InputSlot
    {
        public uint seq;
        public uint reserved;
        public InputState value;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PlayerState
    {
        public uint frame;
        public uint flags;
        public fixed float position[3];
        public fixed float velocity[3];
        public float heading;
        public float pitch;
        public byte stance;
        public fixed byte reserved[3];
        public uint generation;
        public fixed float eye[3];
        public float roll;
        public float fov;
        public fixed float needs[7];
        public float viewHeading;
        public float viewPitch;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PlayerSlot
    {
        public uint seq;
        public uint reserved;
        public PlayerState value;
        public fixed byte pad[16];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VehicleState
    {
        public uint frame;
        public uint flags;
        public uint vehicle;
        public uint generation;
        public fixed float position[3];
        public fixed float rotation[4];
        public fixed float velocity[3];
        public float steer;
        public fixed float wheelSpin[4];
        public fixed float seat[3];
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VehicleSlot
    {
        public uint seq;
        public uint reserved;
        public VehicleState value;
        public fixed byte pad[28];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VehiclePart
    {
        public fixed float rotation[4];
        public fixed float translation[3];
        public uint flags;
    }

    // parts holds kVehiclePartCapacity VehicleParts (C# can't make a fixed buffer of structs).
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VehicleParts
    {
        public uint frame;
        public uint vehicle;
        public uint generation;
        public uint count;
        public uint model;
        public uint lights;
        public float lowLevel;
        public float highLevel;
        public fixed byte parts[8192];

        public VehiclePart Get(int i)
        {
            if ((uint)i >= Wire.VehiclePartCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = parts) return ((VehiclePart*)p)[i];
        }

        public void Set(int i, VehiclePart part)
        {
            if ((uint)i >= Wire.VehiclePartCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = parts) ((VehiclePart*)p)[i] = part;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VehiclePartsSlot
    {
        public uint seq;
        public uint reserved;
        public VehicleParts value;
        public fixed byte pad[24];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ItemPose
    {
        public uint id;
        public uint model;
        public uint flags;
        public fixed float position[3];
        public fixed float rotation[4];
    }

    // items holds kItemCapacity ItemPoses.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ItemState
    {
        public uint frame;
        public uint generation;
        public uint count;
        public uint reserved;
        public fixed byte items[5120];

        public ItemPose Get(int i)
        {
            if ((uint)i >= Wire.ItemCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = items) return ((ItemPose*)p)[i];
        }

        public void Set(int i, ItemPose item)
        {
            if ((uint)i >= Wire.ItemCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = items) ((ItemPose*)p)[i] = item;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ItemSlot
    {
        public uint seq;
        public uint reserved;
        public ItemState value;
        public fixed byte pad[40];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct HandLayer
    {
        public uint frame;
        public uint flags;
        public ulong texture;
        public uint width;
        public uint height;
        public uint format;
        public uint costMicros;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct HandLayerSlot
    {
        public uint seq;
        public uint reserved;
        public HandLayer value;
        public fixed byte pad[24];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ActorPose
    {
        public uint id;
        public fixed float position[3];
        public float heading;
        public fixed float velocity[3];
        public float radius;
        public float height;
        public float mass;
        public uint reserved;
    }

    // actors holds kActorCapacity ActorPoses.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ActorList
    {
        public uint frame;
        public uint generation;
        public uint count;
        public uint reserved;
        public fixed byte actors[1536];

        public ActorPose Get(int i)
        {
            if ((uint)i >= Wire.ActorCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = actors) return ((ActorPose*)p)[i];
        }

        public void Set(int i, ActorPose actor)
        {
            if ((uint)i >= Wire.ActorCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = actors) ((ActorPose*)p)[i] = actor;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ActorSlot
    {
        public uint seq;
        public uint reserved;
        public ActorList value;
        public fixed byte pad[40];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ParticlePose
    {
        public uint model;
        public fixed float position[3];
        public float size;
        public float rotation;
        public fixed byte color[4];
        public fixed byte sheet[4];
    }

    // particles holds kParticleCapacity ParticlePoses.
    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ParticleState
    {
        public uint frame;
        public uint generation;
        public uint count;
        public uint reserved;
        public fixed byte particles[16384];

        public ParticlePose Get(int i)
        {
            if ((uint)i >= Wire.ParticleCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = particles) return ((ParticlePose*)p)[i];
        }

        public void Set(int i, ParticlePose particle)
        {
            if ((uint)i >= Wire.ParticleCapacity) throw new System.ArgumentOutOfRangeException(nameof(i));
            fixed (byte* p = particles) ((ParticlePose*)p)[i] = particle;
        }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ParticleSlot
    {
        public uint seq;
        public uint reserved;
        public ParticleState value;
        public fixed byte pad[40];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct RingHeader
    {
        public uint capacity;
        public uint reserved0;
        public ulong head;
        public fixed byte reserved1[16];
        public ulong tail;
        public fixed byte reserved2[24];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MessageHeader
    {
        public ushort type;
        public ushort reserved;
        public uint size;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct CollisionChunkHeader
    {
        public int chunkX;
        public int chunkY;
        public uint worldspace;
        public uint generation;
        public fixed float origin[3];
        public uint vertexCount;
        public uint indexCount;
        public uint roadCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct WorldspaceChange
    {
        public uint worldspace;
        public uint cell;
        public uint generation;
        public uint flags;
        public fixed float spawn[3];
        public float spawnHeading;
        public fixed float vehicleSpot[3];
        public float vehicleHeading;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct LookAtText
    {
        public uint length;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PlayerDamage
    {
        public uint cause;
        public float severity;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PlayerDeath
    {
        public fixed uint reserved[2];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct MscSnapshot
    {
        public ulong key;
        public uint length;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PunchEvent
    {
        public uint actor;
        public fixed float direction[3];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ImpactEvent
    {
        public uint actor;
        public uint kind;
        public float speed;
        public fixed float direction[3];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ThrownPath
    {
        public uint flight;
        public float speed;
        public fixed float from[3];
        public fixed float to[3];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct HornEvent
    {
        public fixed float direction[3];
        public float speed;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ScreenshotEvent
    {
        public uint length;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct TimePassed
    {
        public float hours;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct IntroCue
    {
        public uint cue;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PlayerShout
    {
        public uint kind;
        public uint shout;
        public uint words;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct MiddleFinger
    {
        public fixed float direction[3];
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PlayerHurt
    {
        public uint cause;
        public float damage;
        public float health;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct StockSpot
    {
        public fixed float position[3];
        public float heading;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct StockShelves
    {
        public uint generation;
        public uint count;
        public fixed float spots[64];  // StockSpotsMax StockSpot: position x, y, z, heading each
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct Consumed
    {
        public uint kind;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct PocketChange
    {
        public uint session;
        public uint seq;
        public uint kind;
        public int delta;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PocketCounts
    {
        public uint session;
        public uint appliedSeq;
        public fixed uint counts[8];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ConsumeEvent
    {
        public float hunger;
        public float thirst;
        public float urine;
        public float drunk;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ModelChunk
    {
        public uint model;
        public uint totalSize;
        public uint offset;
        public uint size;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ModelHeader
    {
        public fixed byte name[48];
        public uint textureCount;
        public uint meshCount;
        public uint materialCount;
        public uint nodeCount;
        public uint lightCount;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct ModelTexture
    {
        public uint width;
        public uint height;
        public uint format;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ModelMaterial
    {
        public uint flags;
        public float alphaCutoff;
        public fixed float specular[3];
        public float glossiness;
        public fixed float emissive[3];
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ModelMesh
    {
        public fixed byte name[48];
        public int texture;
        public uint vertexCount;
        public uint indexCount;
        public int material;
        public int node;
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ModelLight
    {
        public uint role;
        public fixed float color[3];
        public fixed float position[3];
        public fixed float direction[3];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ModelNode
    {
        public fixed byte name[64];
        public int parent;
        public fixed float translation[3];
        public fixed float rotation[4];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ModelVertex
    {
        public fixed float position[3];
        public fixed float normal[3];
        public fixed float uv[2];
    }
}
