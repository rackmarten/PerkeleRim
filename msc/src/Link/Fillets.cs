using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // Ramps over the short steps in a collision chunk (a bridge deck's edge above the road, the land
    // climbing to it, a curb, a hole in the road), so MSC's raycast wheels roll over them instead of
    // the body slamming into them. Skyrim's character controller steps over such lips; the van can't.
    //
    // The chunk's top surface is sampled on a Cell grid. Every deck point (level ground at least
    // about a meter across, so not a fence or railing top) next to a drop lays a RampSlope slope out
    // from itself, and lower ground under that slope is raised to it, as long as the drop from the
    // deck is MinLip..MaxLip. Taller drops (walls, the bridge's sides, cliffs) stay as they are. The
    // raised grid squares become the ramp triangles. How the steps are triangulated doesn't matter.
    // Unity-free, island meters, y up; triangles face up the way Unity expects (cross(b-a, c-a).y > 0).
    public static class Fillets
    {
        public const float MinLip = 0.03f;
        public const float MaxLip = 0.8f;  // a Whiterun bridge's deck edge is 0.73 m
        public const float Cell = 0.25f;
        const float RampSlope = 0.25f;     // rise over run, ~14 degrees
        const float SurfaceY = 0.1f;       // normal.y above this: part of the top surface
        const float DeckTolerance = 0.15f;
        const int DeckReach = 2;           // a deck point is level with most points this many cells around
        const int DeckLevel = 12;          // ... at least this many of the 25
        const float Raised = 0.02f;

        // Appends the ramps to vertices and triangles; returns how many triangles it added. why, if
        // given, hears about every point at the foot of a step: where it is and what became of it.
        public static int Add(ref Vec3[] vertices, ref int[] triangles, Action<Vec3, string> why = null)
        {
            if (triangles.Length < 3) return 0;
            float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
            foreach (var p in vertices)
            {
                minX = Math.Min(minX, p.x); maxX = Math.Max(maxX, p.x);
                minZ = Math.Min(minZ, p.z); maxZ = Math.Max(maxZ, p.z);
            }
            int nx = (int)Math.Floor((maxX - minX) / Cell) + 1, nz = (int)Math.Floor((maxZ - minZ) / Cell) + 1;
            if (nx < 2 || nz < 2 || (long)nx * nz > 4000000) return 0;

            var h = Sample(vertices, triangles, minX, minZ, nx, nz);
            var deck = new bool[nx * nz];
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    deck[i * nz + j] = IsDeck(h, nx, nz, i, j);

            // Lay the slope out from every deck point with a drop next to it.
            var f = (float[])h.Clone();
            int reach = (int)Math.Ceiling(MaxLip / RampSlope / Cell);
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    int s = i * nz + j;
                    if (!deck[s] || !HasDrop(h, nx, nz, i, j)) continue;
                    float top = h[s];
                    for (int a = Math.Max(0, i - reach); a <= Math.Min(nx - 1, i + reach); a++)
                        for (int b = Math.Max(0, j - reach); b <= Math.Min(nz - 1, j + reach); b++)
                        {
                            int t = a * nz + b;
                            float drop = top - h[t];
                            if (float.IsNaN(drop) || drop < MinLip || drop > MaxLip) continue;
                            float run = (float)Math.Sqrt((a - i) * (a - i) + (b - j) * (b - j)) * Cell;
                            float y = top - run * RampSlope;
                            if (y > f[t]) f[t] = y;
                        }
                }

            if (why != null) Explain(h, f, deck, nx, nz, minX, minZ, why);

            // Grid squares with a raised corner become the ramp.
            var newVertices = new List<Vec3>();
            var newTriangles = new List<int>();
            var index = new Dictionary<int, int>();
            int baseIndex = vertices.Length;
            for (int i = 0; i + 1 < nx; i++)
                for (int j = 0; j + 1 < nz; j++)
                {
                    int c00 = i * nz + j, c10 = c00 + nz, c01 = c00 + 1, c11 = c10 + 1;
                    if (float.IsNaN(h[c00]) || float.IsNaN(h[c10]) || float.IsNaN(h[c01]) || float.IsNaN(h[c11])) continue;
                    if (f[c00] - h[c00] < Raised && f[c10] - h[c10] < Raised && f[c01] - h[c01] < Raised && f[c11] - h[c11] < Raised) continue;
                    int v00 = Vertex(c00), v10 = Vertex(c10), v01 = Vertex(c01), v11 = Vertex(c11);
                    // (x, z), (x, z+1), (x+1, z+1) is clockwise from above: faces up in Unity.
                    newTriangles.Add(v00); newTriangles.Add(v01); newTriangles.Add(v11);
                    newTriangles.Add(v00); newTriangles.Add(v11); newTriangles.Add(v10);
                }

            if (newTriangles.Count == 0) return 0;
            var v = new Vec3[vertices.Length + newVertices.Count];
            Array.Copy(vertices, v, vertices.Length);
            newVertices.CopyTo(v, vertices.Length);
            var tr = new int[triangles.Length + newTriangles.Count];
            Array.Copy(triangles, tr, triangles.Length);
            newTriangles.CopyTo(tr, triangles.Length);
            vertices = v;
            triangles = tr;
            return newTriangles.Count / 3;

            int Vertex(int c)
            {
                int k;
                if (index.TryGetValue(c, out k)) return k;
                k = baseIndex + newVertices.Count;
                newVertices.Add(new Vec3(minX + (c / nz) * Cell, f[c], minZ + (c % nz) * Cell));
                index[c] = k;
                return k;
            }
        }

        // The highest up-facing surface at each grid point, NaN where there is none.
        static float[] Sample(Vec3[] v, int[] t, float minX, float minZ, int nx, int nz)
        {
            var h = new float[nx * nz];
            for (int i = 0; i < h.Length; i++) h[i] = float.NaN;
            for (int k = 0; k + 2 < t.Length; k += 3)
            {
                Vec3 a = v[t[k]], b = v[t[k + 1]], c = v[t[k + 2]];
                var n = Normal(a, b, c);
                if (float.IsNaN(n.y) || n.y < SurfaceY) continue;
                float d = (b.z - c.z) * (a.x - c.x) + (c.x - b.x) * (a.z - c.z);
                if (Math.Abs(d) < 1e-9f) continue;
                int i0 = Math.Max(0, (int)Math.Ceiling((Math.Min(a.x, Math.Min(b.x, c.x)) - minX) / Cell - 1e-4f));
                int i1 = Math.Min(nx - 1, (int)Math.Floor((Math.Max(a.x, Math.Max(b.x, c.x)) - minX) / Cell + 1e-4f));
                int j0 = Math.Max(0, (int)Math.Ceiling((Math.Min(a.z, Math.Min(b.z, c.z)) - minZ) / Cell - 1e-4f));
                int j1 = Math.Min(nz - 1, (int)Math.Floor((Math.Max(a.z, Math.Max(b.z, c.z)) - minZ) / Cell + 1e-4f));
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        float x = minX + i * Cell, z = minZ + j * Cell;
                        float w1 = ((b.z - c.z) * (x - c.x) + (c.x - b.x) * (z - c.z)) / d;
                        float w2 = ((c.z - a.z) * (x - c.x) + (a.x - c.x) * (z - c.z)) / d;
                        float w3 = 1 - w1 - w2;
                        const float e = -1e-4f;
                        if (w1 < e || w2 < e || w3 < e) continue;
                        float y = w1 * a.y + w2 * b.y + w3 * c.y;
                        int s = i * nz + j;
                        if (float.IsNaN(h[s]) || y > h[s]) h[s] = y;
                    }
            }
            return h;
        }

        static bool IsDeck(float[] h, int nx, int nz, int i, int j)
        {
            float y = h[i * nz + j];
            if (float.IsNaN(y)) return false;
            int level = 0;
            for (int a = i - DeckReach; a <= i + DeckReach; a++)
                for (int b = j - DeckReach; b <= j + DeckReach; b++)
                    if (a >= 0 && a < nx && b >= 0 && b < nz && Math.Abs(h[a * nz + b] - y) <= DeckTolerance) level++;
            return level >= DeckLevel;
        }

        // A neighbor more than MinLip lower than the ramp would be there: a step, not just a slope
        // (ground falling gentler than the ramp never needs one).
        static bool HasDrop(float[] h, int nx, int nz, int i, int j)
        {
            float y = h[i * nz + j];
            for (int a = Math.Max(0, i - 1); a <= Math.Min(nx - 1, i + 1); a++)
                for (int b = Math.Max(0, j - 1); b <= Math.Min(nz - 1, j + 1); b++)
                {
                    float run = (a != i && b != j ? 1.4142f : 1f) * Cell;
                    if (y - h[a * nz + b] > MinLip + run * RampSlope) return true;
                }
            return false;
        }

        // Every point with a step of MinLip or more up to a neighbor: raised, or why not.
        static void Explain(float[] h, float[] f, bool[] deck, int nx, int nz, float minX, float minZ, Action<Vec3, string> why)
        {
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    int s = i * nz + j;
                    if (float.IsNaN(h[s])) continue;
                    float up = 0;
                    bool deckAbove = false;
                    for (int a = Math.Max(0, i - 1); a <= Math.Min(nx - 1, i + 1); a++)
                        for (int b = Math.Max(0, j - 1); b <= Math.Min(nz - 1, j + 1); b++)
                        {
                            float d = h[a * nz + b] - h[s];
                            if (d > up) { up = d; deckAbove = deck[a * nz + b]; }
                        }
                    if (up < MinLip) continue;
                    string what = f[s] - h[s] >= Raised ? $"raised {f[s] - h[s]:0.00} m"
                        : up > MaxLip ? "not raised, step too tall"
                        : deckAbove ? "not raised" : "not raised, no deck above (narrow or sloped)";
                    why(new Vec3(minX + i * Cell, h[s], minZ + j * Cell), $"step up {up:0.00} m: {what}");
                }
        }

        // Unit normal, NaN for a degenerate triangle.
        internal static Vec3 Normal(Vec3 a, Vec3 b, Vec3 c)
        {
            float ux = b.x - a.x, uy = b.y - a.y, uz = b.z - a.z;
            float vx = c.x - a.x, vy = c.y - a.y, vz = c.z - a.z;
            float nx = uy * vz - uz * vy, ny = uz * vx - ux * vz, nz = ux * vy - uy * vx;
            float l = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
            return l < 1e-9f ? new Vec3(float.NaN, float.NaN, float.NaN) : new Vec3(nx / l, ny / l, nz / l);
        }
    }
}
