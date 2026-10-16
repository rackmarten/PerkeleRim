using System;
using System.Collections.Generic;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class ShelfStockTests
    {
        static byte[] Payload(uint generation, uint count, int spots)
        {
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(generation));
            bytes.AddRange(BitConverter.GetBytes(count));
            for (int i = 0; i < spots; i++)
                foreach (var f in new[] { 100f * i, -200f * i, -4700f + i, 0.5f * i })
                    bytes.AddRange(BitConverter.GetBytes(f));
            return bytes.ToArray();
        }

        [Test]
        public void DecodesTheSpotsInOrderWithTheirItems()
        {
            var payload = Payload(7, 3, (int)Wire.StockSpotsMax);
            Assert.That(ShelfStock.Decode(payload, payload.Length, out var generation, out var spots), Is.True);
            Assert.That(generation, Is.EqualTo(7u));
            Assert.That(spots.Count, Is.EqualTo(3));
            Assert.That(spots[2].Position.x, Is.EqualTo(200f));
            Assert.That(spots[2].Position.y, Is.EqualTo(-400f));
            Assert.That(spots[2].Position.z, Is.EqualTo(-4698f));
            Assert.That(spots[2].Heading, Is.EqualTo(1f));
            Assert.That(spots[0].Item, Is.EqualTo("Beer"));
            Assert.That(spots[2].Item, Is.EqualTo("Sausages"));
        }

        [Test]
        public void ItemsStartOverPastTheList()
        {
            Assert.That(ShelfStock.ItemFor(ShelfStock.Items.Length), Is.EqualTo(ShelfStock.Items[0]));
            Assert.That(ShelfStock.ItemFor(5), Is.EqualTo("MacaronBox"));
        }

        [Test]
        public void RefusesShortOrOversizedPayloads()
        {
            var shortOne = Payload(1, 4, 3);
            Assert.That(ShelfStock.Decode(shortOne, shortOne.Length, out _, out _), Is.False);
            var tooMany = Payload(1, Wire.StockSpotsMax + 1, (int)Wire.StockSpotsMax + 1);
            Assert.That(ShelfStock.Decode(tooMany, tooMany.Length, out _, out _), Is.False);
            Assert.That(ShelfStock.Decode(new byte[4], 4, out _, out _), Is.False);
        }
    }
}
