using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class RunOverTests
    {
        // A van-sized box in its own space: 2 m wide, 2 m tall from 0.3 m up, 4.5 m long.
        static readonly Vec3 Min = new Vec3(-1f, 0.3f, -2.25f);
        static readonly Vec3 Max = new Vec3(1f, 2.3f, 2.25f);

        static bool Person(float x, float y, float z) =>
            RunOver.Touches(Min, Max, new Vec3(x, y + 0.3f, z), new Vec3(x, y + 1.5f, z), 0.3f);

        [Test]
        public void ACapsuleInFrontOrInsideIsHit()
        {
            Assert.That(Person(0f, 0f, 2.5f), Is.True);   // just in front of the bumper
            Assert.That(Person(0f, 0f, 0f), Is.True);     // inside
            Assert.That(Person(1.2f, 0f, 0f), Is.True);   // brushed by the side
        }

        [Test]
        public void ACapsuleBesideOrUnderIsNot()
        {
            Assert.That(Person(0f, 0f, 3f), Is.False);    // a step ahead
            Assert.That(Person(1.5f, 0f, 0f), Is.False);  // beside it
            Assert.That(Person(0f, 3f, 0f), Is.False);    // on a bridge above
        }

        [Test]
        public void AShortCapsuleIsSampledAtLeastAtItsEnds()
        {
            Assert.That(RunOver.Touches(Min, Max, new Vec3(0f, 0.2f, 2.3f), new Vec3(0f, 0.2f, 2.3f), 0.1f), Is.True);
            Assert.That(RunOver.Touches(Min, Max, new Vec3(0f, -1f, 0f), new Vec3(0f, -1f, 0f), 0.1f), Is.False);
        }

        [Test]
        public void OnlyHugeActorsAreSolidToVehicles()
        {
            Assert.That(RunOver.Solid(new ActorBody { Mass = 80f }), Is.False);   // a guard
            Assert.That(RunOver.Solid(new ActorBody { Mass = 470f }), Is.False);  // a cow
            Assert.That(RunOver.Solid(new ActorBody { Mass = 631f }), Is.False);  // an elk
            Assert.That(RunOver.Solid(new ActorBody { Mass = 2000f }), Is.True);  // a dragon
        }

        [Test]
        public void OneActorIsRunOverOncePerIgnoreWindow()
        {
            var pacer = new HitPacer(RunOver.IgnoreSeconds);
            Assert.That(pacer.Ready(1, 10f), Is.True);
            Assert.That(pacer.Ready(1, 10.1f), Is.False);  // still touching the next frame
            Assert.That(pacer.Ready(1, 10f + RunOver.IgnoreSeconds - 0.01f), Is.False);
            Assert.That(pacer.Ready(2, 10.1f), Is.True);   // someone else in the way
            Assert.That(pacer.Ready(1, 10f + RunOver.IgnoreSeconds), Is.True);
        }
    }
}
