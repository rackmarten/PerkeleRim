using System.Collections.Generic;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class ThrowingTests
    {
        static ActorBody Person(uint id, float x, float z) =>
            new ActorBody { Id = id, Feet = new Vec3(x, 0f, z), Radius = 0.3f, Height = 1.8f };

        [Test]
        public void BottlesAreTheOnesThrownHarder()
        {
            Assert.That(Throwing.IsBottle("empty bottle(Clone)"), Is.True);
            Assert.That(Throwing.IsBottle("booze(itemx)"), Is.True);
            Assert.That(Throwing.IsBottle("spirit(itemx)"), Is.True);
            Assert.That(Throwing.IsBottle("beer case(itemx)"), Is.False);
            Assert.That(Throwing.IsBottle("sausages(itemx)"), Is.False);
            Assert.That(Throwing.IsBottle(null), Is.False);
        }

        [Test]
        public void SweepFindsTheFirstBodyAlongTheWay()
        {
            var bodies = new List<ActorBody> { Person(2, 0f, 6f), Person(1, 0f, 3f) };
            Assert.That(Throwing.Sweep(new Vec3(0f, 1f, 0f), new Vec3(0f, 1f, 8f), 0.1f, bodies), Is.EqualTo(1));
            Assert.That(Throwing.Sweep(new Vec3(0f, 1f, 4f), new Vec3(0f, 1f, 8f), 0.1f, bodies), Is.EqualTo(0));
            Assert.That(Throwing.Sweep(new Vec3(2f, 1f, 0f), new Vec3(2f, 1f, 8f), 0.1f, bodies), Is.EqualTo(-1));  // passes beside them
            Assert.That(Throwing.Sweep(new Vec3(0f, 3f, 0f), new Vec3(0f, 3f, 8f), 0.1f, bodies), Is.EqualTo(-1));  // over their heads
        }

        [Test]
        public void SweepOfAPointIsInsideOrNot()
        {
            var bodies = new List<ActorBody> { Person(1, 0f, 3f) };
            Assert.That(Throwing.Sweep(new Vec3(0.2f, 0.5f, 3f), new Vec3(0.2f, 0.5f, 3f), 0.05f, bodies), Is.EqualTo(0));
            Assert.That(Throwing.Sweep(new Vec3(1f, 0.5f, 3f), new Vec3(1f, 0.5f, 3f), 0.05f, bodies), Is.EqualTo(-1));
        }

        [Test]
        public void PissHitsEachActorEveryHalfSecondAtMost()
        {
            var pacer = new HitPacer(Throwing.PissEvery);
            Assert.That(pacer.Ready(1, 0f), Is.True);
            Assert.That(pacer.Ready(1, 0.2f), Is.False);
            Assert.That(pacer.Ready(2, 0.2f), Is.True);
            Assert.That(pacer.Ready(1, 0.5f), Is.True);
            pacer.Clear();
            Assert.That(pacer.Ready(2, 0.3f), Is.True);
        }

        [Test]
        public void AWayEndingInAHitKeepsTheSpeedFromBefore()
        {
            Assert.That(Throwing.PathSpeed(0.5f, 14f), Is.EqualTo(14f));  // stopped by a barricade this frame
            Assert.That(Throwing.PathSpeed(13f, 14f), Is.EqualTo(14f));
            Assert.That(Throwing.PathSpeed(6f, 0f), Is.EqualTo(6f));     // first frame
        }

        [Test]
        public unsafe void AThrownPathCarriesItsFlightSpeedAndWay()
        {
            var path = new PerkeleRim.Protocol.ThrownPath { flight = 3, speed = 12f };
            path.from[2] = 512f;
            path.to[0] = -40f;
            var bytes = new byte[sizeof(PerkeleRim.Protocol.ThrownPath)];
            fixed (byte* p = bytes) *(PerkeleRim.Protocol.ThrownPath*)p = path;
            Assert.That(bytes.Length, Is.EqualTo(32));
            Assert.That(System.BitConverter.ToUInt32(bytes, 0), Is.EqualTo(3u));
            Assert.That(System.BitConverter.ToSingle(bytes, 4), Is.EqualTo(12f));
            Assert.That(System.BitConverter.ToSingle(bytes, 16), Is.EqualTo(512f));
            Assert.That(System.BitConverter.ToSingle(bytes, 20), Is.EqualTo(-40f));
        }
    }
}
