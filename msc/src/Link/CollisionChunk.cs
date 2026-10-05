using System;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // A CollisionChunk message (built by protocol/cpp/collision.h) turned into collision island
    // geometry: vertices in Unity meters on the island, triangles wound the way Unity expects.
    // Unity-free; PlayerLink makes the Mesh.
    public sealed unsafe class CollisionChunk
    {
        public int ChunkX;
        public int ChunkY;
        public uint Worldspace;
        public uint Generation;
        public Vec3[] Vertices;
        public int[] Triangles;

        public long Key => KeyOf(ChunkX, ChunkY);

        public static long KeyOf(int x, int y) => ((long)x << 32) | (uint)y;

        // Chunk index of a Skyrim world coordinate (X or Y), same as Collision::ChunkIndex.
        public static int ChunkIndex(float coord) => (int)Math.Floor(coord / (float)Wire.CollisionChunkUnits);

        // Null (with a reason) when the payload doesn't hold what its header says.
        public static CollisionChunk Parse(byte[] payload, int size, Island island, out string error)
        {
            error = null;
            if (size < sizeof(CollisionChunkHeader)) { error = "payload of " + size + " bytes is shorter than its header"; return null; }

            fixed (byte* p = payload)
            {
                var h = *(CollisionChunkHeader*)p;
                long vertexBytes = (long)h.vertexCount * 12, indexBytes = (long)h.indexCount * 4;
                if (sizeof(CollisionChunkHeader) + vertexBytes + indexBytes != size || h.indexCount % 3 != 0)
                {
                    error = $"payload of {size} bytes doesn't match {h.vertexCount} vertices and {h.indexCount} indices";
                    return null;
                }

                var chunk = new CollisionChunk
                {
                    ChunkX = h.chunkX,
                    ChunkY = h.chunkY,
                    Worldspace = h.worldspace,
                    Generation = h.generation,
                    Vertices = new Vec3[h.vertexCount],
                    Triangles = new int[h.indexCount],
                };

                var v = (float*)(p + sizeof(CollisionChunkHeader));
                for (int i = 0; i < chunk.Vertices.Length; i++, v += 3)
                    chunk.Vertices[i] = island.SkyrimToIsland(new Vec3(h.origin[0] + v[0], h.origin[1] + v[1], h.origin[2] + v[2]));

                // Swapping Y and Z flips the handedness, so counterclockwise-from-above in Skyrim
                // needs the last two corners swapped to face up in Unity.
                var index = (uint*)(p + sizeof(CollisionChunkHeader) + vertexBytes);
                for (int t = 0; t < chunk.Triangles.Length; t += 3)
                {
                    uint a = index[t], b = index[t + 1], c = index[t + 2];
                    if (a >= h.vertexCount || b >= h.vertexCount || c >= h.vertexCount)
                    {
                        error = $"triangle {t / 3} uses a vertex past {h.vertexCount}";
                        return null;
                    }
                    chunk.Triangles[t] = (int)a;
                    chunk.Triangles[t + 1] = (int)c;
                    chunk.Triangles[t + 2] = (int)b;
                }
                return chunk;
            }
        }
    }
}
