using System;
using System.IO;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class SnapshotTests
    {
        string root;

        [SetUp]
        public void MakeRoot()
        {
            root = Path.Combine(Path.GetTempPath(), "PerkeleRim.Snapshot." + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
        }

        [TearDown]
        public void RemoveRoot() => Directory.Delete(root, true);

        [Test]
        public void PayloadCarriesKeyThenUtf8Path()
        {
            var payload = Snapshot.Encode(0x123456789ABCDEF0ul, @"C:\Saves\PerkeleRim\Päivi");
            Assert.That(BitConverter.ToUInt64(payload, 0), Is.EqualTo(0x123456789ABCDEF0ul));
            Assert.That(BitConverter.ToUInt32(payload, 8), Is.EqualTo(26u));  // ä is two bytes
            Assert.That(Snapshot.Decode(payload, payload.Length, out var key, out var path), Is.True);
            Assert.That(key, Is.EqualTo(0x123456789ABCDEF0ul));
            Assert.That(path, Is.EqualTo(@"C:\Saves\PerkeleRim\Päivi"));
        }

        [Test]
        public void MalformedPayloadsAreRefused()
        {
            Assert.That(Snapshot.Encode(0, "x"), Is.Null);
            Assert.That(Snapshot.Encode(1, new string('a', (int)Wire.MscSnapshotPathMax + 1)), Is.Null);
            var payload = Snapshot.Encode(5, "folder");
            Assert.That(Snapshot.Decode(payload, 8, out _, out _), Is.False);
            Assert.That(Snapshot.Decode(payload, payload.Length - 1, out _, out _), Is.False);
            var zero = (byte[])payload.Clone();
            Array.Clear(zero, 0, 8);
            Assert.That(Snapshot.Decode(zero, zero.Length, out _, out _), Is.False);
        }

        static Snapshot Sample()
        {
            var s = new Snapshot { Key = 0xFEDCBA9876543210ul, World = true };
            s.Globals.Add(new Snapshot.Global { Kind = 'f', Name = "PlayerThirst", Value = 12.345678f });
            s.Globals.Add(new Snapshot.Global { Kind = 'i', Name = "PlayerCigarettes", Value = 7 });
            s.Globals.Add(new Snapshot.Global { Kind = 'b', Name = "PlayerHelmet", Value = 1 });
            s.Fsms.Add(new Snapshot.FsmValue { Path = "MAP/SUN/Pivot/SUN", Fsm = "Color", Kind = 's', Name = "18-20" });
            s.Fsms.Add(new Snapshot.FsmValue { Path = "MAP/SUN/Pivot", Fsm = "Rotation", Kind = 'f', Name = "Rotation", Value = 60.25f });
            s.Vehicles.Add(new Snapshot.Body
            {
                Name = "HAYOSIKO(1500kg, 250)",
                Position = new Vec3(15170.5f, -14516.25f, 3021f),
                Rotation = new Quat(0.1f, 0.7f, -0.05f, 0.7f),
                Velocity = new Vec3(3f, -0.5f, 12.75f),
                Angular = new Vec3(0f, 0.25f, 0f),
                Fuel = 41.5f,
                Driven = true,
            });
            s.Items.Add(new Snapshot.Body { Id = -12345, Name = "beer case(itemx)", Position = new Vec3(1, 2, 3), Rotation = new Quat(0, 0, 0, 1), Bottles = 18 });
            s.Items.Add(new Snapshot.Body { Id = 678, Name = "sausages(itemx)", Rotation = new Quat(0, 0, 0, 1) });
            return s;
        }

        [Test]
        public void TextKeepsEveryValueExactly()
        {
            var w = new StringWriter();
            Sample().Write(w);
            var s = Snapshot.Read(new StringReader(w.ToString()));

            Assert.That(s, Is.Not.Null);
            Assert.That(s.Key, Is.EqualTo(0xFEDCBA9876543210ul));
            Assert.That(s.World, Is.True);
            Assert.That(s.Globals.Count, Is.EqualTo(3));
            Assert.That(s.Globals[0].Kind, Is.EqualTo('f'));
            Assert.That((float)s.Globals[0].Value, Is.EqualTo(12.345678f));
            Assert.That(s.Globals[1].Name, Is.EqualTo("PlayerCigarettes"));
            Assert.That(s.Globals[1].Value, Is.EqualTo(7));
            Assert.That(s.Fsms.Count, Is.EqualTo(2));
            Assert.That(s.Fsms[0].Path, Is.EqualTo("MAP/SUN/Pivot/SUN"));
            Assert.That(s.Fsms[0].Kind, Is.EqualTo('s'));
            Assert.That(s.Fsms[0].Name, Is.EqualTo("18-20"));
            Assert.That(s.Fsms[1].Fsm, Is.EqualTo("Rotation"));
            Assert.That((float)s.Fsms[1].Value, Is.EqualTo(60.25f));
            var v = s.Vehicles[0];
            Assert.That(v.Name, Is.EqualTo("HAYOSIKO(1500kg, 250)"));
            Assert.That(v.Position.y, Is.EqualTo(-14516.25f));
            Assert.That(v.Rotation.y, Is.EqualTo(0.7f));
            Assert.That(v.Velocity.z, Is.EqualTo(12.75f));
            Assert.That(v.Angular.y, Is.EqualTo(0.25f));
            Assert.That(v.Fuel, Is.EqualTo(41.5f));
            Assert.That(v.Driven, Is.True);
            Assert.That(s.Items.Count, Is.EqualTo(2));
            Assert.That(s.Items[0].Id, Is.EqualTo(-12345));
            Assert.That(s.Items[0].Name, Is.EqualTo("beer case(itemx)"));
            Assert.That(s.Items[0].Bottles, Is.EqualTo(18));
            Assert.That(s.Items[1].Bottles, Is.EqualTo(-1));
        }

        [Test]
        public void NoFuelStaysNoFuel()
        {
            var s = new Snapshot { Key = 1 };
            s.Vehicles.Add(new Snapshot.Body { Name = "JONNEZ ES(Clone)" });
            var w = new StringWriter();
            s.Write(w);
            Assert.That(float.IsNaN(Snapshot.Read(new StringReader(w.ToString())).Vehicles[0].Fuel), Is.True);
        }

        [Test]
        public void DamagedLinesAreSkippedAndOthersKept()
        {
            var text = "PerkeleRim snapshot 1\nkey\t2A\nglobal\tf\tPlayerMoney\tlots\nglobal\tf\tPlayerHunger\t5\nsomething new\t1\nitem\t1\tshort\n";
            var s = Snapshot.Read(new StringReader(text));
            Assert.That(s.Key, Is.EqualTo(0x2Aul));
            Assert.That(s.Globals.Count, Is.EqualTo(1));
            Assert.That(s.Globals[0].Name, Is.EqualTo("PlayerHunger"));
            Assert.That(s.Items, Is.Empty);
        }

        [Test]
        public void NotASnapshotWithoutHeaderOrKey()
        {
            Assert.That(Snapshot.Read(new StringReader("hello\nkey\t2A\n")), Is.Null);
            Assert.That(Snapshot.Read(new StringReader("PerkeleRim snapshot 1\nworld\t1\n")), Is.Null);
            Assert.That(Snapshot.Read(new StringReader("")), Is.Null);
        }

        [Test]
        public void StoreReplacesAndLoadReadsBack()
        {
            string folder = Path.Combine(root, "Saves", "Quicksave0");
            var first = new Snapshot { Key = 1 };
            first.Store(folder);
            var second = Sample();
            second.Store(folder);

            var loaded = Snapshot.Load(folder);
            Assert.That(loaded.Key, Is.EqualTo(second.Key));
            Assert.That(loaded.Items.Count, Is.EqualTo(2));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(1));  // no .tmp left over
        }

        [Test]
        public void NothingToLoadWithoutAFile()
        {
            Assert.That(Snapshot.Load(Path.Combine(root, "missing")), Is.Null);
            File.WriteAllText(Path.Combine(root, Snapshot.FileName), "not a snapshot");
            Assert.That(Snapshot.Load(root), Is.Null);
        }
    }
}
