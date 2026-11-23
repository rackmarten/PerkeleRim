using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    // The pieces ModelSender reads MSC's models with over several frames: the per-frame step budget,
    // the texture cache, the frame watch, and the step-wise bake and encode, which must give exactly
    // what the one-shot versions gave.
    public class ModelReadTests
    {
        // A clock the steps move on: each step takes what the list says.
        sealed class FakeWork
        {
            public double Now;
            public int Done;
            readonly double[] costs;

            public FakeWork(params double[] costs)
            {
                this.costs = costs;
            }

            public IEnumerator Steps()
            {
                foreach (var cost in costs)
                {
                    Now += cost;
                    Done++;
                    yield return "step " + Done;
                }
            }
        }

        [Test]
        public void ABudgetRunsStepsUntilItIsSpentAndTheRestNextFrame()
        {
            var work = new FakeWork(3, 3, 3, 3, 3);
            var budget = new StepBudget(5, () => work.Now);
            var steps = work.Steps();
            budget.BeginFrame();
            Assert.That(budget.Run(steps), Is.False, "frame 1");
            Assert.That(work.Done, Is.EqualTo(2), "3 ms, then 6: over 5, the rest waits");
            Assert.That(budget.Spent, Is.True);
            budget.BeginFrame();
            Assert.That(budget.Run(steps), Is.False, "frame 2");
            Assert.That(work.Done, Is.EqualTo(4));
            budget.BeginFrame();
            Assert.That(budget.Run(steps), Is.True, "frame 3 ends it");
            Assert.That(work.Done, Is.EqualTo(5));
        }

        [Test]
        public void ABudgetAlwaysRunsAStepAndKeepsTheLongest()
        {
            var work = new FakeWork(1, 40, 1);
            var budget = new StepBudget(5, () => work.Now);
            var steps = work.Steps();
            budget.BeginFrame();
            budget.Run(steps);
            Assert.That(work.Done, Is.EqualTo(2), "a step that outlasts the budget still runs, and ends the frame");
            Assert.That(budget.LongestStepMs, Is.EqualTo(40));
            Assert.That(budget.LongestStep, Is.EqualTo("step 2"));
            budget.ResetLongest();
            Assert.That(budget.LongestStepMs, Is.EqualTo(0));
            budget.BeginFrame();
            Assert.That(budget.Run(steps), Is.True);
        }

        [Test]
        public void SmallPiecesOfWorkShareAFrame()
        {
            double now = 0;
            IEnumerator Small(int steps)
            {
                for (int i = 0; i < steps; i++)
                {
                    now += 1;
                    yield return null;
                }
            }
            var budget = new StepBudget(5, () => now);
            budget.BeginFrame();
            Assert.That(budget.Run(Small(2)), Is.True, "the first ends at 2 ms");
            Assert.That(budget.Spent, Is.False);
            Assert.That(budget.Run(Small(2)), Is.True, "the second at 4 ms, in the same frame");
            Assert.That(budget.Run(Small(2)), Is.False, "the third gets 1 ms of it");
            Assert.That(now, Is.EqualTo(5));
        }

        [Test]
        public void ACacheDropsTheLeastRecentlyUsedFirst()
        {
            var cache = new SizedCache<int, string>(10);
            cache.Put(1, "a", 4);
            cache.Put(2, "b", 4);
            string v;
            Assert.That(cache.TryGet(1, out v), Is.True);  // 1 is now the most recent
            cache.Put(3, "c", 4);                           // 12 > 10: 2 goes
            Assert.That(cache.TryGet(2, out v), Is.False);
            Assert.That(cache.TryGet(1, out v) && v == "a", Is.True);
            Assert.That(cache.TryGet(3, out v) && v == "c", Is.True);
            Assert.That(cache.Size, Is.EqualTo(8));
            Assert.That(cache.Hits, Is.EqualTo(3));
            Assert.That(cache.Misses, Is.EqualTo(1));
        }

        [Test]
        public void ACacheKeepsNothingBiggerThanItselfAndReplacesByKey()
        {
            var cache = new SizedCache<string, int>(10);
            cache.Put("big", 1, 11);
            Assert.That(cache.Count, Is.EqualTo(0));
            cache.Put("x", 1, 6);
            cache.Put("x", 2, 3);
            int v;
            Assert.That(cache.TryGet("x", out v) && v == 2, Is.True);
            Assert.That(cache.Size, Is.EqualTo(3));
            cache.Remove("x");
            Assert.That(cache.Size, Is.EqualTo(0));
            cache.Put("y", 1, 5);
            cache.Clear();
            Assert.That(cache.Count, Is.EqualTo(0));
            Assert.That(cache.Size, Is.EqualTo(0));
        }

        [Test]
        public void AFrameWatchCountsTheHitches()
        {
            var watch = new FrameWatch();
            foreach (var ms in new[] { 16.0, 16, 150, 2100, 16, -1 }) watch.Add(ms);
            Assert.That(watch.Frames, Is.EqualTo(5), "a negative time is no frame");
            Assert.That(watch.WorstMs, Is.EqualTo(2100));
            Assert.That(watch.Hitches, Is.EqualTo(1), "over 200 ms");
            Assert.That(watch.Slow, Is.EqualTo(2), "over 100 ms");
            Assert.That(watch.Summary(), Does.Contain("worst 2100 ms"));
        }

        static ModelTextureData Noise(int width, int height, int seed)
        {
            var random = new Random(seed);
            var rgba = new byte[width * height * 4];
            random.NextBytes(rgba);
            return new ModelTextureData { Width = width, Height = height, Rgba = rgba };
        }

        // The bake as it was, pixel by pixel: what BakeSteps must still give.
        static byte[] BakedPixelByPixel(ModelTextureData src, float[] tint, ModelTextureData metalMap, float metallic)
        {
            var rgba = new byte[src.Rgba.Length];
            for (int y = 0; y < src.Height; y++)
                for (int x = 0; x < src.Width; x++)
                {
                    int i = (y * src.Width + x) * 4;
                    float metal = metallic;
                    if (metalMap != null)
                        metal = metalMap.Rgba[((y * metalMap.Height / src.Height) * metalMap.Width + x * metalMap.Width / src.Width) * 4] / 255f;
                    float shade = 1f - ModelTextureData.MetalDarkening * Math.Max(0f, Math.Min(1f, metal));
                    for (int c = 0; c < 3; c++) rgba[i + c] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(src.Rgba[i + c] * tint[c] * shade)));
                    rgba[i + 3] = (byte)Math.Max(0, Math.Min(255, (int)Math.Round(src.Rgba[i + 3] * tint[3])));
                }
            return rgba;
        }

        [Test]
        public void BakingInBandsGivesThePixelsBakingAtOnceGave()
        {
            var texture = Noise(37, 23, 1);
            var metal = Noise(8, 5, 2);
            var tint = new[] { 0.8f, 1.2f, 0.33f, 0.9f };
            foreach (var map in new[] { null, metal })
                foreach (float metallic in new[] { 0f, 0.6f, 1.5f })
                {
                    var expected = BakedPixelByPixel(texture, tint, map, metallic);
                    Assert.That(ModelTextureData.Baked(texture, tint, map, metallic).Rgba, Is.EqualTo(expected), $"at once, map {map != null}, metal {metallic}");
                    var steps = ModelTextureData.BakeSteps(texture, tint, map, metallic, 5).ToList();
                    Assert.That(steps.Count, Is.EqualTo(5), "23 rows in bands of 5: a null after each of the first four bands, then the texture");
                    Assert.That(steps.Take(4).All(s => s == null), Is.True);
                    Assert.That(steps[4].Rgba, Is.EqualTo(expected), $"in bands, map {map != null}, metal {metallic}");
                    Assert.That(steps[4].Width, Is.EqualTo(37));
                }
        }

        // The encoder as it was, float by float: what EncodeSteps must still give.
        static byte[] EncodedFloatByFloat(ModelData model)
        {
            var stream = new MemoryStream();
            var w = new BinaryWriter(stream);
            void Name(string name, int size)
            {
                var bytes = Encoding.UTF8.GetBytes(name ?? "");
                var field = new byte[size];
                Array.Copy(bytes, field, Math.Min(bytes.Length, size - 1));
                w.Write(field);
            }
            Name(model.Name, (int)Wire.ModelNameMax);
            w.Write((uint)model.Textures.Count);
            w.Write((uint)model.Meshes.Count);
            w.Write((uint)model.Materials.Count);
            w.Write((uint)model.Nodes.Count);
            w.Write((uint)model.Lights.Count);
            foreach (var t in model.Textures)
            {
                w.Write((uint)t.Width);
                w.Write((uint)t.Height);
                w.Write(Wire.ModelTextureRgba8);
                w.Write(0u);
                w.Write(t.Rgba);
            }
            foreach (var m in model.Materials)
            {
                w.Write(m.Flags);
                w.Write(m.AlphaCutoff);
                for (int k = 0; k < 3; k++) w.Write(m.Specular[k]);
                w.Write(m.Glossiness);
                for (int k = 0; k < 3; k++) w.Write(m.Emissive[k]);
                w.Write(0u);
            }
            foreach (var n in model.Nodes)
            {
                Name(n.Name, (int)Wire.ModelNodeNameMax);
                w.Write(n.Parent);
                for (int k = 0; k < 3; k++) w.Write(n.Translation[k]);
                for (int k = 0; k < 4; k++) w.Write(n.Rotation[k]);
            }
            foreach (var l in model.Lights)
            {
                w.Write(l.Role);
                for (int k = 0; k < 3; k++) w.Write(l.Color[k]);
                for (int k = 0; k < 3; k++) w.Write(l.Position[k]);
                for (int k = 0; k < 3; k++) w.Write(l.Direction[k]);
            }
            foreach (var m in model.Meshes)
            {
                Name(m.Name, (int)Wire.ModelNameMax);
                w.Write(m.Texture);
                w.Write((uint)m.VertexCount);
                w.Write((uint)m.Indices.Length);
                w.Write(m.Material);
                w.Write(m.Node);
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
            return stream.ToArray();
        }

        static ModelData Sample()
        {
            var random = new Random(7);
            float F() => (float)(random.NextDouble() * 200 - 100);
            var model = new ModelData { Name = "Hayosiko" };
            model.Textures.Add(Noise(16, 8, 3));
            model.Textures.Add(Noise(3, 5, 4));
            model.Materials.Add(new ModelMaterialData { Flags = Wire.MaterialCutout, AlphaCutoff = 0.3f, Glossiness = 0.7f, Specular = new[] { 0.1f, 0.2f, 0.3f } });
            model.Materials.Add(new ModelMaterialData { Flags = Wire.MaterialBlend, Emissive = new[] { 1f, 0.5f, 0f } });
            model.Nodes.Add(new ModelNodeData { Name = "bodymesh", Translation = new[] { F(), F(), F() } });
            model.Nodes.Add(new ModelNodeData { Name = "wheelFL/tire", Parent = 0, Rotation = new[] { 0.5f, 0.5f, 0.5f, 0.5f } });
            model.Lights.Add(new ModelLightData { Role = Wire.LightLow, Position = new[] { F(), F(), F() } });
            for (int k = 0; k < 3; k++)
            {
                int n = 10 + k * 7;
                var mesh = new ModelMeshData
                {
                    Name = "mesh" + k, Texture = k % 2, Material = k % 2, Node = k - 1,
                    Positions = new float[n * 3], Normals = new float[n * 3], Uvs = new float[n * 2], Indices = new ushort[(n - 2) * 3],
                };
                for (int i = 0; i < mesh.Positions.Length; i++) { mesh.Positions[i] = F(); mesh.Normals[i] = F() / 100; }
                for (int i = 0; i < mesh.Uvs.Length; i++) mesh.Uvs[i] = (float)random.NextDouble();
                for (int i = 0; i < mesh.Indices.Length; i++) mesh.Indices[i] = (ushort)random.Next(n);
                model.Meshes.Add(mesh);
            }
            return model;
        }

        [Test]
        public void EncodingInStepsGivesTheBytesEncodingFloatByFloatGave()
        {
            var model = Sample();
            var expected = EncodedFloatByFloat(model);
            Assert.That(model.EncodedSize(), Is.EqualTo(expected.Length));
            Assert.That(model.Encode(), Is.EqualTo(expected));
            var steps = model.EncodeSteps().ToList();
            Assert.That(steps.Count, Is.EqualTo(2 + 3 + 1), "a step per texture and per mesh, then the encoded model");
            Assert.That(steps.Take(5).All(s => s == null), Is.True);
            Assert.That(steps[5].ToArray(), Is.EqualTo(expected));
            Assert.That(steps[5].Length, Is.EqualTo(expected.Length));
            Assert.That(steps[5].Parts, Does.Contain(model.Textures[0].Rgba), "a texture's pixels go in as they are, not copied");
        }

        [Test]
        public void ChunksPutBackTogetherAreTheBlob()
        {
            var blob = EncodedFloatByFloat(Sample());
            var whole = new byte[blob.Length];
            int offset = 0, chunks = 0;
            while (offset < blob.Length)
            {
                var payload = ModelData.Chunk(blob, 9, offset, 100);
                Assert.That(BitConverter.ToUInt32(payload, 8), Is.EqualTo((uint)offset));
                int size = (int)BitConverter.ToUInt32(payload, 12);
                Array.Copy(payload, 16, whole, offset, size);
                offset += size;
                chunks++;
            }
            Assert.That(whole, Is.EqualTo(blob));
            Assert.That(chunks, Is.EqualTo(ModelData.Split(blob, 9, 100).Count));
        }

        [Test]
        public void ACursorCutsTheEncodedPartsIntoTheSameChunks()
        {
            var model = Sample();
            EncodedModel encoded = null;
            foreach (var step in model.EncodeSteps()) if (step != null) encoded = step;
            var blob = encoded.ToArray();
            // Chunks of 100, ending exactly where the first part ends, and the whole model in one.
            foreach (int chunkMax in new[] { 100, encoded.Parts[0].Length, blob.Length })
            {
                var expected = ModelData.Split(blob, 9, chunkMax);
                var cursor = new ChunkCursor(encoded, 9, chunkMax);
                var buffer = new byte[16 + chunkMax];
                int i = 0;
                while (!cursor.Done)
                {
                    int size = cursor.Next(buffer);
                    Assert.That(buffer.Take(size), Is.EqualTo(expected[i]), $"chunk {i} of {chunkMax}");
                    Assert.That(cursor.Next(buffer), Is.EqualTo(size), "made again until it went: the same");
                    cursor.Sent(size);
                    i++;
                }
                Assert.That(i, Is.EqualTo(expected.Count));
                Assert.That(cursor.Offset, Is.EqualTo(blob.Length));
            }
        }
    }
}
