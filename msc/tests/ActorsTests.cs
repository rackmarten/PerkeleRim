using System.Collections.Generic;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public unsafe class ActorsTests
    {
        static Island AnchoredIsland(uint generation)
        {
            var island = new Island();
            island.SetAnchor(generation, 0x3C, 0, new Vec3(1000f, 2000f, 300f), 0f);
            return island;
        }

        static ActorPose Pose(uint id, float x, float y, float z)
        {
            var a = new ActorPose { id = id, radius = 21f, height = 128f, heading = 1f };
            a.position[0] = x;
            a.position[1] = y;
            a.position[2] = z;
            a.velocity[0] = 70f;  // about 1 m/s east
            return a;
        }

        [Test]
        public void FromListPutsActorsOnTheIsland()
        {
            var island = AnchoredIsland(4);
            var list = new ActorList { generation = 4, count = 2 };
            list.Set(0, Pose(0x14, 1000f, 2000f, 300f));        // on the anchor
            list.Set(1, Pose(0xFF000801, 1070f, 2000f, 370f));  // 1 m east, 1 m up

            var bodies = Actors.FromList(ref list, island);
            Assert.That(bodies.Count, Is.EqualTo(2));
            Assert.That(bodies[0].Id, Is.EqualTo(0x14u));
            Assert.That(bodies[0].Feet.x, Is.EqualTo(Island.Base.x).Within(1e-3f));
            Assert.That(bodies[0].Feet.y, Is.EqualTo(Island.Base.y).Within(1e-3f));
            Assert.That(bodies[1].Feet.x - Island.Base.x, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(bodies[1].Feet.y - Island.Base.y, Is.EqualTo(1.0f).Within(0.01f));
            Assert.That(bodies[0].Radius, Is.EqualTo(0.30f).Within(0.01f));
            Assert.That(bodies[0].Height, Is.EqualTo(1.83f).Within(0.01f));
            Assert.That(bodies[0].Velocity.x, Is.EqualTo(1.0f).Within(0.01f));
        }

        [Test]
        public void FromListIgnoresOtherAnchorsAndEmptySlots()
        {
            var island = AnchoredIsland(4);
            var old = new ActorList { generation = 3, count = 1 };
            old.Set(0, Pose(0x14, 1000f, 2000f, 300f));
            Assert.That(Actors.FromList(ref old, island), Is.Empty);

            var list = new ActorList { generation = 4, count = 40 };  // count past the capacity
            list.Set(0, Pose(0, 1000f, 2000f, 300f));                 // id 0: nothing there
            list.Set(1, Pose(0x14, 1000f, 2000f, 300f));
            Assert.That(Actors.FromList(ref list, island).Count, Is.EqualTo(1));

            Assert.That(Actors.FromList(ref list, new Island()), Is.Empty);
        }

        static ActorBody Body(uint id, float x, float z) =>
            new ActorBody { Id = id, Feet = new Vec3(x, 0f, z), Radius = 0.3f, Height = 1.8f };

        [Test]
        public void PunchLandsOnTheFirstBodyInReach()
        {
            var eye = new Vec3(0f, 1.6f, 0f);
            var north = new Vec3(0f, 0f, 1f);
            var bodies = new List<ActorBody> { Body(1, 0f, 1.5f), Body(2, 0f, 0.8f), Body(3, 3f, 0.8f) };
            float distance;
            Assert.That(Actors.Punch(eye, north, 1.2f, 0.1f, bodies, out distance), Is.EqualTo(1));
            Assert.That(distance, Is.EqualTo(0.8f).Within(0.01f));  // where the fist passes its axis

            // Only the far one ahead: its capsule starts 1.2 m out, a fist at 1.2 m reach touches it.
            bodies.RemoveAt(1);
            Assert.That(Actors.Punch(eye, north, 1.2f, 0.1f, bodies, out distance), Is.EqualTo(0));
            Assert.That(Actors.Punch(eye, north, 1.0f, 0.1f, bodies, out distance), Is.EqualTo(-1));
        }

        [Test]
        public void PunchMissesBesideAboveAndBehind()
        {
            var eye = new Vec3(0f, 1.6f, 0f);
            var bodies = new List<ActorBody> { Body(1, 0f, 0.8f) };
            float distance;
            Assert.That(Actors.Punch(eye, new Vec3(1f, 0f, 0f), 1.2f, 0.1f, bodies, out distance), Is.EqualTo(-1));  // looking east
            Assert.That(Actors.Punch(eye, new Vec3(0f, 0f, -1f), 1.2f, 0.1f, bodies, out distance), Is.EqualTo(-1)); // behind
            Assert.That(Actors.Punch(new Vec3(0f, 3f, 0f), new Vec3(0f, 0f, 1f), 1.2f, 0.1f, bodies, out distance), Is.EqualTo(-1));  // over its head

            // A dwarf-sized body (shorter than its width) is a sphere and still punchable from above.
            var squat = new List<ActorBody> { new ActorBody { Id = 9, Feet = new Vec3(0f, 0f, 0.8f), Radius = 0.5f, Height = 0.6f } };
            var down = new Vec3(0f, -0.8f, 0.6f);
            Assert.That(Actors.Punch(new Vec3(0f, 1.2f, 0f), down, 1.5f, 0.1f, squat, out distance), Is.EqualTo(0));
        }

        [Test]
        public void SegmentDistanceBasics()
        {
            float s, t;
            Assert.That(Actors.SegmentDistance(new Vec3(0, 0, 0), new Vec3(2, 0, 0), new Vec3(1, -1, 1), new Vec3(1, 1, 1), out s, out t), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(s, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(t, Is.EqualTo(0.5f).Within(1e-5f));
            // Parallel segments, and a point against a segment.
            Assert.That(Actors.SegmentDistance(new Vec3(0, 0, 0), new Vec3(1, 0, 0), new Vec3(0, 2, 0), new Vec3(1, 2, 0), out s, out t), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(Actors.SegmentDistance(new Vec3(5, 0, 0), new Vec3(5, 0, 0), new Vec3(0, 0, 0), new Vec3(1, 0, 0), out s, out t), Is.EqualTo(4f).Within(1e-5f));
        }
    }
}
