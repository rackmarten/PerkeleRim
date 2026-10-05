using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    // Mirrors protocol/tests/cpp/test_link.cpp: both ends in one process, each test on its own section.
    public unsafe class LinkTests
    {
        static int counter;

        static string UniqueName() =>
            @"Local\PerkeleRim.CsTest." + Environment.ProcessId + "." + Interlocked.Increment(ref counter);

        sealed class Pair : IDisposable
        {
            public readonly string Name = UniqueName();
            public readonly List<string> SkyrimLog = new List<string>(), MscLog = new List<string>();
            public Link Skyrim, Msc;
            public ulong Now = 1000;

            public Pair()
            {
                Skyrim = new Link(Side.Skyrim, SkyrimLog.Add, Name);
                Msc = new Link(Side.Msc, MscLog.Add, Name);
            }

            public void Tick(int times = 1)
            {
                for (int i = 0; i < times; i++)
                {
                    Now += 50;
                    Skyrim?.Tick(Now);
                    Msc?.Tick(Now);
                }
            }

            public void Dispose()
            {
                Skyrim?.Dispose();
                Msc?.Dispose();
            }
        }

        static bool Has(List<string> log, string needle) => log.Any(l => l.Contains(needle));

        [Test]
        public void ComesUpOnceBothSidesTick()
        {
            using var p = new Pair();
            p.Tick(1);
            Assert.That(p.Skyrim.IsUp, Is.False);
            p.Tick(2);
            Assert.That(p.Skyrim.IsUp && p.Msc.IsUp, Is.True);
            Assert.That(Has(p.SkyrimLog, "created shared memory"));
            Assert.That(Has(p.MscLog, "joined shared memory"));
            Assert.That(Has(p.MscLog, "link up: Skyrim plugin pid"));
            Assert.That(Has(p.SkyrimLog, "link up: MSC mod pid"));
        }

        [Test]
        public void SlotsCarryTheLatestValue()
        {
            using var p = new Pair();
            p.Tick(3);
            Assert.That(p.Msc.ReadInput(out _), Is.False);

            for (uint frame = 1; frame <= 3; frame++)
            {
                var input = new InputState { frame = frame, flags = Wire.InputOwnsPlayer, mouseX = 1.5 * frame, vertical = 1f };
                input.SetKey(119, true);  // KeyCode.W
                p.Skyrim.WriteInput(ref input);
            }
            Assert.That(p.Msc.ReadInput(out var read), Is.True);
            Assert.That(read.frame, Is.EqualTo(3));
            Assert.That(read.mouseX, Is.EqualTo(4.5));
            Assert.That(read.IsKeyDown(119), Is.True);
            Assert.That(read.IsKeyDown(120), Is.False);

            var player = new PlayerState { frame = 7, flags = Wire.PlayerValid, heading = 1.25f, stance = 2 };
            player.position[2] = -12.5f;
            p.Msc.WritePlayer(ref player);
            Assert.That(p.Skyrim.ReadPlayer(out var back), Is.True);
            Assert.That(back.frame, Is.EqualTo(7));
            Assert.That(back.position[2], Is.EqualTo(-12.5f));
            Assert.That(back.stance, Is.EqualTo(2));

            Assert.That(p.Skyrim.ReadVehicle(out _), Is.False);  // never written
            var vehicle = new VehicleState { frame = 9, flags = Wire.VehicleValid | Wire.VehicleDriven, vehicle = Wire.VehicleHayosiko };
            vehicle.rotation[3] = 1f;
            vehicle.wheelSpin[3] = 2.5f;
            p.Msc.WriteVehicle(ref vehicle);
            Assert.That(p.Skyrim.ReadVehicle(out var vback), Is.True);
            Assert.That(vback.frame, Is.EqualTo(9));
            Assert.That(vback.flags, Is.EqualTo(Wire.VehicleValid | Wire.VehicleDriven));
            Assert.That(vback.rotation[3], Is.EqualTo(1f));
            Assert.That(vback.wheelSpin[3], Is.EqualTo(2.5f));
        }

        [Test]
        public void RingDeliversInOrderAcrossTheWrapPoint()
        {
            using var p = new Pair();
            p.Tick(3);
            var payload = new byte[40000];
            int sent = 0, received = 0;
            for (int round = 0; round < 80; round++)
            {
                for (int i = 0; i < payload.Length; i++) payload[i] = (byte)(sent + i);
                Assert.That(p.Msc.Send(MessageType.WorldspaceChange, payload, payload.Length), Is.True);
                sent++;
                p.Skyrim.Receive((type, data, size) =>
                {
                    Assert.That(type, Is.EqualTo(MessageType.WorldspaceChange));
                    Assert.That(size, Is.EqualTo(40000));
                    Assert.That(data[0], Is.EqualTo((byte)received));
                    Assert.That(data[39999], Is.EqualTo((byte)(received + 39999)));
                    received++;
                });
            }
            Assert.That(received, Is.EqualTo(sent));
        }

        [Test]
        public void RingRefusesWhenFullOrTooLarge()
        {
            using var p = new Pair();
            p.Tick(3);
            var payload = new byte[100000];
            Assert.That(p.Msc.Send(MessageType.WorldspaceChange, payload, 100000), Is.True);
            Assert.That(p.Msc.Send(MessageType.WorldspaceChange, payload, 100000), Is.True);
            Assert.That(p.Msc.Send(MessageType.WorldspaceChange, payload, 100000), Is.False);
            Assert.That(p.Skyrim.Receive((t, d, s) => { }), Is.EqualTo(2));
            Assert.That(p.Msc.Send(MessageType.WorldspaceChange, payload, 100000), Is.True);
        }

        [Test]
        public void GoesDownWhenThePeerStallsOrDetaches()
        {
            using var p = new Pair();
            p.Tick(3);
            for (int i = 0; i < 50; i++) p.Msc.Tick(p.Now += 50);  // Skyrim stalls
            Assert.That(p.Msc.IsUp, Is.False);
            Assert.That(Has(p.MscLog, "link down: no heartbeat from the Skyrim plugin"));

            p.Tick(2);
            Assert.That(p.Msc.IsUp, Is.True);

            p.Skyrim.Dispose();
            p.Skyrim = null;
            p.Tick(1);
            Assert.That(p.Msc.IsUp, Is.False);
            Assert.That(Has(p.MscLog, "link down: the Skyrim plugin detached"));
        }

        [Test]
        public void RestartedPeerDropsStaleMessages()
        {
            using var p = new Pair();
            p.Tick(3);
            Assert.That(p.Msc.Send(MessageType.WorldspaceChange, new byte[] { 42 }, 1), Is.True);
            p.Skyrim.Dispose();
            p.Skyrim = null;
            p.Tick(1);
            p.Skyrim = new Link(Side.Skyrim, p.SkyrimLog.Add, p.Name);
            p.Tick(3);
            Assert.That(p.Skyrim.IsUp, Is.True);
            Assert.That(p.Skyrim.Receive((t, d, s) => { }), Is.EqualTo(0));
        }

        [Test]
        public void PeerOnAnotherVersionDisablesTheLink()
        {
            using var p = new Pair();
            p.Tick(3);
            p.Skyrim.Dispose();
            p.Skyrim = null;
            using (var raw = new RawSection(p.Name, Wire.MappingSize))
            {
                raw.Header->peers0.pid = 1234;
                raw.Header->peers0.version = 99;
                p.Tick(1);
            }
            Assert.That(p.Msc.CurrentState, Is.EqualTo(Link.State.Disabled));
            Assert.That(Has(p.MscLog, "protocol mismatch: the Skyrim plugin speaks v99, this MSC mod speaks v" + Wire.ProtocolVersion));
        }

        [Test]
        public void SectionFromAnotherVersionDisablesTheJoiningSide()
        {
            string name = UniqueName();
            using var raw = new RawSection(name, 4096);
            raw.Header->headerSize = 128;
            raw.Header->mappingSize = 4096;
            raw.Header->creatorVersion = 99;
            raw.Header->magic = Wire.Magic;

            var log = new List<string>();
            using var msc = new Link(Side.Msc, log.Add, name);
            msc.Tick(1000);
            Assert.That(msc.CurrentState, Is.EqualTo(Link.State.Disabled));
            Assert.That(Has(log, "protocol mismatch: the shared memory was created with protocol v99, this MSC mod speaks v" + Wire.ProtocolVersion));
            Assert.That(raw.Header->peers1.version, Is.EqualTo(Wire.ProtocolVersion));
        }

        [Test]
        public void ForeignSectionIsRejected()
        {
            string name = UniqueName();
            using var raw = new RawSection(name, 4096);
            raw.Header->magic = 0xDEADBEEF;
            var log = new List<string>();
            using var skyrim = new Link(Side.Skyrim, log.Add, name);
            skyrim.Tick(1000);
            Assert.That(skyrim.CurrentState, Is.EqualTo(Link.State.Disabled));
            Assert.That(Has(log, "not a PerkeleRim link"));
        }

        // Raw view of a section, for faking a peer of another version.
        sealed class RawSection : IDisposable
        {
            readonly IntPtr handle, view;

            public RawSection(string name, uint size)
            {
                handle = CreateFileMappingW(new IntPtr(-1), IntPtr.Zero, 0x04, 0, size, name);
                view = MapViewOfFile(handle, 0xF001F, 0, 0, UIntPtr.Zero);
            }

            public LinkHeader* Header => (LinkHeader*)view;

            public void Dispose()
            {
                UnmapViewOfFile(view);
                CloseHandle(handle);
            }

            [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
            static extern IntPtr CreateFileMappingW(IntPtr file, IntPtr attributes, uint protect, uint maxSizeHigh, uint maxSizeLow, string name);

            [DllImport("kernel32.dll")]
            static extern IntPtr MapViewOfFile(IntPtr mapping, uint access, uint offsetHigh, uint offsetLow, UIntPtr bytes);

            [DllImport("kernel32.dll")]
            static extern bool UnmapViewOfFile(IntPtr view);

            [DllImport("kernel32.dll")]
            static extern bool CloseHandle(IntPtr handle);
        }
    }
}
