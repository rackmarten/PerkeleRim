using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace PerkeleRim.Tests
{
    public class FilletsTests
    {
        // A mesh made of axis-aligned quads, each as two triangles facing up (walls either way) the
        // way the island's chunks do.
        sealed class Mesh
        {
            public readonly List<Vec3> V = new List<Vec3>();
            public readonly List<int> T = new List<int>();

            // Flat ground at height y over x0..x1, z0..z1, facing up.
            public Mesh Floor(float x0, float x1, float z0, float z1, float y) => Slope(x0, x1, z0, z1, y, y);

            // Ground rising along x from y0 at x0 to y1 at x1, facing up.
            public Mesh Slope(float x0, float x1, float z0, float z1, float y0, float y1)
            {
                int i = V.Count;
                V.Add(new Vec3(x0, y0, z0)); V.Add(new Vec3(x0, y0, z1)); V.Add(new Vec3(x1, y1, z1)); V.Add(new Vec3(x1, y1, z0));
                T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                return this;
            }

            // A vertical wall in the plane x = x, from y0 up to y1 over z0..z1.
            public Mesh Wall(float x, float z0, float z1, float y0, float y1)
            {
                int i = V.Count;
                V.Add(new Vec3(x, y0, z0)); V.Add(new Vec3(x, y1, z0)); V.Add(new Vec3(x, y1, z1)); V.Add(new Vec3(x, y0, z1));
                T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                return this;
            }

            // Everything on road unless roads says otherwise.
            public Result Fillet(float[] roads = null, List<string> why = null)
            {
                var v = V.ToArray();
                var t = T.ToArray();
                int sampled = t.Length;
                Action<Vec3, string> explain = null;
                if (why != null) explain = (at, reason) => why.Add($"{at.x:0.00} {at.z:0.00}: {reason}");
                int added = Fillets.Add(ref v, ref t, roads ?? Road(-20, 20, -20, 20), explain);
                return new Result { V = v, T = t, Added = added, Sampled = sampled };
            }
        }

        // A road rectangle over x0..x1, z0..z1 as two triangles (x, z corners).
        static float[] Road(float x0, float x1, float z0, float z1) =>
            new[] { x0, z0, x1, z0, x1, z1, x0, z0, x1, z1, x0, z1 };

        static float[] Roads(params float[][] parts)
        {
            var all = new List<float>();
            foreach (var p in parts) all.AddRange(p);
            return all.ToArray();
        }

        sealed class Result
        {
            public Vec3[] V;
            public int[] T;
            public int Added, Sampled;

            // The highest up-facing surface at (x, z), the ramps included.
            public float HeightAt(float x, float z)
            {
                float best = float.NaN;
                for (int k = 0; k + 2 < T.Length; k += 3)
                {
                    Vec3 a = V[T[k]], b = V[T[k + 1]], c = V[T[k + 2]];
                    if (Fillets.Normal(a, b, c).y < 0.1f) continue;
                    float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                    float w1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                    float w2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                    if (w1 < -1e-4f || w2 < -1e-4f || 1 - w1 - w2 < -1e-4f) continue;
                    float y = w1 * a.y + w2 * b.y + (1 - w1 - w2) * c.y;
                    if (float.IsNaN(best) || y > best) best = y;
                }
                return best;
            }

            public void RampsFaceUp()
            {
                for (int k = Sampled; k < T.Length; k += 3)
                    Assert.That(Fillets.Normal(V[T[k]], V[T[k + 1]], V[T[k + 2]]).y, Is.GreaterThan(0.5f), "ramp faces up");
            }
        }

        // Road at 0 for x < 0, a deck `lip` higher for x > 0, its edge a face down into the ground.
        static Mesh Step(float lip) =>
            new Mesh().Floor(-6, 0, -3, 3, 0).Floor(0, 6, -3, 3, lip).Wall(0, -3, 3, -0.5f, lip);

        [Test]
        public void A_short_lip_gets_a_1_in_4_ramp_onto_the_lower_side()
        {
            var r = Step(0.3f).Fillet();

            Assert.That(r.Added, Is.GreaterThan(0));
            r.RampsFaceUp();
            Assert.That(r.HeightAt(-0.6f, 0), Is.EqualTo(0.15f).Within(0.03f));
            Assert.That(r.HeightAt(-1.5f, 0), Is.EqualTo(0f).Within(0.001f), "the road beyond the ramp");
            Assert.That(r.HeightAt(2f, 0), Is.EqualTo(0.3f).Within(0.001f), "the deck");
        }

        [Test]
        public void A_bridge_deck_edge_of_0_73_m_gets_a_ramp()
        {
            var r = Step(0.73f).Fillet();
            Assert.That(r.HeightAt(-1.4f, 0), Is.EqualTo(0.38f).Within(0.04f));
        }

        [Test]
        public void A_steep_irregular_strip_up_to_a_deck_gets_a_ramp()
        {
            // Road at 0, a 45 degree strip up to the deck at 0.7, cut into triangles of mixed heights.
            var m = new Mesh().Floor(-6, -0.7f, -3, 3, 0).Floor(0, 6, -3, 3, 0.7f);
            int i = m.V.Count;
            m.V.Add(new Vec3(-0.7f, 0, -3)); m.V.Add(new Vec3(-0.7f, 0, 3)); m.V.Add(new Vec3(0, 0.7f, 3));
            m.V.Add(new Vec3(0, 0.7f, -3)); m.V.Add(new Vec3(-0.35f, 0.35f, 0.4f));
            m.T.AddRange(new[] { i, i + 1, i + 4, i + 1, i + 2, i + 4, i + 2, i + 3, i + 4, i + 3, i, i + 4 });

            var r = m.Fillet();
            r.RampsFaceUp();
            Assert.That(r.HeightAt(-1.4f, 0), Is.EqualTo(0.35f).Within(0.04f), "half way down the ramp, above the road");
            Assert.That(r.HeightAt(-0.35f, 0), Is.GreaterThan(0.55f), "over the strip");
        }

        [Test]
        public void A_hole_in_the_road_gets_filled_from_its_rims()
        {
            var m = new Mesh().Floor(-6, -0.5f, -3, 3, 0).Floor(0.5f, 6, -3, 3, 0).Floor(-0.5f, 0.5f, -3, 3, -0.3f)
                .Wall(-0.5f, -3, 3, -0.3f, 0).Wall(0.5f, -3, 3, -0.3f, 0);
            var r = m.Fillet();
            Assert.That(r.HeightAt(0, 0), Is.GreaterThan(-0.2f));
        }

        [Test]
        public void Walls_taller_than_a_lip_stay_walls()
        {
            var r = Step(1.5f).Fillet();
            Assert.That(r.HeightAt(-0.3f, 0), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void Ground_gentler_than_the_ramp_gets_nothing()
        {
            Assert.That(new Mesh().Floor(-6, 6, -3, 3, 0).Fillet().Added, Is.EqualTo(0));
            Assert.That(new Mesh().Slope(-6, 6, -3, 3, 0, 2f).Fillet().Added, Is.EqualTo(0));
        }

        [Test]
        public void A_low_fence_or_railing_stays_as_it_is()
        {
            var fence = new Mesh().Floor(-6, 6, -3, 3, 0)
                .Wall(0, -3, 3, 0, 0.4f).Wall(0.1f, -3, 3, 0, 0.4f).Floor(0, 0.1f, -3, 3, 0.4f);
            var r = fence.Fillet();
            Assert.That(r.HeightAt(-0.4f, 0), Is.EqualTo(0f).Within(0.001f));
            Assert.That(r.HeightAt(0.5f, 0), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void A_deck_underside_is_not_ground()
        {
            // A deck 0.3 thick hanging over nothing: no road below to ramp onto.
            var m = new Mesh().Floor(0, 6, -3, 3, 0.3f).Wall(0, -3, 3, 0, 0.3f);
            int i = m.V.Count;  // the underside, facing down
            m.V.Add(new Vec3(-3, 0, -3)); m.V.Add(new Vec3(6, 0, -3)); m.V.Add(new Vec3(6, 0, 3)); m.V.Add(new Vec3(-3, 0, 3));
            m.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
            Assert.That(m.Fillet().Added, Is.EqualTo(0));
        }

        [Test]
        public void The_same_step_off_road_gets_no_ramp()
        {
            var why = new List<string>();
            var r = Step(0.3f).Fillet(new float[0], why);
            Assert.That(r.Added, Is.EqualTo(0));
            Assert.That(r.HeightAt(-0.6f, 0), Is.EqualTo(0f).Within(0.001f));
            Assert.That(why, Has.Some.Contains("not road"));

            // A road somewhere else in the chunk doesn't help.
            Assert.That(Step(0.3f).Fillet(Road(-6, -4, -3, 3)).Added, Is.EqualTo(0));
        }

        [Test]
        public void A_step_across_a_road_is_ramped_only_where_the_road_is()
        {
            // The road crosses the step along x for z in -1..1; the rest of the step is off road.
            var r = Step(0.3f).Fillet(Road(-6, 6, -1, 1));
            Assert.That(r.Added, Is.GreaterThan(0));
            Assert.That(r.HeightAt(-0.6f, 0), Is.EqualTo(0.15f).Within(0.03f), "on the road");
            Assert.That(r.HeightAt(-0.6f, 2.5f), Is.EqualTo(0f).Within(0.001f), "beside the road");
        }

        [Test]
        public void A_railing_top_beside_the_road_gets_no_ramp()
        {
            // A deck road at 0 over z -3..1, a wide stone railing 0.5 m higher over z 1..3, the road
            // stopping at its foot. The railing top is level and wide, so it would be a deck.
            var m = new Mesh().Floor(-6, 6, -3, 1, 0).Floor(-6, 6, 1, 3, 0.5f);
            int i = m.V.Count;  // the railing's face, z = 1, facing the road
            m.V.Add(new Vec3(-6, 0, 1)); m.V.Add(new Vec3(6, 0, 1)); m.V.Add(new Vec3(6, 0.5f, 1)); m.V.Add(new Vec3(-6, 0.5f, 1));
            m.T.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });

            var why = new List<string>();
            var r = m.Fillet(Road(-6, 6, -3, 1), why);
            Assert.That(r.Added, Is.EqualTo(0));
            Assert.That(r.HeightAt(0, 0.6f), Is.EqualTo(0f).Within(0.001f), "the road at the railing's foot");
            Assert.That(why, Has.Some.Contains("not road"));

            // The same railing with the road over it would get one, so it is the road that stops it.
            Assert.That(m.Fillet(Road(-6, 6, -3, 3)).Added, Is.GreaterThan(0));
        }

        [Test]
        public void Points_within_the_margin_of_a_road_are_still_raised()
        {
            // The road crosses the step for z in -1..1: the ramp spreads up to RoadMargin past its side.
            var r = Step(0.3f).Fillet(Road(-6, 6, -1, 1));
            Assert.That(r.HeightAt(-0.25f, 1.25f), Is.GreaterThan(0.1f), "0.25 m beside the road");
            Assert.That(r.HeightAt(-0.25f, 1.75f), Is.EqualTo(0f).Within(0.001f), "0.75 m beside the road");

            // A deck edge outside the road lays no ramp of its own.
            Assert.That(Step(0.3f).Fillet(Road(0.6f, 6, -3, 3)).Added, Is.EqualTo(0));
        }

        [Test]
        public void InRoad_takes_either_winding()
        {
            var cw = new float[] { 0, 0, 0, 1, 1, 0 };
            var ccw = new float[] { 0, 0, 1, 0, 0, 1 };
            Assert.That(Fillets.InRoad(cw, 0.2f, 0.2f), Is.True);
            Assert.That(Fillets.InRoad(ccw, 0.2f, 0.2f), Is.True);
            Assert.That(Fillets.InRoad(ccw, 0.8f, 0.8f), Is.False);
            Assert.That(Fillets.InRoad(null, 0, 0), Is.False);
        }
    }
}
