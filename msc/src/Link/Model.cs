using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // A model for the ModelChunk messages: MSC's own meshes and textures, which Skyrim builds in
    // memory (blob layout in protocol/perkelerim_protocol.h). Unity-free, so the tests cover it; the
    // game side (Game/ModelSender) reads the Unity objects and fills these from them.
    public sealed class ModelTextureData
    {
        public int Width, Height;
        public byte[] Rgba;  // Width * Height * 4, top row first

        // Unity's pixel order (Texture2D.GetPixels32: bottom row first) to the wire's.
        public static ModelTextureData FromUnity(int width, int height, byte[] bottomUpRgba)
        {
            int row = width * 4;
            var rgba = new byte[row * height];
            for (int y = 0; y < height; y++)
                Array.Copy(bottomUpRgba, y * row, rgba, (height - 1 - y) * row, row);
            return new ModelTextureData { Width = width, Height = height, Rgba = rgba };
        }
    }

    public sealed class ModelMeshData
    {
        public string Name;
        public int Texture = -1;
        public float[] Positions;  // x, y, z per vertex, Skyrim axes and units
        public float[] Normals;    // x, y, z per vertex, Skyrim axes
        public float[] Uvs;        // u, v per vertex, (0, 0) = top left
        public ushort[] Indices;   // 3 per triangle, counterclockwise seen from the front

        public int VertexCount => Positions.Length / 3;

        // From Unity mesh data already moved into the model root's space (meters, Unity axes).
        // Unity's front faces wind clockwise seen from the front, so every triangle is turned round,
        // unless the mesh is mirrored into the root (negative scale), which turned it round already.
        public static ModelMeshData FromUnity(string name, int texture, float[] positions, float[] normals, float[] uvs,
            int[] triangles, bool mirrored)
        {
            int n = positions.Length / 3;
            if (n > Wire.ModelMeshVertexMax) throw new ArgumentException($"{name}: {n} vertices, at most {Wire.ModelMeshVertexMax}");
            var mesh = new ModelMeshData
            {
                Name = name,
                Texture = texture,
                Positions = new float[n * 3],
                Normals = new float[n * 3],
                Uvs = new float[n * 2],
                Indices = new ushort[triangles.Length],
            };
            for (int i = 0; i < n; i++)
            {
                var p = Coords.UnityToSkyrim(new Vec3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]));
                mesh.Positions[i * 3] = p.x;
                mesh.Positions[i * 3 + 1] = p.y;
                mesh.Positions[i * 3 + 2] = p.z;
                if (normals != null && normals.Length == n * 3)
                {
                    mesh.Normals[i * 3] = normals[i * 3];
                    mesh.Normals[i * 3 + 1] = normals[i * 3 + 2];
                    mesh.Normals[i * 3 + 2] = normals[i * 3 + 1];
                }
                if (uvs != null && uvs.Length == n * 2)
                {
                    mesh.Uvs[i * 2] = uvs[i * 2];
                    mesh.Uvs[i * 2 + 1] = 1f - uvs[i * 2 + 1];
                }
            }
            for (int t = 0; t + 2 < triangles.Length; t += 3)
            {
                mesh.Indices[t] = (ushort)triangles[t];
                mesh.Indices[t + 1] = (ushort)triangles[mirrored ? t + 1 : t + 2];
                mesh.Indices[t + 2] = (ushort)triangles[mirrored ? t + 2 : t + 1];
            }
            return mesh;
        }
    }

    public sealed class ModelData
    {
        public string Name;
        public readonly List<ModelTextureData> Textures = new List<ModelTextureData>();
        public readonly List<ModelMeshData> Meshes = new List<ModelMeshData>();

        public int VertexCount
        {
            get
            {
                int n = 0;
                foreach (var m in Meshes) n += m.VertexCount;
                return n;
            }
        }

        public byte[] Encode()
        {
            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            WriteName(w, Name);
            w.Write((uint)Textures.Count);
            w.Write((uint)Meshes.Count);
            w.Write(0u);
            w.Write(0u);
            foreach (var t in Textures)
            {
                if (t.Width < 1 || t.Height < 1 || t.Width > Wire.ModelTextureSizeMax || t.Height > Wire.ModelTextureSizeMax ||
                    t.Rgba.Length != t.Width * t.Height * 4)
                    throw new ArgumentException($"{Name}: bad texture {t.Width}x{t.Height}");
                w.Write((uint)t.Width);
                w.Write((uint)t.Height);
                w.Write(Wire.ModelTextureRgba8);
                w.Write(0u);
                w.Write(t.Rgba);
            }
            foreach (var m in Meshes)
            {
                WriteName(w, m.Name);
                w.Write(m.Texture);
                w.Write((uint)m.VertexCount);
                w.Write((uint)m.Indices.Length);
                w.Write(0u);
                for (int i = 0; i < m.VertexCount; i++)
                {
                    for (int k = 0; k < 3; k++) w.Write(m.Positions[i * 3 + k]);
                    for (int k = 0; k < 3; k++) w.Write(m.Normals[i * 3 + k]);
                    w.Write(m.Uvs[i * 2]);
                    w.Write(m.Uvs[i * 2 + 1]);
                }
                foreach (var index in m.Indices) w.Write(index);
            }
            w.Flush();
            if (stream.Length > Wire.ModelSizeMax) throw new ArgumentException($"{Name}: {stream.Length} bytes, at most {Wire.ModelSizeMax}");
            return stream.ToArray();
        }

        // ModelChunk payloads for a blob, in sending order.
        public static List<byte[]> Split(byte[] blob, uint model, int chunkMax = (int)Wire.ModelChunkMax)
        {
            var payloads = new List<byte[]>();
            int offset = 0;
            do
            {
                int size = Math.Min(chunkMax, blob.Length - offset);
                var payload = new byte[16 + size];  // sizeof(ModelChunk)
                BitConverter.GetBytes(model).CopyTo(payload, 0);
                BitConverter.GetBytes((uint)blob.Length).CopyTo(payload, 4);
                BitConverter.GetBytes((uint)offset).CopyTo(payload, 8);
                BitConverter.GetBytes((uint)size).CopyTo(payload, 12);
                Array.Copy(blob, offset, payload, 16, size);
                payloads.Add(payload);
                offset += size;
            } while (offset < blob.Length);
            return payloads;
        }

        // UTF-8, cut at a character boundary to fit, zero padded.
        static void WriteName(BinaryWriter w, string name)
        {
            var bytes = Encoding.UTF8.GetBytes(name ?? "");
            int length = Math.Min(bytes.Length, (int)Wire.ModelNameMax - 1);
            while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80) length--;
            var field = new byte[Wire.ModelNameMax];
            Array.Copy(bytes, field, length);
            w.Write(field);
        }
    }
}
