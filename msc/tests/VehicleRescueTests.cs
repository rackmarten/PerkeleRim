using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class VehicleRescueTests
    {
        [Test]
        public void GoesBackWhereItLastStoodWhenThatIsNear()
        {
            Assert.That(FallRescue.Vehicle(true, 12f, 0f, 30f), Is.EqualTo(FallRescue.Pick.Stood));
            Assert.That(FallRescue.Vehicle(true, FallRescue.VehicleReach, 0f, 30f), Is.EqualTo(FallRescue.Pick.Stood));
        }

        [Test]
        public void NeverOntoACliffFarAbove()
        {
            // The surface 70 m up is where the van was put, and Kumpo fell from it.
            Assert.That(FallRescue.Vehicle(false, 0f, 0f, 70f), Is.EqualTo(FallRescue.Pick.Leave));
            Assert.That(FallRescue.Vehicle(true, FallRescue.VehicleReach + 1f, 0f, 70f), Is.EqualTo(FallRescue.Pick.Leave));
        }

        [Test]
        public void WithoutAMemoryTakesTheSurfaceJustAbove()
        {
            Assert.That(FallRescue.Vehicle(false, 0f, 10f, 12f), Is.EqualTo(FallRescue.Pick.Above));
            Assert.That(FallRescue.Vehicle(false, 0f, 10f, 10f + FallRescue.VehicleMaxAbove), Is.EqualTo(FallRescue.Pick.Above));
            Assert.That(FallRescue.Vehicle(false, 0f, 10f, 10f + FallRescue.VehicleMaxAbove + 0.1f), Is.EqualTo(FallRescue.Pick.Leave));
        }

        [Test]
        public void WithNothingAboveItIsLeftAlone()
        {
            Assert.That(FallRescue.Vehicle(false, 0f, 0f, float.NaN), Is.EqualTo(FallRescue.Pick.Leave));
        }

        [Test]
        public void TheCliffRuleIsFarBeyondTheTriggerDepth()
        {
            // The rescue fires when the nearest surface is more than 1.5 m above; a bit more than that
            // must still be a valid "just above".
            Assert.That(FallRescue.VehicleMaxAbove, Is.GreaterThan(1.5f));
        }
    }

    public class ClearSpotTests
    {
        [Test]
        public void StaysWhereItIsWhenThatIsClear()
        {
            var spot = ClearSpot.First(o => true);
            Assert.That(spot.HasValue);
            Assert.That(spot.Value.x, Is.EqualTo(0f));
            Assert.That(spot.Value.z, Is.EqualTo(0f));
        }

        [Test]
        public void TakesTheNearestClearOffsetAheadFirst()
        {
            // Only the first ring is clear, the whole of it: ahead wins.
            var spot = ClearSpot.First(o => o.x * o.x + o.z * o.z > 1f && o.x * o.x + o.z * o.z < 10f);
            Assert.That(spot.HasValue);
            Assert.That(spot.Value.x, Is.EqualTo(0f).Within(1e-4f));
            Assert.That(spot.Value.z, Is.EqualTo(ClearSpot.RingStep).Within(1e-4f));
        }

        [Test]
        public void GoesBehindThenToTheSidesWhenAheadIsBlocked()
        {
            var behind = ClearSpot.First(o => o.x * o.x + o.z * o.z > 1f && o.z < 0f && System.Math.Abs(o.x) < 0.01f);
            Assert.That(behind.Value.z, Is.EqualTo(-ClearSpot.RingStep).Within(1e-4f));
            var right = ClearSpot.First(o => o.x > 0.01f);
            Assert.That(right.Value.x, Is.EqualTo(ClearSpot.RingStep).Within(1e-3f));
            Assert.That(right.Value.z, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void NearerRingsComeBeforeFurtherOnes()
        {
            var tried = ClearSpot.Offsets();
            float last = 0f;
            foreach (var o in tried)
            {
                float d = (float)System.Math.Sqrt(o.x * o.x + o.z * o.z);
                Assert.That(d, Is.GreaterThanOrEqualTo(last - 1e-3f));
                last = d;
            }
            Assert.That(tried.Count, Is.EqualTo(1 + 8 * ClearSpot.Rings));
        }

        [Test]
        public void NothingClearGivesNull()
        {
            Assert.That(ClearSpot.First(o => false).HasValue, Is.False);
        }

        [Test]
        public void GroundIsFlatWithinTheParkingStep()
        {
            Assert.That(ClearSpot.Flat(1f, 1.4f), Is.True);
            Assert.That(ClearSpot.Flat(1f, 1.41f), Is.False);   // a wall top beside the vehicle
        }
    }

    public class VehicleLeaveTests
    {
        [Test]
        public void ATippedVanOrTractorLetsKumpoOutAtAnySpeed()
        {
            float roll60 = (float)System.Math.Cos(61 * System.Math.PI / 180);
            Assert.That(Leaving.Limit(roll60, Upright.MaxTiltCos), Is.EqualTo(Leaving.Anything));   // roll or pitch 61 degrees
            Assert.That(Leaving.Limit(0f, Upright.MaxTiltCos), Is.EqualTo(Leaving.Anything));        // on its side
            Assert.That(Leaving.Limit(-1f, Upright.MaxTiltCos), Is.EqualTo(Leaving.Anything));       // on its roof
        }

        [Test]
        public void AStandingOneLetsKumpoOutAtAWalkingPace()
        {
            float steep = (float)System.Math.Cos(40 * System.Math.PI / 180);
            Assert.That(Leaving.Limit(steep, Upright.MaxTiltCos), Is.EqualTo(Leaving.WalkingSpeed));  // a steep road
            Assert.That(Leaving.Limit(1f, Upright.MaxTiltCos), Is.EqualTo(Leaving.WalkingSpeed));
        }

        [Test]
        public void TheMopedStillCountsAsTippedAt45Degrees()
        {
            float lean50 = (float)System.Math.Cos(50 * System.Math.PI / 180);
            Assert.That(Leaving.Limit(lean50), Is.EqualTo(Leaving.Anything));
            Assert.That(Leaving.Limit(lean50, Upright.MaxTiltCos), Is.EqualTo(Leaving.WalkingSpeed));
        }
    }
}
