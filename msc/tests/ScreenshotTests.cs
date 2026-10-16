using System;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class ScreenshotTests
    {
        [Test]
        public void PayloadCarriesTheUtf8Path()
        {
            const string path = @"C:\Users\me\Documents\My Games\PerkeleRim\Screenshots\2026-10-17_14-03-22_msc_Päivi.png";
            var payload = Screenshot.Encode(path);
            Assert.That(BitConverter.ToUInt32(payload, 0), Is.EqualTo((uint)path.Length + 1));  // ä is two bytes
            Assert.That(Screenshot.Decode(payload, payload.Length, out var decoded), Is.True);
            Assert.That(decoded, Is.EqualTo(path));
        }

        [Test]
        public void MalformedPayloadsAreRefused()
        {
            Assert.That(Screenshot.Encode(""), Is.Null);
            Assert.That(Screenshot.Encode(new string('a', (int)Wire.ScreenshotPathMax + 1)), Is.Null);
            var payload = Screenshot.Encode("shot.png");
            Assert.That(Screenshot.Decode(payload, 4, out _), Is.False);
            Assert.That(Screenshot.Decode(payload, payload.Length - 1, out _), Is.False);
            Assert.That(Screenshot.Decode(null, 20, out _), Is.False);
        }
    }
}
