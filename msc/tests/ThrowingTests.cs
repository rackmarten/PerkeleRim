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
    }
}
