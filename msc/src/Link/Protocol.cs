using System.Runtime.InteropServices;

// C# mirror of protocol/perkelerim_protocol.h, which is the source of truth (and explains the
// fields). Field names match the header; msc/tests checks every size, offset and constant the
// header asserts. An inline array of structs in C++ (LinkHeader.peers) becomes numbered fields.
namespace PerkeleRim.Protocol
{
    public static class Wire
    {
        public const uint Magic = 0x4D495250;
        public const uint ProtocolVersion = 3;
        public const string MappingName = @"Local\PerkeleRim.Link";
        public const uint PeerTimeoutMs = 2000;
        public const uint CollisionChunkUnits = 2048;

        public const uint OffsetHeader = 0;
        public const uint OffsetInputSlot = 128;
        public const uint OffsetPlayerSlot = 256;
        public const uint OffsetVehicleSlot = 320;
        public const uint OffsetRingToMsc = 448;
        public const uint RingToMscCapacity = 4194304;
        public const uint OffsetRingToSkyrim = 4194816;
        public const uint RingToSkyrimCapacity = 262144;
        public const uint MappingSize = 4457024;

        public const uint InputOwnsPlayer = 0x1;
        public const uint PlayerValid = 0x1;
        public const uint VehicleValid = 0x1;
        public const uint VehicleDriven = 0x2;
        public const uint VehicleHayosiko = 1;
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
        public fixed byte pad[24];
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
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public unsafe struct PlayerSlot
    {
        public uint seq;
        public uint reserved;
        public PlayerState value;
        public fixed byte pad[8];
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
        public uint reserved;
        public fixed float spawn[3];
        public float spawnHeading;
    }
}
