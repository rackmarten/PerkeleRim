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

        // A metal's diffuse is darkened by this much at metallic 1. Unity makes it nearly black and
        // shows reflections instead, which Skyrim doesn't have for these, so only part of it.
        public const float MetalDarkening = 0.35f;

        // What MSC actually draws as diffuse: the texture (white when null) times the material's color
        // tint, darkened where it is metal (metalMap's red channel, sampled nearest, or metallic for
        // the whole texture when there is no map).
        public static ModelTextureData Baked(ModelTextureData texture, float[] tint, ModelTextureData metalMap, float metallic)
        {
            ModelTextureData baked = null;
            foreach (var step in BakeSteps(texture, tint, metalMap, metallic, int.MaxValue)) baked = step;
            return baked;
        }

        // Baked a band of rows at a time (ModelSender reads over several frames): null after each
        // band, the texture last. Each output byte depends only on its input byte and, with a metal
        // map, the map's byte there, so they go through tables worked out once (the same sums).
        public static IEnumerable<ModelTextureData> BakeSteps(ModelTextureData texture, float[] tint, ModelTextureData metalMap, float metallic, int rowsPerStep)
        {
            var src = texture ?? new ModelTextureData { Width = 1, Height = 1, Rgba = new byte[] { 255, 255, 255, 255 } };
            int w = src.Width, h = src.Height;
            var rgba = new byte[src.Rgba.Length];
            var alpha = new byte[256];
            for (int v = 0; v < 256; v++) alpha[v] = Byte(v * tint[3]);
            // By shade: one table of 3 x 256 for the whole texture, or one per byte of the metal map
            // (worked out when that byte first comes up).
            var tables = new byte[metalMap != null ? 256 : 1][];
            if (metalMap == null) tables[0] = ShadeTable(tint, Shade(metallic));
            int[] column = null;
            if (metalMap != null)
            {
                column = new int[w];
                for (int x = 0; x < w; x++) column[x] = x * metalMap.Width / w;
            }
            var from = src.Rgba;
            for (int y0 = 0; y0 < h; y0 += rowsPerStep)
            {
                int y1 = (int)Math.Min(h, (long)y0 + rowsPerStep);
                for (int y = y0; y < y1; y++)
                {
                    int metalRow = metalMap != null ? (y * metalMap.Height / h) * metalMap.Width : 0;
                    var table = tables[0];
                    for (int x = 0, i = y * w * 4; x < w; x++, i += 4)
                    {
                        if (metalMap != null)
                        {
                            int m = metalMap.Rgba[(metalRow + column[x]) * 4];
                            table = tables[m] ?? (tables[m] = ShadeTable(tint, Shade(m / 255f)));
                        }
                        rgba[i] = table[from[i]];
                        rgba[i + 1] = table[256 + from[i + 1]];
                        rgba[i + 2] = table[512 + from[i + 2]];
                        rgba[i + 3] = alpha[from[i + 3]];
                    }
                }
                if (y1 < h) yield return null;
            }
            yield return new ModelTextureData { Width = w, Height = h, Rgba = rgba };
        }

        static byte[] ShadeTable(float[] tint, float shade)
        {
            var table = new byte[768];
            for (int c = 0; c < 3; c++)
                for (int v = 0; v < 256; v++) table[c * 256 + v] = Byte(v * tint[c] * shade);
            return table;
        }

        static float Shade(float metal) => 1f - MetalDarkening * Math.Max(0f, Math.Min(1f, metal));

        static byte Byte(float v) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(v)));
    }

    // How a mesh is drawn (ModelMaterial): kMaterial* flags, alpha cutoff, highlight, glow.
    public sealed class ModelMaterialData
    {
        public uint Flags;
        public float AlphaCutoff = 0.5f;
        public float[] Specular = new float[3];  // r, g, b
        public float Glossiness;
        public float[] Emissive = new float[3];  // r, g, b

        // What MSC's Unity material says, as far as Skyrim can follow it. Null fields weren't set.
        public sealed class Unity
        {
            public string Shader = "";
            public int RenderQueue;
            public float? Mode;         // Standard: 0 opaque, 1 cutout, 2 fade, 3 transparent
            public float? Cutoff;
            public float? Metallic;     // Standard: the metallic map's average red, else _Metallic
            public float? Glossiness;   // Standard: the metallic map's average alpha, else _Glossiness
            public float[] SpecColor;   // legacy specular shaders
            public float? Shininess;    // legacy specular shaders
            public float[] Emission;    // Standard _EmissionColor
        }

        public static ModelMaterialData FromUnity(Unity u)
        {
            var m = new ModelMaterialData();
            var shader = u.Shader ?? "";
            bool standard = shader == "Standard" || shader.StartsWith("Standard ");
            int mode = u.Mode.HasValue ? (int)Math.Round(u.Mode.Value) : 0;
            if (shader.Contains("Cutout") || (standard && mode == 1)) m.Flags |= Wire.MaterialCutout;
            else if (shader.Contains("Transparent") || (standard && mode >= 2) || u.RenderQueue >= 3000) m.Flags |= Wire.MaterialBlend;
            if (shader.StartsWith("Unlit/") || shader.StartsWith("GUI/") || shader.StartsWith("Particles/")) m.Flags |= Wire.MaterialUnlit;
            m.AlphaCutoff = u.Cutoff ?? 0.5f;
            if (standard)
            {
                // Unity shows smooth surfaces mostly through reflections, which Skyrim has none of here,
                // so only a soft highlight: metals more, dielectrics a little.
                float gloss = Clamp01(u.Glossiness ?? 0.5f), metal = Clamp01(u.Metallic ?? 0f);
                float strength = 0.3f * gloss * (0.3f + 0.7f * metal);
                m.Specular = new[] { strength, strength, strength };
                m.Glossiness = gloss;
                if (u.Emission != null && u.Emission.Length >= 3) m.Emissive = new[] { u.Emission[0], u.Emission[1], u.Emission[2] };
            }
            else if (shader.Contains("Specular") && u.SpecColor != null && u.SpecColor.Length >= 3)
            {
                m.Specular = new[] { u.SpecColor[0], u.SpecColor[1], u.SpecColor[2] };
                m.Glossiness = Clamp01(u.Shininess ?? 0.5f);
            }
            return m;
        }

        static float Clamp01(float v) => Math.Max(0f, Math.Min(1f, v));
    }

    public sealed class ModelMeshData
    {
        public string Name;
        public int Texture = -1;
        public int Material = -1;
        public int Node = -1;      // index into ModelData.Nodes, -1 = the model's root
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

    // A named node meshes hang under (ModelNode), so Skyrim can move it on its own: pose relative to
    // its parent at rest, Skyrim axes and units.
    public sealed class ModelNodeData
    {
        public string Name;
        public int Parent = -1;  // index into ModelData.Nodes, below this one's; -1 = the model's root
        public float[] Translation = new float[3];
        public float[] Rotation = { 0f, 0f, 0f, 1f };  // x, y, z, w
    }

    // A light of a vehicle (ModelLight): its role (Wire.Light*), color and where it shines from and along,
    // in the vehicle's frame, Skyrim axes and units.
    public sealed class ModelLightData
    {
        public uint Role;
        public float[] Color = { 1f, 1f, 1f };
        public float[] Position = new float[3];
        public float[] Direction = { 0f, 1f, 0f };
    }

    // A model's blob as the pieces it was encoded in, in order (ModelData.EncodeSteps): textures'
    // pixels are their own arrays, not copied. ChunkCursor cuts it into ModelChunk payloads.
    public sealed class EncodedModel
    {
        public readonly List<byte[]> Parts = new List<byte[]>();
        public int Length { get; private set; }

        public void Add(byte[] part)
        {
            if (part.Length == 0) return;
            Parts.Add(part);
            Length += part.Length;
        }

        public byte[] ToArray()
        {
            var blob = new byte[Length];
            int at = 0;
            foreach (var part in Parts)
            {
                Array.Copy(part, 0, blob, at, part.Length);
                at += part.Length;
            }
            return blob;
        }
    }

    // An encoded model's ModelChunk payloads, one after the other, each copied from its parts into
    // one buffer (ModelSender's, for every chunk): Next makes the next one, Sent moves on past it.
    public sealed class ChunkCursor
    {
        readonly EncodedModel model;
        readonly uint id;
        readonly int chunkMax;
        int part, at;          // where the next chunk starts
        int nextPart, nextAt;  // where the one after starts, once Next made one

        public ChunkCursor(EncodedModel model, uint id, int chunkMax = (int)Wire.ModelChunkMax)
        {
            this.model = model;
            this.id = id;
            this.chunkMax = chunkMax;
        }

        public uint Id => id;
        public int Length => model.Length;
        public int Offset { get; private set; }  // into the blob, of the next chunk
        public bool Done => Offset >= model.Length;

        // The next chunk into payload (at least 16 + chunkMax long): its size.
        public int Next(byte[] payload)
        {
            int size = Math.Min(chunkMax, model.Length - Offset);
            ModelData.PutChunkHeader(payload, id, (uint)model.Length, (uint)Offset, (uint)size);
            int p = part, a = at, filled = 0;
            while (filled < size)
            {
                var from = model.Parts[p];
                int n = Math.Min(size - filled, from.Length - a);
                Array.Copy(from, a, payload, 16 + filled, n);
                filled += n;
                a += n;
                if (a == from.Length) { p++; a = 0; }
            }
            nextPart = p;
            nextAt = a;
            return 16 + size;
        }

        // The chunk Next made went out.
        public void Sent(int payloadSize)
        {
            Offset += payloadSize - 16;
            part = nextPart;
            at = nextAt;
        }
    }

    public sealed class ModelData
    {
        public string Name;
        public readonly List<ModelTextureData> Textures = new List<ModelTextureData>();
        public readonly List<ModelMaterialData> Materials = new List<ModelMaterialData>();
        public readonly List<ModelNodeData> Nodes = new List<ModelNodeData>();
        public readonly List<ModelLightData> Lights = new List<ModelLightData>();
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
            EncodedModel encoded = null;
            foreach (var step in EncodeSteps()) if (step != null) encoded = step;
            return encoded.ToArray();
        }

        // The blob's size, worked out before writing it.
        public long EncodedSize()
        {
            long size = Wire.ModelNameMax + 5 * 4;
            foreach (var t in Textures) size += 16 + (t.Rgba != null ? t.Rgba.LongLength : 0);
            size += Materials.Count * 40L;
            size += Nodes.Count * (Wire.ModelNodeNameMax + 4 + 7 * 4L);
            size += Lights.Count * 40L;
            foreach (var m in Meshes)
                size += Wire.ModelNameMax + 6 * 4 + (m.Positions != null ? m.VertexCount * 32L : 0) + (m.Indices != null ? m.Indices.Length * 2L : 0);
            return size;
        }

        // Encoded a texture or a mesh at a time (ModelSender reads over several frames): null after
        // each, the encoded model last. Never one array: a texture's pixels go in as they are, the
        // rest in pieces (EncodedModel), so a vehicle's 50 MB and more is never copied whole.
        public IEnumerable<EncodedModel> EncodeSteps()
        {
            long size = EncodedSize();
            if (size > Wire.ModelSizeMax) throw new ArgumentException($"{Name}: {size} bytes, at most {Wire.ModelSizeMax}");
            var encoded = new EncodedModel();
            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            WriteName(w, Name);
            w.Write((uint)Textures.Count);
            w.Write((uint)Meshes.Count);
            w.Write((uint)Materials.Count);
            w.Write((uint)Nodes.Count);
            w.Write((uint)Lights.Count);
            foreach (var t in Textures)
            {
                if (t.Width < 1 || t.Height < 1 || t.Width > Wire.ModelTextureSizeMax || t.Height > Wire.ModelTextureSizeMax ||
                    t.Rgba.Length != t.Width * t.Height * 4)
                    throw new ArgumentException($"{Name}: bad texture {t.Width}x{t.Height}");
                w.Write((uint)t.Width);
                w.Write((uint)t.Height);
                w.Write(Wire.ModelTextureRgba8);
                w.Write(0u);
                encoded.Add(Take(stream));
                encoded.Add(t.Rgba);
                yield return null;
            }
            foreach (var m in Materials)
            {
                w.Write(m.Flags);
                w.Write(m.AlphaCutoff);
                for (int k = 0; k < 3; k++) w.Write(m.Specular[k]);
                w.Write(m.Glossiness);
                for (int k = 0; k < 3; k++) w.Write(m.Emissive[k]);
                w.Write(0u);
            }
            for (int i = 0; i < Nodes.Count; i++)
            {
                var n = Nodes[i];
                if (n.Parent < -1 || n.Parent >= i) throw new ArgumentException($"{Name}: node {n.Name} has parent {n.Parent}");
                WriteName(w, n.Name, (int)Wire.ModelNodeNameMax);
                w.Write(n.Parent);
                for (int k = 0; k < 3; k++) w.Write(n.Translation[k]);
                for (int k = 0; k < 4; k++) w.Write(n.Rotation[k]);
            }
            if (Lights.Count > Wire.ModelLightMax) throw new ArgumentException($"{Name}: {Lights.Count} lights, at most {Wire.ModelLightMax}");
            foreach (var l in Lights)
            {
                w.Write(l.Role);
                for (int k = 0; k < 3; k++) w.Write(l.Color[k]);
                for (int k = 0; k < 3; k++) w.Write(l.Position[k]);
                for (int k = 0; k < 3; k++) w.Write(l.Direction[k]);
            }
            foreach (var m in Meshes)
            {
                if (m.Material < -1 || m.Material >= Materials.Count) throw new ArgumentException($"{Name}: mesh {m.Name} has material {m.Material}");
                if (m.Node < -1 || m.Node >= Nodes.Count) throw new ArgumentException($"{Name}: mesh {m.Name} has node {m.Node}");
                if (m.Positions == null || m.Positions.Length % 3 != 0 || m.Normals == null || m.Normals.Length < m.Positions.Length || m.Uvs == null ||
                    m.Uvs.Length < m.VertexCount * 2 || m.Indices == null)
                    throw new ArgumentException($"{Name}: mesh {m.Name} has {(m.Positions != null ? m.Positions.Length / 3 : 0)} positions but not as many normals and uvs");
                WriteName(w, m.Name);
                w.Write(m.Texture);
                w.Write((uint)m.VertexCount);
                w.Write((uint)m.Indices.Length);
                w.Write(m.Material);
                w.Write(m.Node);
                w.Write(0u);
                encoded.Add(Take(stream));
                encoded.Add(Vertices(m));
                yield return null;
            }
            encoded.Add(Take(stream));
            if (encoded.Length != size) throw new InvalidOperationException($"{Name}: encoded {encoded.Length} bytes, worked out {size}");
            yield return encoded;
        }

        // What the writer wrote since the last Take, and the stream emptied for the next.
        static byte[] Take(MemoryStream stream)
        {
            var bytes = stream.ToArray();
            stream.SetLength(0);
            return bytes;
        }

        // A mesh's position, normal and uv per vertex, then its indices: little-endian, as the machine is.
        static unsafe byte[] Vertices(ModelMeshData m)
        {
            int n = m.VertexCount;
            var bytes = new byte[n * 32 + m.Indices.Length * 2];
            if (n > 0)
                fixed (byte* to = bytes)
                fixed (float* p = m.Positions)
                fixed (float* nn = m.Normals)
                fixed (float* uv = m.Uvs)
                {
                    float* f = (float*)to;
                    for (int i = 0; i < n; i++, f += 8)
                    {
                        f[0] = p[i * 3]; f[1] = p[i * 3 + 1]; f[2] = p[i * 3 + 2];
                        f[3] = nn[i * 3]; f[4] = nn[i * 3 + 1]; f[5] = nn[i * 3 + 2];
                        f[6] = uv[i * 2]; f[7] = uv[i * 2 + 1];
                    }
                }
            Buffer.BlockCopy(m.Indices, 0, bytes, n * 32, m.Indices.Length * 2);
            return bytes;
        }

        // A screen overlay: one quad showing texture over everything without lighting, 2 units wide
        // (x -1..1, right), as tall as the texture's shape (z, up), at y 0 facing a viewer that
        // looks along +Y. Skyrim puts it in front of its camera and scales it to cover the view.
        public static ModelData Overlay(string name, ModelTextureData texture)
        {
            float h = (float)texture.Height / texture.Width;
            var model = new ModelData { Name = name };
            model.Textures.Add(texture);
            model.Materials.Add(new ModelMaterialData { Flags = Wire.MaterialBlend | Wire.MaterialUnlit | Wire.MaterialOverlay });
            model.Meshes.Add(new ModelMeshData
            {
                Name = name,
                Texture = 0,
                Material = 0,
                // bottom left, bottom right, top right, top left
                Positions = new[] { -1f, 0f, -h, 1f, 0f, -h, 1f, 0f, h, -1f, 0f, h },
                Normals = new[] { 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f, 0f },
                Uvs = new[] { 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f },
                Indices = new ushort[] { 0, 1, 2, 0, 2, 3 },
            });
            return model;
        }

        // A particle's look: one square of texture, 1 unit wide (x and z -0.5..0.5), alpha blended
        // and unlit, at y 0 facing a viewer that looks along +Y. Skyrim turns it to its camera and
        // scales it to the particle's size.
        public static ModelData Particle(string name, ModelTextureData texture)
        {
            var model = new ModelData { Name = name };
            model.Textures.Add(texture);
            model.Materials.Add(new ModelMaterialData { Flags = Wire.MaterialBlend | Wire.MaterialUnlit });
            model.Meshes.Add(new ModelMeshData
            {
                Name = name,
                Texture = 0,
                Material = 0,
                // bottom left, bottom right, top right, top left
                Positions = new[] { -0.5f, 0f, -0.5f, 0.5f, 0f, -0.5f, 0.5f, 0f, 0.5f, -0.5f, 0f, 0.5f },
                Normals = new[] { 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f, 0f, 0f, -1f, 0f },
                Uvs = new[] { 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f },
                Indices = new ushort[] { 0, 1, 2, 0, 2, 3 },
            });
            return model;
        }

        // ModelChunk payloads for a blob, in sending order.
        public static List<byte[]> Split(byte[] blob, uint model, int chunkMax = (int)Wire.ModelChunkMax)
        {
            var payloads = new List<byte[]>();
            int offset = 0;
            do
            {
                payloads.Add(Chunk(blob, model, offset, chunkMax));
                offset += payloads[payloads.Count - 1].Length - 16;
            } while (offset < blob.Length);
            return payloads;
        }

        // The ModelChunk payload starting at offset.
        public static byte[] Chunk(byte[] blob, uint model, int offset, int chunkMax = (int)Wire.ModelChunkMax)
        {
            int size = Math.Min(chunkMax, blob.Length - offset);
            var payload = new byte[16 + size];  // sizeof(ModelChunk)
            PutChunkHeader(payload, model, (uint)blob.Length, (uint)offset, (uint)size);
            Array.Copy(blob, offset, payload, 16, size);
            return payload;
        }

        // sizeof(ModelChunk): model, totalSize, offset, size.
        public static void PutChunkHeader(byte[] payload, uint model, uint total, uint offset, uint size)
        {
            Put(payload, 0, model);
            Put(payload, 4, total);
            Put(payload, 8, offset);
            Put(payload, 12, size);
        }

        static void Put(byte[] to, int at, uint value)
        {
            to[at] = (byte)value;
            to[at + 1] = (byte)(value >> 8);
            to[at + 2] = (byte)(value >> 16);
            to[at + 3] = (byte)(value >> 24);
        }

        // UTF-8, cut at a character boundary to fit, zero padded.
        static void WriteName(BinaryWriter w, string name, int size = (int)Wire.ModelNameMax)
        {
            var bytes = Encoding.UTF8.GetBytes(name ?? "");
            int length = Math.Min(bytes.Length, size - 1);
            while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80) length--;
            var field = new byte[size];
            Array.Copy(bytes, field, length);
            w.Write(field);
        }
    }
}
