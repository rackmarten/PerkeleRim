using System;
using System.Text;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class ModelTests
    {
        // One triangle facing up in Unity (clockwise seen from above): a, north of it, east of it.
        static ModelMeshData UpTriangle(bool mirrored = false) => ModelMeshData.FromUnity("case/bottle", 0,
            new float[] { 0, 0, 0, 0, 0, 1, 1, 0, 0 },
            new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0 },
            new float[] { 0, 0, 0, 1, 1, 0.25f },
            new[] { 0, 1, 2 }, mirrored);

        static float CrossZ(ModelMeshData m)
        {
            float[] p = m.Positions;
            int a = m.Indices[0] * 3, b = m.Indices[1] * 3, c = m.Indices[2] * 3;
            float ux = p[b] - p[a], uy = p[b + 1] - p[a + 1];
            float vx = p[c] - p[a], vy = p[c + 1] - p[a + 1];
            return ux * vy - uy * vx;
        }

        [Test]
        public void UnityMeshesBecomeSkyrimAxesUnitsAndWinding()
        {
            var m = UpTriangle();
            Assert.That(m.Positions[4], Is.EqualTo((float)Coords.UnitsPerMeter).Within(1e-3));  // Unity +Z (north) is Skyrim +Y
            Assert.That(m.Normals[2], Is.EqualTo(1f));  // Unity up is Skyrim +Z
            Assert.That(CrossZ(m), Is.GreaterThan(0f), "front faces counterclockwise in Skyrim");
            Assert.That(m.Uvs[5], Is.EqualTo(0.75f), "v counts from the top");
            Assert.That(CrossZ(UpTriangle(mirrored: true)), Is.LessThan(0f), "a mirrored mesh is turned round already");
        }

        [Test]
        public void TextureRowsGoTopFirst()
        {
            var bottomUp = new byte[] { 1, 1, 1, 1, 2, 2, 2, 2 };  // 1x2: bottom row 1, top row 2
            var t = ModelTextureData.FromUnity(1, 2, bottomUp);
            Assert.That(t.Rgba[0], Is.EqualTo(2));
            Assert.That(t.Rgba[4], Is.EqualTo(1));
        }

        [Test]
        public void EncodesTheHeaderLayout()
        {
            var model = new ModelData { Name = "beer case(itemx)" };
            model.Textures.Add(new ModelTextureData { Width = 1, Height = 1, Rgba = new byte[] { 9, 8, 7, 6 } });
            model.Meshes.Add(UpTriangle());
            var blob = model.Encode();

            Assert.That(Encoding.UTF8.GetString(blob, 0, 16), Is.EqualTo("beer case(itemx)"));
            Assert.That(blob[16], Is.EqualTo(0));
            Assert.That(BitConverter.ToUInt32(blob, 48), Is.EqualTo(1u), "textureCount");
            Assert.That(BitConverter.ToUInt32(blob, 52), Is.EqualTo(1u), "meshCount");
            Assert.That(BitConverter.ToUInt32(blob, 64 + 8), Is.EqualTo(Wire.ModelTextureRgba8));
            int mesh = 64 + 16 + 4;
            Assert.That(Encoding.UTF8.GetString(blob, mesh, 11), Is.EqualTo("case/bottle"));
            Assert.That(BitConverter.ToInt32(blob, mesh + 48), Is.EqualTo(0), "texture");
            Assert.That(BitConverter.ToUInt32(blob, mesh + 52), Is.EqualTo(3u), "vertexCount");
            Assert.That(BitConverter.ToUInt32(blob, mesh + 56), Is.EqualTo(3u), "indexCount");
            Assert.That(blob.Length, Is.EqualTo(mesh + 64 + 3 * 32 + 3 * 2));
        }

        [Test]
        public void LongNamesAreCutToFit()
        {
            var model = new ModelData { Name = new string('a', 60) };
            var blob = model.Encode();
            Assert.That(blob[(int)Wire.ModelNameMax - 2], Is.EqualTo((byte)'a'));
            Assert.That(blob[(int)Wire.ModelNameMax - 1], Is.EqualTo(0));
        }

        [Test]
        public void SplitsIntoOrderedChunks()
        {
            var blob = new byte[100];
            for (int i = 0; i < blob.Length; i++) blob[i] = (byte)i;
            var chunks = ModelData.Split(blob, 5, 40);
            Assert.That(chunks.Count, Is.EqualTo(3));
            Assert.That(BitConverter.ToUInt32(chunks[2], 0), Is.EqualTo(5u), "model");
            Assert.That(BitConverter.ToUInt32(chunks[2], 4), Is.EqualTo(100u), "totalSize");
            Assert.That(BitConverter.ToUInt32(chunks[2], 8), Is.EqualTo(80u), "offset");
            Assert.That(BitConverter.ToUInt32(chunks[2], 12), Is.EqualTo(20u), "size");
            Assert.That(chunks[2][16], Is.EqualTo(80));
            Assert.That(chunks[2].Length, Is.EqualTo(16 + 20));
        }
    }
}
