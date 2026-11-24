using System;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class PartReportTests
    {
        static PartReport Door() => new PartReport
        {
            Name = "Door_Left",
            Flags = PartReport.FlagsFor(true, false, false, true),
            Generation = 7,
            Position = new Vec3(15170f, -14516f, -4600f),
            Bolts = 4,
            BoltsTight = 1,
            Stages = 13,
            Tightness = 13f,
            TightnessFull = 28f,
            Object = 0xABCD1234u,
        };

        [Test]
        public void RoundTrips()
        {
            var payload = Door().Encode();
            Assert.That(payload.Length, Is.EqualTo(PartReport.Size));
            var back = PartReport.Decode(payload, payload.Length);
            Assert.That(back, Is.Not.Null);
            Assert.That(back.Value.Name, Is.EqualTo("Door_Left"));
            Assert.That(back.Value.Flags, Is.EqualTo(Wire.PartInstalled | Wire.PartPlaced));
            Assert.That(back.Value.Generation, Is.EqualTo(7u));
            Assert.That(back.Value.Position.y, Is.EqualTo(-14516f));
            Assert.That(back.Value.Bolts, Is.EqualTo(4));
            Assert.That(back.Value.BoltsTight, Is.EqualTo(1));
            Assert.That(back.Value.Stages, Is.EqualTo(13));
            Assert.That(back.Value.TightnessFull, Is.EqualTo(28f));
            Assert.That(back.Value.Object, Is.EqualTo(0xABCD1234u));
        }

        [Test]
        public void LaysOutLikeTheHeader()
        {
            var payload = Door().Encode();
            Assert.That(BitConverter.ToUInt32(payload, 12), Is.EqualTo(Wire.PartInstalled | Wire.PartPlaced));
            Assert.That(BitConverter.ToUInt16(payload, 20), Is.EqualTo(4));
            Assert.That(BitConverter.ToUInt32(payload, 36), Is.EqualTo(0xABCD1234u));
            Assert.That(BitConverter.ToUInt32(payload, 40), Is.EqualTo(9u));
            Assert.That((char)payload[44], Is.EqualTo('D'));
        }

        [Test]
        public void ClampsWhatCanNotBe()
        {
            var p = Door();
            p.BoltsTight = 9;
            p.Stages = 99;
            var back = PartReport.Decode(p.Encode(), PartReport.Size);
            Assert.That(back, Is.Not.Null);
            Assert.That(back.Value.BoltsTight, Is.EqualTo(4));
            Assert.That(back.Value.Stages, Is.EqualTo(32));
        }

        [Test]
        public void RefusesMalformedPayloads()
        {
            var payload = Door().Encode();
            Assert.That(PartReport.Decode(payload, PartReport.Size - 1), Is.Null);
            var nameless = Door();
            nameless.Name = "";
            Assert.That(PartReport.Decode(nameless.Encode(), PartReport.Size), Is.Null);
            BitConverter.GetBytes(Wire.PartNameMax + 1).CopyTo(payload, 40);
            Assert.That(PartReport.Decode(payload, PartReport.Size), Is.Null);
        }

        [Test]
        public void CutsALongNameAtACharacter()
        {
            var p = Door();
            p.Name = new string('a', (int)Wire.PartNameMax - 1) + "ä";
            Assert.That(PartReport.Decode(p.Encode(), PartReport.Size).Value.Name, Is.EqualTo(new string('a', (int)Wire.PartNameMax - 1)));
        }

        [Test]
        public void SendsChangesAndRealMovesOnly()
        {
            var door = Door();
            Assert.That(PartReport.ShouldSend(null, door), Is.True);
            Assert.That(PartReport.ShouldSend(door, door), Is.False);
            var moved = door;
            moved.Position = new Vec3(door.Position.x + 10f, door.Position.y, door.Position.z);
            Assert.That(PartReport.ShouldSend(door, moved), Is.False);  // 14 cm
            moved.Position = new Vec3(door.Position.x + 20f, door.Position.y, door.Position.z);
            Assert.That(PartReport.ShouldSend(door, moved), Is.True);   // 29 cm
            var other = door;
            other.Object = 1;
            Assert.That(PartReport.ShouldSend(door, other), Is.True);  // the part's object swapped (the radiator's two)
            var tighter = door;
            tighter.Stages = 14;
            Assert.That(PartReport.ShouldSend(door, tighter), Is.True);
            var anchor = door;
            anchor.Generation = 8;
            Assert.That(PartReport.ShouldSend(door, anchor), Is.True);
            var away = door;
            away.Flags &= ~Wire.PartPlaced;
            var awayMoved = away;
            awayMoved.Position = new Vec3(0f, 0f, 0f);
            Assert.That(PartReport.ShouldSend(away, awayMoved), Is.False);  // off the island its position means nothing
        }

        [Test]
        public void CountsBoltsFromTheirStages()
        {
            int bolts, tight, sum;
            PartReport.CountBolts(new[] { 8, 8, 3, 0 }, out bolts, out tight, out sum);
            Assert.That(new[] { bolts, tight, sum }, Is.EqualTo(new[] { 4, 2, 19 }));
            PartReport.CountBolts(new[] { 12, -1 }, out bolts, out tight, out sum);
            Assert.That(new[] { bolts, tight, sum }, Is.EqualTo(new[] { 2, 1, 8 }));
            PartReport.CountBolts(new[] { 8, 3, 8, 8 }, 2, out bolts, out tight, out sum);  // a buffer longer than the part's bolts
            Assert.That(new[] { bolts, tight, sum }, Is.EqualTo(new[] { 2, 1, 11 }));
            PartReport.CountBolts(null, out bolts, out tight, out sum);
            Assert.That(new[] { bolts, tight, sum }, Is.EqualTo(new[] { 0, 0, 0 }));
        }

        [Test]
        public void FlagsFollowMscsDatabase()
        {
            Assert.That(PartReport.FlagsFor(false, false, false, false), Is.EqualTo(0u));
            Assert.That(PartReport.FlagsFor(true, true, true, true),
                Is.EqualTo(Wire.PartInstalled | Wire.PartBolted | Wire.PartDamaged | Wire.PartPlaced));
        }
    }
}
