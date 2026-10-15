using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // Ramps over the short steps in a collision chunk (a bridge deck's edge above the road, the land
    // climbing to it, a curb, a hole in the road), so MSC's raycast wheels roll over them instead of
    // the body slamming into them. Skyrim's character controller steps over such lips; the van can't.
    //
    // The chunk's top surface is sampled on a Cell grid. Every deck point (level ground at least
    // about a meter across, so not a fence or railing top) next to a step of MinLip..MaxLip (measured
    // down to the step's foot, where the ground stops falling steeper than StepSlope) raises the
    // ground around it by the step's height, less RampSlope per meter out from the deck, and never
    // above the deck's own RampSlope slope. So on flat ground the ramp is a plain 1 in 5, and where
    // the ground beyond falls away (a lip at the top of a hill) the ramp follows it down and fades
    // out instead of ending in a shelf. Taller steps (walls, the bridge's sides, cliffs) stay as
    // they are. The raised grid squares become the ramp triangles. How the steps are triangulated
    // doesn't matter.
    // Only Skyrim's roads get ramps (navmesh triangles flagged preferred, see CollisionChunk.Roads):
    // a deck point lays a slope only when it and its four neighbors are inside road triangles, and
    // a point it raises must be within RoadMargin of one, so stones, rocks and railings beside the
    // road stay obstacles.
    // Unity-free, island meters, y up; triangles face up the way Unity expects (cross(b-a, c-a).y > 0).
    public static class Fillets
    {
        public const float MinLip = 0.03f;
        public const float MaxLip = 0.8f;  // a Whiterun bridge's deck edge is 0.73 m
        public const float Cell = 0.25f;
        public const float RoadMargin = 0.75f;
        const float RampSlope = 0.2f;      // rise over run, ~11 degrees
        const float StepSlope = 0.5f;      // ground falling steeper than this (~27 degrees) is the step's face
        const float SurfaceY = 0.1f;       // normal.y above this: part of the top surface
        const float DeckTolerance = 0.15f;
        const int DeckReach = 2;           // a deck point is level with most points this many cells around
        const int DeckLevel = 12;          // ... at least this many of the 25
        const float Raised = 0.02f;

        const byte NearRoad = 1, OnRoad = 2;

        // Appends the ramps to vertices and triangles; returns how many triangles it added. roads are
        // the chunk's road triangles (x, z of three corners each); none, no ramps. why, if given,
        // hears about every point at the foot of a step: where it is and what became of it.
        public static int Add(ref Vec3[] vertices, ref int[] triangles, float[] roads, Action<Vec3, string> why = null)
        {
            if (triangles.Length < 3) return 0;
            if ((roads == null || roads.Length < 6) && why == null) return 0;
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
            var road = RoadMask(roads, minX, minZ, nx, nz);

            // Raise the ground around every deck point with a step next to it.
            var f = (float[])h.Clone();
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    int s = i * nz + j;
                    if (!deck[s] || !RoadAround(road, nx, nz, i, j) || !HasDrop(h, nx, nz, i, j)) continue;
                    float top = h[s], lip = Lip(h, nx, nz, i, j);
                    if (lip < MinLip) continue;
                    int reach = (int)Math.Ceiling(lip / RampSlope / Cell);
                    for (int a = Math.Max(0, i - reach); a <= Math.Min(nx - 1, i + reach); a++)
                        for (int b = Math.Max(0, j - reach); b <= Math.Min(nz - 1, j + reach); b++)
                        {
                            int t = a * nz + b;
                            if (float.IsNaN(h[t]) || road[t] == 0) continue;
                            float run = (float)Math.Sqrt((a - i) * (a - i) + (b - j) * (b - j)) * Cell;
                            float y = Math.Min(top - run * RampSlope, h[t] + lip - run * RampSlope);
                            if (y > f[t]) f[t] = y;
                        }
                }

            if (why != null) Explain(h, f, deck, road, nx, nz, minX, minZ, why);

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

        // OnRoad for grid points inside a road triangle, NearRoad within RoadMargin of one, else 0.
        static byte[] RoadMask(float[] roads, float minX, float minZ, int nx, int nz)
        {
            var mask = new byte[nx * nz];
            if (roads == null) return mask;
            for (int k = 0; k + 5 < roads.Length; k += 6)
            {
                float ax = roads[k], az = roads[k + 1], bx = roads[k + 2], bz = roads[k + 3], cx = roads[k + 4], cz = roads[k + 5];
                int i0 = Math.Max(0, (int)Math.Ceiling((Math.Min(ax, Math.Min(bx, cx)) - RoadMargin - minX) / Cell));
                int i1 = Math.Min(nx - 1, (int)Math.Floor((Math.Max(ax, Math.Max(bx, cx)) + RoadMargin - minX) / Cell));
                int j0 = Math.Max(0, (int)Math.Ceiling((Math.Min(az, Math.Min(bz, cz)) - RoadMargin - minZ) / Cell));
                int j1 = Math.Min(nz - 1, (int)Math.Floor((Math.Max(az, Math.Max(bz, cz)) + RoadMargin - minZ) / Cell));
                for (int i = i0; i <= i1; i++)
                    for (int j = j0; j <= j1; j++)
                    {
                        int s = i * nz + j;
                        if (mask[s] == OnRoad) continue;
                        float x = minX + i * Cell, z = minZ + j * Cell;
                        if (InTriangle(x, z, ax, az, bx, bz, cx, cz)) mask[s] = OnRoad;
                        else if (Math.Min(SegmentDistance(x, z, ax, az, bx, bz), Math.Min(SegmentDistance(x, z, bx, bz, cx, cz), SegmentDistance(x, z, cx, cz, ax, az))) <= RoadMargin)
                            mask[s] = NearRoad;
                    }
            }
            return mask;
        }

        // The point and its four neighbors are inside road triangles: a railing top whose foot is the
        // road's edge is not road, though its edge point is on the road's boundary.
        static bool RoadAround(byte[] road, int nx, int nz, int i, int j)
        {
            int s = i * nz + j;
            return road[s] == OnRoad && (i == 0 || road[s - nz] == OnRoad) && (i == nx - 1 || road[s + nz] == OnRoad)
                && (j == 0 || road[s - 1] == OnRoad) && (j == nz - 1 || road[s + 1] == OnRoad);
        }

        // Whether (x, z) is inside one of the road triangles (x, z of three corners each).
        public static bool InRoad(float[] roads, float x, float z)
        {
            if (roads == null) return false;
            for (int k = 0; k + 5 < roads.Length; k += 6)
                if (InTriangle(x, z, roads[k], roads[k + 1], roads[k + 2], roads[k + 3], roads[k + 4], roads[k + 5])) return true;
            return false;
        }

        // Either winding; the edges count as inside.
        static bool InTriangle(float x, float z, float ax, float az, float bx, float bz, float cx, float cz)
        {
            float d1 = (bx - ax) * (z - az) - (bz - az) * (x - ax);
            float d2 = (cx - bx) * (z - bz) - (cz - bz) * (x - bx);
            float d3 = (ax - cx) * (z - cz) - (az - cz) * (x - cx);
            const float e = 1e-6f;
            return (d1 >= -e && d2 >= -e && d3 >= -e) || (d1 <= e && d2 <= e && d3 <= e);
        }

        static float SegmentDistance(float x, float z, float ax, float az, float bx, float bz)
        {
            float ux = bx - ax, uz = bz - az, l = ux * ux + uz * uz;
            float t = l < 1e-12f ? 0 : Math.Max(0, Math.Min(1, ((x - ax) * ux + (z - az) * uz) / l));
            float dx = x - (ax + t * ux), dz = z - (az + t * uz);
            return (float)Math.Sqrt(dx * dx + dz * dz);
        }

        static bool IsDeck(float[] h, int nx, int nz, int i, int j)
        {
            float y = h[i * nz + j];
            if (float.IsNaN(y)) return false;
            int level = 0;
            for (int a = i - DeckReach; a <= i + DeckReach; a++)
                for (int b = j - DeckReach; b <= j + DeckReach; b++)
                    if (a >= 0 && a < nx && b >= 0 && b < nz && Math.Abs(h[a * nz + b] - y) <= DeckTolerance) level++;
            if (level < DeckLevel) return false;

            // Flatter than the ramp on at least one side along each axis: a deck's edge has its drop
            // on one side only, while a hill steeper than the ramp falls away on both and is no deck
            // (otherwise every cell of it is a "step" and lays a shelf down the hill).
            float sx = SideSlope(h, y, i > DeckReach - 1 ? (i - DeckReach) * nz + j : -1, i + DeckReach < nx ? (i + DeckReach) * nz + j : -1);
            float sz = SideSlope(h, y, j > DeckReach - 1 ? i * nz + j - DeckReach : -1, j + DeckReach < nz ? i * nz + j + DeckReach : -1);
            return sx * sx + sz * sz <= RampSlope * RampSlope;
        }

        // The gentler of the slopes from y to the points before and after it, DeckReach cells away.
        static float SideSlope(float[] h, float y, int before, int after)
        {
            float b = before < 0 || float.IsNaN(h[before]) ? float.NaN : Math.Abs(y - h[before]);
            float a = after < 0 || float.IsNaN(h[after]) ? float.NaN : Math.Abs(h[after] - y);
            float d = float.IsNaN(b) ? a : float.IsNaN(a) ? b : Math.Min(a, b);
            return float.IsNaN(d) ? 0 : d / (DeckReach * Cell);
        }

        // The height of the tallest step of at most MaxLip down from (i, j): along each of the eight
        // directions, the drop to where the ground stops falling steeper than StepSlope. A taller
        // step that way is a wall and doesn't count; 0 if there is none.
        static float Lip(float[] h, int nx, int nz, int i, int j)
        {
            float top = h[i * nz + j], lip = 0;
            int limit = (int)Math.Ceiling(MaxLip / StepSlope / Cell) + 1;
            for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                {
                    if (di == 0 && dj == 0) continue;
                    float run = (di != 0 && dj != 0 ? 1.4142f : 1f) * Cell, prev = top;
                    for (int k = 1; k <= limit; k++)
                    {
                        int a = i + k * di, b = j + k * dj;
                        if (a < 0 || a >= nx || b < 0 || b >= nz) break;
                        float y = h[a * nz + b];
                        if (float.IsNaN(y) || prev - y <= StepSlope * run) break;
                        prev = y;
                    }
                    float drop = top - prev;
                    if (drop <= MaxLip && drop > lip) lip = drop;
                }
            return lip;
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
        static void Explain(float[] h, float[] f, bool[] deck, byte[] road, int nx, int nz, float minX, float minZ, Action<Vec3, string> why)
        {
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                {
                    int s = i * nz + j;
                    if (float.IsNaN(h[s])) continue;
                    float up = 0;
                    bool deckAbove = false, roadAbove = false;
                    for (int a = Math.Max(0, i - 1); a <= Math.Min(nx - 1, i + 1); a++)
                        for (int b = Math.Max(0, j - 1); b <= Math.Min(nz - 1, j + 1); b++)
                        {
                            float d = h[a * nz + b] - h[s];
                            if (d > up) { up = d; deckAbove = deck[a * nz + b]; roadAbove = RoadAround(road, nx, nz, a, b); }
                        }
                    if (up < MinLip) continue;
                    string what = f[s] - h[s] >= Raised ? $"raised {f[s] - h[s]:0.00} m"
                        : up > MaxLip ? "not raised, step too tall"
                        : road[s] == 0 || !roadAbove ? "not raised, not road"
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
