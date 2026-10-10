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
            model.Materials.Add(new ModelMaterialData { Flags = Wire.MaterialCutout, AlphaCutoff = 0.25f, Glossiness = 0.5f });
            var triangle = UpTriangle();
            triangle.Material = 0;
            model.Meshes.Add(triangle);
            var blob = model.Encode();

            Assert.That(Encoding.UTF8.GetString(blob, 0, 16), Is.EqualTo("beer case(itemx)"));
            Assert.That(blob[16], Is.EqualTo(0));
            Assert.That(BitConverter.ToUInt32(blob, 48), Is.EqualTo(1u), "textureCount");
            Assert.That(BitConverter.ToUInt32(blob, 52), Is.EqualTo(1u), "meshCount");
            Assert.That(BitConverter.ToUInt32(blob, 56), Is.EqualTo(1u), "materialCount");
            Assert.That(BitConverter.ToUInt32(blob, 64 + 8), Is.EqualTo(Wire.ModelTextureRgba8));
            int material = 64 + 16 + 4;
            Assert.That(BitConverter.ToUInt32(blob, material), Is.EqualTo(Wire.MaterialCutout), "flags");
            Assert.That(BitConverter.ToSingle(blob, material + 4), Is.EqualTo(0.25f), "alphaCutoff");
            Assert.That(BitConverter.ToSingle(blob, material + 20), Is.EqualTo(0.5f), "glossiness");
            int mesh = material + 40;
            Assert.That(Encoding.UTF8.GetString(blob, mesh, 11), Is.EqualTo("case/bottle"));
            Assert.That(BitConverter.ToInt32(blob, mesh + 48), Is.EqualTo(0), "texture");
            Assert.That(BitConverter.ToUInt32(blob, mesh + 52), Is.EqualTo(3u), "vertexCount");
            Assert.That(BitConverter.ToUInt32(blob, mesh + 56), Is.EqualTo(3u), "indexCount");
            Assert.That(BitConverter.ToInt32(blob, mesh + 60), Is.EqualTo(0), "material");
            Assert.That(blob.Length, Is.EqualTo(mesh + 64 + 3 * 32 + 3 * 2));
        }

        [Test]
        public void AMeshNeedsAnExistingMaterial()
        {
            var model = new ModelData { Name = "x" };
            var triangle = UpTriangle();
            triangle.Material = 1;
            model.Meshes.Add(triangle);
            Assert.Throws<ArgumentException>(() => model.Encode());
        }

        [Test]
        public void TintAndMetalAreBakedIntoTheTexture()
        {
            var texture = new ModelTextureData { Width = 2, Height = 1, Rgba = new byte[] { 200, 100, 50, 255, 200, 100, 50, 128 } };
            var metal = new ModelTextureData { Width = 1, Height = 1, Rgba = new byte[] { 255, 0, 0, 255 } };
            var tinted = ModelTextureData.Baked(texture, new[] { 0.5f, 1f, 1f, 0.5f }, null, 0f);
            Assert.That(tinted.Rgba[0], Is.EqualTo(100));
            Assert.That(tinted.Rgba[1], Is.EqualTo(100));
            Assert.That(tinted.Rgba[3], Is.EqualTo(128));
            var darkened = ModelTextureData.Baked(texture, new[] { 1f, 1f, 1f, 1f }, metal, 0f);
            Assert.That(darkened.Rgba[4], Is.EqualTo((byte)Math.Round(200 * (1 - ModelTextureData.MetalDarkening))));
            Assert.That(darkened.Rgba[7], Is.EqualTo(128), "alpha untouched by metal");
            var plain = ModelTextureData.Baked(null, new[] { 0.5f, 0.25f, 1f, 1f }, null, 0f);
            Assert.That(plain.Width, Is.EqualTo(1));
            Assert.That(plain.Rgba[1], Is.EqualTo(64), "no texture is white times the tint");
        }

        [Test]
        public void UnityMaterialsBecomeFlags()
        {
            // The beer case: Standard in cutout mode. A bottle: Standard opaque with a metallic map.
            var crate = ModelMaterialData.FromUnity(new ModelMaterialData.Unity { Shader = "Standard", Mode = 1, Cutoff = 0.4f, Glossiness = 0.37f });
            Assert.That(crate.Flags, Is.EqualTo(Wire.MaterialCutout));
            Assert.That(crate.AlphaCutoff, Is.EqualTo(0.4f));
            var bottle = ModelMaterialData.FromUnity(new ModelMaterialData.Unity { Shader = "Standard", Mode = 0, Metallic = 1f, Glossiness = 0.68f });
            Assert.That(bottle.Flags, Is.EqualTo(0u));
            Assert.That(bottle.Specular[0], Is.EqualTo(0.3f * 0.68f).Within(1e-5));
            Assert.That(bottle.Glossiness, Is.EqualTo(0.68f).Within(1e-5));
            var glass = ModelMaterialData.FromUnity(new ModelMaterialData.Unity { Shader = "Standard", Mode = 3 });
            Assert.That(glass.Flags, Is.EqualTo(Wire.MaterialBlend));
            var legacy = ModelMaterialData.FromUnity(new ModelMaterialData.Unity { Shader = "Legacy Shaders/Diffuse" });
            Assert.That(legacy.Flags, Is.EqualTo(0u));
            Assert.That(legacy.Specular[0], Is.EqualTo(0f));
            var unlit = ModelMaterialData.FromUnity(new ModelMaterialData.Unity { Shader = "Unlit/Transparent", RenderQueue = 3000 });
            Assert.That(unlit.Flags, Is.EqualTo(Wire.MaterialBlend | Wire.MaterialUnlit));
        }

        [Test]
        public void AnOverlayIsOneQuadFacingTheViewer()
        {
            var texture = new ModelTextureData { Width = 4, Height = 2, Rgba = new byte[32] };
            var model = ModelData.Overlay("PlayerBlood", texture);
            Assert.That(model.Materials[0].Flags, Is.EqualTo(Wire.MaterialBlend | Wire.MaterialUnlit | Wire.MaterialOverlay));
            var quad = model.Meshes[0];
            Assert.That(quad.Positions[8], Is.EqualTo(0.5f), "half as tall as wide, like the texture");
            // The first triangle faces -Y (towards a viewer looking along +Y).
            float[] p = quad.Positions;
            int a = quad.Indices[0] * 3, b = quad.Indices[1] * 3, c = quad.Indices[2] * 3;
            float ux = p[b] - p[a], uz = p[b + 2] - p[a + 2], vx = p[c] - p[a], vz = p[c + 2] - p[a + 2];
            Assert.That(uz * vx - ux * vz, Is.LessThan(0f));
            Assert.DoesNotThrow(() => model.Encode());
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
