using System;
using System.IO;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class CollisionChunkTests
    {
        static Island AnchoredIsland()
        {
            var island = new Island();
            island.SetAnchor(4, 0x3C, 0, new Vec3(20000f, -9000f, -3000f), 0f);
            return island;
        }

        // One grid cell like protocol/cpp/collision.h builds it: corners a (0,0), b east, c north,
        // d north-east, triangles a-b-c and b-d-c, counterclockwise from above in Skyrim.
        static byte[] OneCell(int chunkX, int chunkY, uint generation, float[] origin, uint[] indices = null)
        {
            indices = indices ?? new uint[] { 0, 1, 2, 1, 3, 2 };
            var vertices = new float[] { 0, 0, 0, 64, 0, 0, 0, 64, 0, 64, 64, 35 };
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write(chunkX);
                w.Write(chunkY);
                w.Write(0x3Cu);
                w.Write(generation);
                foreach (var f in origin) w.Write(f);
                w.Write((uint)(vertices.Length / 3));
                w.Write((uint)indices.Length);
                w.Write(0u);
                foreach (var f in vertices) w.Write(f);
                foreach (var i in indices) w.Write(i);
                return ms.ToArray();
            }
        }

        static Vec3 Sub(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);

        // Unity's up component of cross(b - a, c - a).
        static float NormalY(CollisionChunk c, int t)
        {
            var a = c.Vertices[c.Triangles[t * 3]];
            var u = Sub(c.Vertices[c.Triangles[t * 3 + 1]], a);
            var v = Sub(c.Vertices[c.Triangles[t * 3 + 2]], a);
            return u.z * v.x - u.x * v.z;
        }

        [Test]
        public void ParsesIntoIslandSpaceFacingUp()
        {
            var island = AnchoredIsland();
            var payload = OneCell(9, -5, 4, new[] { 20000f, -9000f, -3000f });
            var chunk = CollisionChunk.Parse(payload, payload.Length, island, out var error);

            Assert.That(error, Is.Null);
            Assert.That(chunk.ChunkX, Is.EqualTo(9));
            Assert.That(chunk.ChunkY, Is.EqualTo(-5));
            Assert.That(chunk.Generation, Is.EqualTo(4u));
            Assert.That(chunk.Key, Is.EqualTo(CollisionChunk.KeyOf(9, -5)));
            Assert.That(chunk.Vertices, Has.Length.EqualTo(4));
            Assert.That(chunk.Triangles, Has.Length.EqualTo(6));

            // Vertex 0 is the anchor itself, vertex 3 is 64 east, 64 north and 35 up.
            Assert.That(chunk.Vertices[0].x, Is.EqualTo(Island.Base.x).Within(1e-3));
            Assert.That(chunk.Vertices[0].y, Is.EqualTo(Island.Base.y).Within(1e-3));
            Assert.That(chunk.Vertices[3].x, Is.EqualTo(Island.Base.x + 64 * Coords.MetersPerUnit).Within(1e-3));
            Assert.That(chunk.Vertices[3].y, Is.EqualTo(Island.Base.y + 35 * Coords.MetersPerUnit).Within(1e-3));
            Assert.That(chunk.Vertices[3].z, Is.EqualTo(Island.Base.z + 64 * Coords.MetersPerUnit).Within(1e-3));

            for (int t = 0; t < 2; t++)
                Assert.That(NormalY(chunk, t), Is.GreaterThan(0f), $"triangle {t}");
        }

        [Test]
        public void RejectsPayloadsThatDontMatchTheirHeader()
        {
            var island = AnchoredIsland();
            var payload = OneCell(0, 0, 1, new[] { 0f, 0f, 0f });

            Assert.That(CollisionChunk.Parse(payload, 20, island, out var error), Is.Null);
            Assert.That(error, Does.Contain("shorter than its header"));
            Assert.That(CollisionChunk.Parse(payload, payload.Length - 4, island, out error), Is.Null);
            Assert.That(error, Does.Contain("doesn't match"));

            var bad = OneCell(0, 0, 1, new[] { 0f, 0f, 0f }, new uint[] { 0, 1, 4 });
            Assert.That(CollisionChunk.Parse(bad, bad.Length, island, out error), Is.Null);
            Assert.That(error, Does.Contain("past 4"));
        }

        [Test]
        public void EmptyChunkParses()
        {
            var payload = OneCell(0, 0, 1, new[] { 0f, 0f, 0f }, new uint[0]);
            // Drop the vertices too: header only.
            var header = new byte[40];
            Array.Copy(payload, header, 40);
            header[28] = 0;
            var chunk = CollisionChunk.Parse(header, header.Length, AnchoredIsland(), out var error);
            Assert.That(error, Is.Null);
            Assert.That(chunk.Vertices, Is.Empty);
            Assert.That(chunk.Triangles, Is.Empty);
        }

        [TestCase(0f, 0)]
        [TestCase(2047.9f, 0)]
        [TestCase(2048f, 1)]
        [TestCase(-0.5f, -1)]
        [TestCase(-2048.5f, -2)]
        public void ChunkIndexFloors(float coord, int expected)
        {
            Assert.That(CollisionChunk.ChunkIndex(coord), Is.EqualTo(expected));
        }
    }
}
