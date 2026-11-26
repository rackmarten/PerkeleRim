using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class GarageMessagesTests
    {
        [Test]
        public void ABayRequestComesThroughAsSent()
        {
            var sent = new GarageMessages.Request { Generation = 7, What = Wire.BayMoveSatsuma | Wire.BayCheck, Position = new Vec3(14728f, -14243f, -4774f), Heading = 0.553f };
            var bytes = GarageMessages.EncodeBay(sent);
            Assert.That(bytes.Length, Is.EqualTo(GarageMessages.BaySize));
            Assert.That(GarageMessages.DecodeBay(bytes, bytes.Length, out var got), Is.True);
            Assert.That(got.Generation, Is.EqualTo(7u));
            Assert.That(got.What, Is.EqualTo(Wire.BayMoveSatsuma | Wire.BayCheck));
            Assert.That(got.Position.x, Is.EqualTo(14728f));
            Assert.That(got.Position.z, Is.EqualTo(-4774f));
            Assert.That(got.Heading, Is.EqualTo(0.553f));
        }

        [Test]
        public void AShortBayRequestIsRefused()
        {
            var bytes = GarageMessages.EncodeBay(new GarageMessages.Request());
            Assert.That(GarageMessages.DecodeBay(bytes, bytes.Length - 1, out _), Is.False);
            Assert.That(GarageMessages.DecodeBay(null, 32, out _), Is.False);
        }

        [Test]
        public void DoneAnswersWhatWasAskedOrNothing()
        {
            var asked = new GarageMessages.Request { Generation = 3, What = Wire.BayPartsHome };
            var done = GarageMessages.Done(asked, true, true, 80, 4, 0, -1);
            Assert.That(done.generation, Is.EqualTo(3u));
            Assert.That(done.request, Is.EqualTo(Wire.BayPartsHome));
            Assert.That(done.body, Is.EqualTo(1u));
            Assert.That(done.slotted, Is.EqualTo(80u));
            Assert.That(done.missed, Is.EqualTo(0u));
            Assert.That(done.asked, Is.EqualTo(Wire.BayPartsHome));
            var failed = GarageMessages.Done(asked, false, false, 0, 0, 0, 0);
            Assert.That(failed.request, Is.EqualTo(0u));
            Assert.That(failed.asked, Is.EqualTo(Wire.BayPartsHome));
        }
    }
}
