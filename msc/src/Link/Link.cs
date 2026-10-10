using System;
using System.Runtime.InteropServices;
using System.Threading;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // C# end of the PerkeleRim link, a line-by-line mirror of protocol/cpp/link.cpp (see there and
    // docs/PROTOCOL.md for the rules). Unity-free; LinkHost drives it from the game loop.
    // net35 has no MemoryMappedFile, so the section is opened through kernel32 directly.
    public sealed unsafe class Link : IDisposable
    {
        public enum State
        {
            Detached,
            Waiting,
            Up,
            Disabled,
        }

        public delegate void MessageHandler(MessageType type, byte[] payload, int size);

        readonly Side side;
        readonly Action<string> log;
        readonly string name;

        IntPtr mapping = IntPtr.Zero;
        byte* basePtr;
        State state = State.Detached;
        bool attached;
        bool mapFailLogged;
        bool peerSeeded;
        bool peerSeen;
        ulong peerHeartbeat;
        ulong peerSeenMs;
        byte[] receiveBuffer = new byte[4096];

        public Link(Side side, Action<string> log, string mappingName = Wire.MappingName)
        {
            this.side = side;
            this.log = log;
            name = mappingName;
        }

        public State CurrentState => state;
        public bool IsUp => state == State.Up;

        static string SideName(Side s) => s == Side.Skyrim ? "Skyrim plugin" : "MSC mod";
        static Side Other(Side s) => s == Side.Skyrim ? Side.Msc : Side.Skyrim;

        LinkHeader* Header => (LinkHeader*)(basePtr + Wire.OffsetHeader);
        PeerInfo* Peer(Side s) => s == Side.Skyrim ? &Header->peers0 : &Header->peers1;
        RingHeader* Outbound(Side s) => (RingHeader*)(basePtr + (s == Side.Skyrim ? Wire.OffsetRingToMsc : Wire.OffsetRingToSkyrim));
        RingHeader* Inbound(Side s) => Outbound(Other(s));

        void TryMap()
        {
            IntPtr handle = Native.CreateFileMappingW(Native.InvalidHandle, IntPtr.Zero, Native.PageReadWrite, 0, Wire.MappingSize, name);
            if (handle == IntPtr.Zero)
            {
                if (!mapFailLogged) log("cannot create shared memory (error " + Marshal.GetLastWin32Error() + "), retrying");
                mapFailLogged = true;
                return;
            }
            bool existed = Marshal.GetLastWin32Error() == Native.ErrorAlreadyExists;

            // Size 0 maps the whole section, whatever size its creator gave it. Until the version check
            // passes only the frozen LinkHeader is touched.
            IntPtr view = Native.MapViewOfFile(handle, Native.FileMapAllAccess, 0, 0, UIntPtr.Zero);
            if (view == IntPtr.Zero)
            {
                if (!mapFailLogged) log("cannot map shared memory (error " + Marshal.GetLastWin32Error() + "), retrying");
                mapFailLogged = true;
                Native.CloseHandle(handle);
                return;
            }

            byte* b = (byte*)view;
            if (!existed)
            {
                // Fresh sections are zero-filled. Publish magic last so the peer never sees half a header.
                var header = (LinkHeader*)(b + Wire.OffsetHeader);
                header->headerSize = (uint)sizeof(LinkHeader);
                header->mappingSize = Wire.MappingSize;
                header->creatorVersion = Wire.ProtocolVersion;
                ((RingHeader*)(b + Wire.OffsetRingToMsc))->capacity = Wire.RingToMscCapacity;
                ((RingHeader*)(b + Wire.OffsetRingToSkyrim))->capacity = Wire.RingToSkyrimCapacity;
                Thread.MemoryBarrier();
                Thread.VolatileWrite(ref *(int*)&header->magic, unchecked((int)Wire.Magic));
            }

            mapping = handle;
            basePtr = b;
            state = State.Waiting;
            log(existed ? "joined shared memory, waiting for peer" : "created shared memory, waiting for peer");
        }

        // Attaches, bumps our heartbeat and checks the peer. Call regularly (10+ times a second).
        public void Tick(ulong nowMs)
        {
            State current = state;
            if (current == State.Disabled) return;
            if (basePtr == null)
            {
                TryMap();
                if (basePtr == null) return;
            }

            LinkHeader* header = Header;
            uint magic = (uint)Thread.VolatileRead(ref *(int*)&header->magic);
            if (magic == 0) return;  // creator is still initializing
            if (magic != Wire.Magic || header->headerSize != (uint)sizeof(LinkHeader))
            {
                Disable("the shared memory is not a PerkeleRim link (unknown magic or header size)");
                return;
            }

            PeerInfo* self = Peer(side);
            PeerInfo* peer = Peer(Other(side));

            if (!attached)
            {
                // Publish ourselves first, even on a version mismatch, so the peer can report it too.
                Thread.VolatileWrite(ref *(int*)&self->pid, (int)Native.GetCurrentProcessId());
                Thread.VolatileWrite(ref *(int*)&self->version, (int)Wire.ProtocolVersion);
                if (header->creatorVersion != Wire.ProtocolVersion || header->mappingSize != Wire.MappingSize)
                {
                    Disable("protocol mismatch: the shared memory was created with protocol v" + header->creatorVersion +
                            ", this " + SideName(side) + " speaks v" + Wire.ProtocolVersion + "; update both halves to the same release");
                    return;
                }
                // Anything still in our inbound ring was sent to an earlier instance of this side.
                RingHeader* inbound = Inbound(side);
                Interlocked.Exchange(ref *(long*)&inbound->tail, Interlocked.Read(ref *(long*)&inbound->head));
                attached = true;
            }

            Interlocked.Increment(ref *(long*)&self->heartbeat);

            uint peerVersion = (uint)Thread.VolatileRead(ref *(int*)&peer->version);
            uint peerPid = (uint)Thread.VolatileRead(ref *(int*)&peer->pid);
            if (peerVersion != 0 && peerVersion != Wire.ProtocolVersion)
            {
                Disable("protocol mismatch: the " + SideName(Other(side)) + " speaks v" + peerVersion + ", this " +
                        SideName(side) + " speaks v" + Wire.ProtocolVersion + "; update both halves to the same release");
                return;
            }

            ulong heartbeat = (ulong)Interlocked.Read(ref *(long*)&peer->heartbeat);
            if (!peerSeeded)
            {
                peerSeeded = true;
                peerHeartbeat = heartbeat;
            }
            else if (heartbeat != peerHeartbeat)
            {
                peerHeartbeat = heartbeat;
                peerSeen = true;
                peerSeenMs = nowMs;
            }

            bool alive = peerPid != 0 && peerVersion == Wire.ProtocolVersion && peerSeen && nowMs - peerSeenMs < Wire.PeerTimeoutMs;
            if (alive && current != State.Up)
            {
                state = State.Up;
                log("link up: " + SideName(Other(side)) + " pid " + peerPid + ", protocol v" + Wire.ProtocolVersion);
            }
            else if (!alive && current == State.Up)
            {
                state = State.Waiting;
                log(peerPid == 0
                    ? "link down: the " + SideName(Other(side)) + " detached"
                    : "link down: no heartbeat from the " + SideName(Other(side)) + " for " + Wire.PeerTimeoutMs + " ms");
            }
        }

        void Disable(string reason)
        {
            state = State.Disabled;
            log("link disabled: " + reason);
        }

        public void Dispose()
        {
            if (basePtr == null) return;
            if (attached || state == State.Disabled)
            {
                PeerInfo* self = Peer(side);
                Thread.VolatileWrite(ref *(int*)&self->pid, 0);
                Thread.VolatileWrite(ref *(int*)&self->version, 0);
            }
            attached = false;
            Native.UnmapViewOfFile((IntPtr)basePtr);
            Native.CloseHandle(mapping);
            basePtr = null;
            mapping = IntPtr.Zero;
            state = State.Detached;
        }

        // ---- Latest-value slots (seqlock). Write only the slot your side owns. ----

        public void WriteInput(ref InputState value)
        {
            if (basePtr == null || !attached) return;
            var slot = (InputSlot*)(basePtr + Wire.OffsetInputSlot);
            BeginWrite(&slot->seq, out uint n);
            slot->value = value;
            EndWrite(&slot->seq, n);
        }

        public bool ReadInput(out InputState value)
        {
            value = default(InputState);
            if (!IsUp) return false;
            var slot = (InputSlot*)(basePtr + Wire.OffsetInputSlot);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                uint before = (uint)Thread.VolatileRead(ref *(int*)&slot->seq);
                if (before == 0) return false;
                if ((before & 1) != 0) continue;
                value = slot->value;
                Thread.MemoryBarrier();
                if ((uint)Thread.VolatileRead(ref *(int*)&slot->seq) == before) return true;
            }
            return false;
        }

        public void WritePlayer(ref PlayerState value)
        {
            if (basePtr == null || !attached) return;
            var slot = (PlayerSlot*)(basePtr + Wire.OffsetPlayerSlot);
            BeginWrite(&slot->seq, out uint n);
            slot->value = value;
            EndWrite(&slot->seq, n);
        }

        public bool ReadPlayer(out PlayerState value)
        {
            value = default(PlayerState);
            if (!IsUp) return false;
            var slot = (PlayerSlot*)(basePtr + Wire.OffsetPlayerSlot);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                uint before = (uint)Thread.VolatileRead(ref *(int*)&slot->seq);
                if (before == 0) return false;
                if ((before & 1) != 0) continue;
                value = slot->value;
                Thread.MemoryBarrier();
                if ((uint)Thread.VolatileRead(ref *(int*)&slot->seq) == before) return true;
            }
            return false;
        }

        public void WriteVehicle(ref VehicleState value)
        {
            if (basePtr == null || !attached) return;
            var slot = (VehicleSlot*)(basePtr + Wire.OffsetVehicleSlot);
            BeginWrite(&slot->seq, out uint n);
            slot->value = value;
            EndWrite(&slot->seq, n);
        }

        public bool ReadVehicle(out VehicleState value)
        {
            value = default(VehicleState);
            if (!IsUp) return false;
            var slot = (VehicleSlot*)(basePtr + Wire.OffsetVehicleSlot);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                uint before = (uint)Thread.VolatileRead(ref *(int*)&slot->seq);
                if (before == 0) return false;
                if ((before & 1) != 0) continue;
                value = slot->value;
                Thread.MemoryBarrier();
                if ((uint)Thread.VolatileRead(ref *(int*)&slot->seq) == before) return true;
            }
            return false;
        }

        public void WriteVehicleParts(ref VehicleParts value)
        {
            if (basePtr == null || !attached) return;
            var slot = (VehiclePartsSlot*)(basePtr + Wire.OffsetVehiclePartsSlot);
            BeginWrite(&slot->seq, out uint n);
            slot->value = value;
            EndWrite(&slot->seq, n);
        }

        public bool ReadVehicleParts(out VehicleParts value)
        {
            value = default(VehicleParts);
            if (!IsUp) return false;
            var slot = (VehiclePartsSlot*)(basePtr + Wire.OffsetVehiclePartsSlot);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                uint before = (uint)Thread.VolatileRead(ref *(int*)&slot->seq);
                if (before == 0) return false;
                if ((before & 1) != 0) continue;
                value = slot->value;
                Thread.MemoryBarrier();
                if ((uint)Thread.VolatileRead(ref *(int*)&slot->seq) == before) return true;
            }
            return false;
        }

        static void BeginWrite(uint* seq, out uint n)
        {
            n = *seq;
            Thread.VolatileWrite(ref *(int*)seq, unchecked((int)(n + 1)));
            Thread.MemoryBarrier();
        }

        static void EndWrite(uint* seq, uint n)
        {
            Thread.MemoryBarrier();
            Thread.VolatileWrite(ref *(int*)seq, unchecked((int)(n + 2)));
        }

        public void WriteItems(ref ItemState value)
        {
            if (basePtr == null || !attached) return;
            var slot = (ItemSlot*)(basePtr + Wire.OffsetItemSlot);
            BeginWrite(&slot->seq, out uint n);
            slot->value = value;
            EndWrite(&slot->seq, n);
        }

        public bool ReadItems(out ItemState value)
        {
            value = default(ItemState);
            if (!IsUp) return false;
            var slot = (ItemSlot*)(basePtr + Wire.OffsetItemSlot);
            for (int attempt = 0; attempt < 64; attempt++)
            {
                uint before = (uint)Thread.VolatileRead(ref *(int*)&slot->seq);
                if (before == 0) return false;
                if ((before & 1) != 0) continue;
                value = slot->value;
                Thread.MemoryBarrier();
                if ((uint)Thread.VolatileRead(ref *(int*)&slot->seq) == before) return true;
            }
            return false;
        }

        // ---- Rings ----

        // Outbound ring (MSC -> Skyrim here). False when the link is down, the ring is full or the
        // message can never fit.
        public bool Send(MessageType type, byte[] payload, int size)
        {
            if (!IsUp) return false;
            RingHeader* ring = Outbound(side);
            ulong total = (ulong)sizeof(MessageHeader) + (ulong)size;
            ulong head = (ulong)Interlocked.Read(ref *(long*)&ring->head);
            ulong tail = (ulong)Interlocked.Read(ref *(long*)&ring->tail);
            if (total > ring->capacity - (head - tail)) return false;
            var message = new MessageHeader { type = (ushort)type, size = (uint)size };
            CopyIn(ring, head, (byte*)&message, sizeof(MessageHeader));
            fixed (byte* p = payload) CopyIn(ring, head + (ulong)sizeof(MessageHeader), p, size);
            Interlocked.Exchange(ref *(long*)&ring->head, (long)(head + total));
            return true;
        }

        // Drains the inbound ring. The payload array is reused between calls: copy what you keep.
        public int Receive(MessageHandler handler)
        {
            if (!attached || state == State.Disabled) return 0;
            RingHeader* ring = Inbound(side);
            ulong head = (ulong)Interlocked.Read(ref *(long*)&ring->head);
            ulong tail = (ulong)Interlocked.Read(ref *(long*)&ring->tail);
            int count = 0;
            while (head - tail >= (ulong)sizeof(MessageHeader))
            {
                MessageHeader message;
                CopyOut(ring, tail, (byte*)&message, sizeof(MessageHeader));
                if ((ulong)sizeof(MessageHeader) + message.size > head - tail)
                {
                    log("inbound ring corrupt (message of " + message.size + " bytes), dropping it");
                    tail = head;
                    break;
                }
                int size = (int)message.size;
                if (receiveBuffer.Length < size) receiveBuffer = new byte[Math.Max(size, receiveBuffer.Length * 2)];
                fixed (byte* p = receiveBuffer) CopyOut(ring, tail + (ulong)sizeof(MessageHeader), p, size);
                tail += (ulong)sizeof(MessageHeader) + message.size;
                Interlocked.Exchange(ref *(long*)&ring->tail, (long)tail);
                handler((MessageType)message.type, receiveBuffer, size);
                count++;
            }
            Interlocked.Exchange(ref *(long*)&ring->tail, (long)tail);
            return count;
        }

        static void CopyIn(RingHeader* ring, ulong pos, byte* src, int size)
        {
            ulong cap = ring->capacity;
            var data = (byte*)(ring + 1);
            int at = (int)(pos & (cap - 1));
            int first = (int)Math.Min((ulong)size, cap - (ulong)at);
            Copy(src, data + at, first);
            Copy(src + first, data, size - first);
        }

        static void CopyOut(RingHeader* ring, ulong pos, byte* dst, int size)
        {
            ulong cap = ring->capacity;
            var data = (byte*)(ring + 1);
            int at = (int)(pos & (cap - 1));
            int first = (int)Math.Min((ulong)size, cap - (ulong)at);
            Copy(data + at, dst, first);
            Copy(data, dst + first, size - first);
        }

        // net35 has no Buffer.MemoryCopy.
        static void Copy(byte* src, byte* dst, int size)
        {
            int i = 0;
            for (; i + 8 <= size; i += 8) *(ulong*)(dst + i) = *(ulong*)(src + i);
            for (; i < size; i++) dst[i] = src[i];
        }

        static class Native
        {
            public static readonly IntPtr InvalidHandle = new IntPtr(-1);
            public const uint PageReadWrite = 0x04;
            public const uint FileMapAllAccess = 0xF001F;
            public const int ErrorAlreadyExists = 183;

            [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            public static extern IntPtr CreateFileMappingW(IntPtr file, IntPtr attributes, uint protect, uint maxSizeHigh, uint maxSizeLow, string name);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool UnmapViewOfFile(IntPtr view);

            [DllImport("kernel32.dll", SetLastError = true)]
            public static extern bool CloseHandle(IntPtr handle);

            [DllImport("kernel32.dll")]
            public static extern uint GetCurrentProcessId();
        }
    }
}
