using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class TeimoLineTests
    {
        [Test]
        public void RoundTripsTheClipAndWhereHeStands()
        {
            var payload = TeimoLines.Encode("teimo_huomio_kannin2", new Vec3(21000.5f, -45100f, -80f));
            Assert.That(TeimoLines.Decode(payload, payload.Length, out var clip, out var at), Is.True);
            Assert.That(clip, Is.EqualTo("teimo_huomio_kannin2"));
            Assert.That(at.x, Is.EqualTo(21000.5f));
            Assert.That(at.y, Is.EqualTo(-45100f));
            Assert.That(at.z, Is.EqualTo(-80f));
        }

        [Test]
        public void KeepsUtf8Names()
        {
            var payload = TeimoLines.Encode("älänotku", new Vec3(0f, 0f, 0f));
            Assert.That(TeimoLines.Decode(payload, payload.Length, out var clip, out _), Is.True);
            Assert.That(clip, Is.EqualTo("älänotku"));
        }

        [Test]
        public void RefusesShortOrEmptyMessages()
        {
            var payload = TeimoLines.Encode("paivaa1", new Vec3(0f, 0f, 0f));
            Assert.That(TeimoLines.Decode(payload, payload.Length - 1, out _, out _), Is.False);
            Assert.That(TeimoLines.Decode(new byte[64], 64, out _, out _), Is.False);  // no clip
            payload[12] = 49;  // a length past the clip's room
            Assert.That(TeimoLines.Decode(payload, payload.Length, out _, out _), Is.False);
        }
    }
}
