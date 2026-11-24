using System;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class SharedTexturesTests
    {
        const int TextureHeader = 68;  // ModelHeader: the first ModelTexture follows it

        static uint next;
        static SharedTextures Shared() => new SharedTextures(() => ++next);

        static ModelData Part(string name, string key)
        {
            var model = new ModelData { Name = name };
            model.Textures.Add(new ModelTextureData { Width = 2, Height = 1, Rgba = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, Key = key });
            var mesh = ModelMeshData.FromUnity("mesh", 0, new float[] { 0, 0, 0, 0, 0, 1, 1, 0, 0 },
                new float[] { 0, 1, 0, 0, 1, 0, 0, 1, 0 }, new float[] { 0, 0, 0, 1, 1, 0.25f }, new[] { 0, 1, 2 }, false);
            model.Meshes.Add(mesh);
            return model;
        }

        static uint Format(byte[] blob) => BitConverter.ToUInt32(blob, TextureHeader + 8);
        static uint Id(byte[] blob) => BitConverter.ToUInt32(blob, TextureHeader + 12);

        [Test]
        public void ASharedTextureGoesInlineOnceThenByItsId()
        {
            var shared = Shared();
            var door = Part("door left(Clone)", "paint");
            var fender = Part("fender left(Clone)", "paint");
            var first = door.Encode(shared);
            var second = fender.Encode(shared);
            Assert.That(Format(first), Is.EqualTo(Wire.ModelTextureRgba8));
            Assert.That(Id(first), Is.Not.Zero);
            Assert.That(BitConverter.ToUInt32(first, TextureHeader + 16), Is.EqualTo(0x04030201u), "its pixels follow");
            Assert.That(Format(second), Is.EqualTo(Wire.ModelTextureShared));
            Assert.That(Id(second), Is.EqualTo(Id(first)));
            Assert.That(second.Length, Is.EqualTo(first.Length - 8), "without the pixels");
            Assert.That(shared.BytesSaved, Is.EqualTo(8));
        }

        [Test]
        public void AModelsOwnTextureAlwaysGoesInlineWithoutAnId()
        {
            var shared = Shared();
            var a = Part("overlay", null).Encode(shared);
            var b = Part("overlay", null).Encode(shared);
            Assert.That(Format(a), Is.EqualTo(Wire.ModelTextureRgba8));
            Assert.That(Id(a), Is.Zero);
            Assert.That(Format(b), Is.EqualTo(Wire.ModelTextureRgba8));
            Assert.That(Id(b), Is.Zero);
        }

        [Test]
        public void WithoutSharingEveryTextureGoesInlineAsBefore()
        {
            var blob = Part("door left(Clone)", "paint").Encode();
            Assert.That(Format(blob), Is.EqualTo(Wire.ModelTextureRgba8));
            Assert.That(Id(blob), Is.Zero);
        }

        [Test]
        public void DifferentTexturesGetDifferentIds()
        {
            var shared = Shared();
            var paint = Part("door", "paint").Encode(shared);
            var rust = Part("seat", "seats").Encode(shared);
            Assert.That(Id(paint), Is.Not.EqualTo(Id(rust)));
            Assert.That(Format(rust), Is.EqualTo(Wire.ModelTextureRgba8));
        }

        [Test]
        public void AfterTheLinkWentDownEveryTextureGoesInlineAgainUnderItsId()
        {
            var shared = Shared();
            var first = Part("door", "paint").Encode(shared);
            shared.Forget();
            var again = Part("door", "paint").Encode(shared);
            Assert.That(Format(again), Is.EqualTo(Wire.ModelTextureRgba8), "Skyrim may have restarted");
            Assert.That(Id(again), Is.EqualTo(Id(first)));
            Assert.That(shared.BytesSaved, Is.Zero);
        }

        [Test]
        public void ClearedTheyGetNewIds()
        {
            var shared = Shared();
            var first = Part("door", "paint").Encode(shared);
            shared.Clear();
            var again = Part("door", "paint").Encode(shared);
            Assert.That(Format(again), Is.EqualTo(Wire.ModelTextureRgba8));
            Assert.That(Id(again), Is.Not.EqualTo(Id(first)), "as after MSC started");
        }
        [Test]
        public void ATextureSkyrimHasNeedsNoPixels()
        {
            var shared = Shared();
            int w, h;
            Assert.That(shared.Sent("paint", out w, out h), Is.False, "nothing sent yet");
            var first = Part("door", "paint").Encode(shared);
            Assert.That(shared.Sent("paint", out w, out h), Is.True);
            Assert.That(w, Is.EqualTo(2));
            Assert.That(h, Is.EqualTo(1));
            var later = Part("fender", "paint");
            later.Textures[0] = new ModelTextureData { Width = w, Height = h, Key = "paint" };  // not read: ModelSender knows Skyrim has it
            var blob = later.Encode(shared);
            Assert.That(Format(blob), Is.EqualTo(Wire.ModelTextureShared));
            Assert.That(Id(blob), Is.EqualTo(Id(first)));
            Assert.That(blob.Length, Is.EqualTo(first.Length - 8));
        }

        [Test]
        public void ATextureWithoutPixelsThatWasNeverSentIsRefused()
        {
            var shared = Shared();
            var part = Part("door", "paint");
            part.Textures[0] = new ModelTextureData { Width = 2, Height = 1, Key = "paint" };
            Assert.Throws<System.ArgumentException>(() => part.Encode(shared));
            shared.Clear();
            Assert.Throws<System.ArgumentException>(() => part.Encode(), "and never without sharing");
        }

        [Test]
        public void AfterTheLinkWentDownNothingCountsAsSent()
        {
            var shared = Shared();
            Part("door", "paint").Encode(shared);
            shared.Forget();
            int w, h;
            Assert.That(shared.Sent("paint", out w, out h), Is.False, "the next model reads it and sends it again");
        }
    }
}
