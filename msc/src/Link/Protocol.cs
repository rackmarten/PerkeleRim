using System.Runtime.InteropServices;

// C# mirror of protocol/perkelerim_protocol.h, which is the source of truth (and explains the
// fields). Field names match the header; msc/tests checks every size, offset and constant the
// header asserts. An inline array of structs in C++ (LinkHeader.peers) becomes numbered fields.
namespace PerkeleRim.Protocol
{
    public static class Wire
    {
        public const uint Magic = 0x4D495250;
        public const uint ProtocolVersion = 17;
        public const string MappingName = @"Local\PerkeleRim.Link";
        public const uint PeerTimeoutMs = 2000;
        public const uint CollisionChunkUnits = 2048;

        public const uint OffsetHeader = 0;
        public const uint OffsetInputSlot = 128;
        public const uint OffsetPlayerSlot = 256;
        public const uint OffsetVehicleSlot = 384;
        public const uint OffsetVehiclePartsSlot = 512;
        public const uint OffsetItemSlot = 2112;
        public const uint OffsetHandLayerSlot = 7296;
        public const uint OffsetActorSlot = 7360;
        public const uint OffsetRingToMsc = 8960;
        public const uint RingToMscCapacity = 4194304;
        public const uint OffsetRingToSkyrim = 4203328;
        public const uint RingToSkyrimCapacity = 262144;
        public const uint MappingSize = 4465536;

        public const uint InputOwnsPlayer = 0x1;
        public const uint PlayerValid = 0x1;
        public const uint PlayerCamera = 0x2;
        public const uint PlayerNeeds = 0x4;
        public const uint PlayerGrounded = 0x8;
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
        public const uint VehicleHayosiko = 1;
        public const uint AnchorVehicleSpot = 0x1;
        public const uint VehiclePartCapacity = 48;
        public const uint PartVisible = 0x1;
        public const uint HayosikoPartCount = 40;
        public const uint ItemCapacity = 128;
        public const uint ItemVisible = 0x1;
        public const uint ItemHeld = 0x2;
        public const uint HandLayerValid = 0x1;
        public const uint ActorCapacity = 32;
        public const uint LookAtTextMax = 255;
        public const uint LookAtUse = 0x1;
        public const uint ModelChunkMax = 49152;
        public const uint ModelSizeMax = 67108864;
        public const uint ModelNameMax = 48;
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
        public const uint PocketBeer = 0;
        public const uint PocketBottle = 1;
        public const uint PocketSausages = 2;
        public const uint PocketPizza = 3;
        public const uint PocketChips = 4;
        public const uint PocketMacaron = 5;
        public const uint PocketCigarettes = 6;
        public const uint PocketKinds = 7;
        public const uint MscSnapshotPathMax = 1024;
        public const uint MaterialCutout = 0x1;
        public const uint MaterialBlend = 0x2;
        public const uint MaterialUnlit = 0x4;
        public const uint MaterialOverlay = 0x8;

        // VehicleParts.parts order for the van; msc/tests checks it against the header's list.
        public static readonly string[] HayosikoParts =
        {
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
    }

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
        public fixed byte pad[8];
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
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PlayerSlot
    {
        public uint seq;
        public uint reserved;
        public PlayerState value;
        public fixed byte pad[24];
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
        public uint reserved;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct VehicleSlot
    {
        public uint seq;
        public uint reserved;
        public VehicleState value;
        public fixed byte pad[40];
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
        public fixed byte parts[1536];

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
        public fixed byte pad[40];
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
        public fixed uint reserved[2];
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
        public uint reserved;
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
        public fixed uint counts[7];
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
        public uint reserved;
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
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct ModelVertex
    {
        public fixed float position[3];
        public fixed float normal[3];
        public fixed float uv[2];
    }
}
