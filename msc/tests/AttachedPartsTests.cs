using System.Collections.Generic;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class AttachedPartsTests
    {
        static AttachedParts.Entry Part(uint id, uint model, bool visible = true, float x = 0f) => new AttachedParts.Entry
        {
            Part = id,
            Model = model,
            Visible = visible,
            Rotation = new Quat(0f, 0f, 0.6f, 0.8f),
            Translation = new Vec3(x, 20f, -5f),
        };

        [Test]
        public unsafe void EncodesEveryPartInOrderWithItsModelAndPose()
        {
            var set = new PartSet();
            int left = AttachedParts.Encode(new List<AttachedParts.Entry> { Part(7, 100, true, 12f), Part(9, 101, false) }, Wire.VehicleSatsuma, 3, 42, ref set);
            Assert.That(left, Is.Zero);
            Assert.That(set.vehicle, Is.EqualTo(Wire.VehicleSatsuma));
            Assert.That(set.generation, Is.EqualTo(3u));
            Assert.That(set.frame, Is.EqualTo(42u));
            Assert.That(set.count, Is.EqualTo(2u));
            var first = set.Get(0);
            Assert.That(first.part, Is.EqualTo(7u));
            Assert.That(first.model, Is.EqualTo(100u));
            Assert.That(first.flags, Is.EqualTo(Wire.PartVisible));
            Assert.That(first.translation[0], Is.EqualTo(12f));
            Assert.That(first.translation[1], Is.EqualTo(20f));
            Assert.That(first.translation[2], Is.EqualTo(-5f));
            Assert.That(first.rotation[2], Is.EqualTo(0.6f));
            Assert.That(first.rotation[3], Is.EqualTo(0.8f));
            var second = set.Get(1);
            Assert.That(second.part, Is.EqualTo(9u));
            Assert.That(second.flags, Is.Zero, "a part MSC doesn't draw right now");
        }

        [Test]
        public void ABareBodyIsAnEmptySet()
        {
            var set = new PartSet { count = 5 };
            Assert.That(AttachedParts.Encode(new List<AttachedParts.Entry>(), Wire.VehicleSatsuma, 1, 1, ref set), Is.Zero);
            Assert.That(set.count, Is.Zero);
            Assert.That(set.vehicle, Is.EqualTo(Wire.VehicleSatsuma), "empty, but still the Satsuma's: its parts all came off");
        }

        [Test]
        public void AModelNotReadYetGoesOutAsZero()
        {
            var set = new PartSet();
            AttachedParts.Encode(new List<AttachedParts.Entry> { Part(7, 0) }, Wire.VehicleSatsuma, 1, 1, ref set);
            Assert.That(set.count, Is.EqualTo(1u));
            Assert.That(set.Get(0).model, Is.Zero);
        }

        [Test]
        public void EachPartGoesOutOnceAndNeverAsZero()
        {
            var set = new PartSet();
            int left = AttachedParts.Encode(new List<AttachedParts.Entry> { Part(0, 100), Part(7, 100), Part(7, 101), Part(8, 100) }, Wire.VehicleSatsuma, 1, 1, ref set);
            Assert.That(left, Is.EqualTo(2));
            Assert.That(set.count, Is.EqualTo(2u));
            Assert.That(set.Get(0).part, Is.EqualTo(7u));
            Assert.That(set.Get(0).model, Is.EqualTo(100u), "the first entry of a part wins");
            Assert.That(set.Get(1).part, Is.EqualTo(8u));
        }

        [Test]
        public void PartsPastTheCapacityAreLeftOut()
        {
            var entries = new List<AttachedParts.Entry>();
            for (uint i = 1; i <= Wire.PartSetCapacity + 10; i++) entries.Add(Part(i, 100));
            var set = new PartSet();
            Assert.That(AttachedParts.Encode(entries, Wire.VehicleSatsuma, 1, 1, ref set), Is.EqualTo(10));
            Assert.That(set.count, Is.EqualTo(Wire.PartSetCapacity));
            Assert.That(set.Get((int)Wire.PartSetCapacity - 1).part, Is.EqualTo(Wire.PartSetCapacity));
        }

        [Test]
        public void TheSameSetOnlyMovedIsTheSame()
        {
            var a = new List<AttachedParts.Entry> { Part(7, 100), Part(8, 101) };
            var moved = new List<AttachedParts.Entry> { Part(7, 100, false, 50f), Part(8, 101, true, 9f) };
            Assert.That(AttachedParts.SameSet(a, moved), Is.True);
            Assert.That(AttachedParts.SameSet(a, new List<AttachedParts.Entry> { Part(7, 100) }), Is.False, "a part came off");
            Assert.That(AttachedParts.SameSet(a, new List<AttachedParts.Entry> { Part(7, 100), Part(8, 102) }), Is.False, "a part looks different");
            Assert.That(AttachedParts.SameSet(a, new List<AttachedParts.Entry> { Part(8, 101), Part(7, 100) }), Is.False);
        }
    }
}
