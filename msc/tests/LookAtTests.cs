using System;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class LookAtTests
    {
        [Test]
        public void EncodesLengthThenUtf8()
        {
            var payload = LookAt.Encode("KÄYNTI");
            Assert.That(BitConverter.ToUInt32(payload, 0), Is.EqualTo(7u));  // Ä is two bytes
            Assert.That(payload.Length, Is.EqualTo(8 + 7));
            Assert.That(LookAt.Decode(payload, payload.Length), Is.EqualTo("KÄYNTI"));
            Assert.That(LookAt.Decode(LookAt.Encode(null), 8), Is.EqualTo(""));
        }

        [Test]
        public void CarriesTheHandIconFlag()
        {
            var payload = LookAt.Encode("", Wire.LookAtUse);
            Assert.That(BitConverter.ToUInt32(payload, 4), Is.EqualTo(Wire.LookAtUse));
            Assert.That(LookAt.Decode(payload, payload.Length, out var flags), Is.EqualTo(""));
            Assert.That(flags, Is.EqualTo(Wire.LookAtUse));
        }

        [Test]
        public void LongTextIsCutAtACharacter()
        {
            var text = new string('a', (int)Wire.LookAtTextMax - 1) + "ää";
            var payload = LookAt.Encode(text);
            Assert.That(LookAt.Decode(payload, payload.Length), Is.EqualTo(new string('a', (int)Wire.LookAtTextMax - 1)));
        }

        [Test]
        public void MalformedPayloadsAreRefused()
        {
            var payload = LookAt.Encode("KEYS");
            Assert.That(LookAt.Decode(payload, 4), Is.Null);
            Assert.That(LookAt.Decode(payload, payload.Length - 1), Is.Null);
            payload[1] = 1;  // length 260
            Assert.That(LookAt.Decode(payload, payload.Length), Is.Null);
        }
    }

    public class CameraPoseTests
    {
        [Test]
        public unsafe void EyeIsConvertedToSkyrimAxesAndUnits()
        {
            var s = new PlayerState { flags = Wire.PlayerValid };
            // 0.85 m up, 0.2 m forward (Unity +z = north = Skyrim +y), level camera.
            CameraPose.Fill(ref s, new Vec3(0f, 0.85f, 0.2f), new Vec3(1f, 0f, 0f), new Vec3(0f, 1f, 0f), 60f);
            Assert.That(s.flags, Is.EqualTo(Wire.PlayerValid | Wire.PlayerCamera));
            Assert.That(s.eye[0], Is.EqualTo(0f).Within(1e-4));
            Assert.That(s.eye[1], Is.EqualTo(0.2 / Coords.MetersPerUnit).Within(1e-2));
            Assert.That(s.eye[2], Is.EqualTo(0.85 / Coords.MetersPerUnit).Within(1e-2));
            Assert.That(s.roll, Is.EqualTo(0f).Within(1e-6));
            Assert.That(s.fov, Is.EqualTo(60f));
        }

        [Test]
        public void TiltingRightIsPositiveRoll()
        {
            double a = 30.0 * Math.PI / 180.0;
            // Head tilted right: the right vector dips, the up vector leans right.
            var right = new Vec3((float)Math.Cos(a), (float)-Math.Sin(a), 0f);
            var up = new Vec3((float)Math.Sin(a), (float)Math.Cos(a), 0f);
            Assert.That(CameraPose.Roll(right, up), Is.EqualTo(a).Within(1e-5));
            Assert.That(CameraPose.Roll(new Vec3(right.x, -right.y, 0f), new Vec3(-up.x, up.y, 0f)), Is.EqualTo(-a).Within(1e-5));
        }
    }
}
