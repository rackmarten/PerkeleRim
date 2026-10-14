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

            public Result Fillet()
            {
                var v = V.ToArray();
                var t = T.ToArray();
                int sampled = t.Length;
                int added = Fillets.Add(ref v, ref t);
                return new Result { V = v, T = t, Added = added, Sampled = sampled };
            }
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
    }
}
