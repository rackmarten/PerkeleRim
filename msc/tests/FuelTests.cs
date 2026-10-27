using System;
using System.Collections.Generic;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class FuelTests
    {
        static byte[] Payload(uint count, int pumps)
        {
            var bytes = new List<byte>();
            bytes.AddRange(BitConverter.GetBytes(count));
            bytes.AddRange(BitConverter.GetBytes(0u));
            for (int i = 0; i < pumps; i++)
                foreach (var f in new[] { 1000f * i, -2000f * i, -4800f + i, 0.5f * i })
                    bytes.AddRange(BitConverter.GetBytes(f));
            return bytes.ToArray();
        }

        static Fuel.Pump PumpAt(float x, float y, float heading = 0f) =>
            new Fuel.Pump { Position = new Vec3(x, y, 0f), Heading = heading };

        [Test]
        public void DecodesThePumpsInOrder()
        {
            var payload = Payload(3, (int)Wire.FuelPumpsMax);
            Assert.That(Fuel.Decode(payload, payload.Length, out var pumps), Is.True);
            Assert.That(pumps.Count, Is.EqualTo(3));
            Assert.That(pumps[2].Position.x, Is.EqualTo(2000f));
            Assert.That(pumps[2].Position.y, Is.EqualTo(-4000f));
            Assert.That(pumps[2].Position.z, Is.EqualTo(-4798f));
            Assert.That(pumps[2].Heading, Is.EqualTo(1f));
        }

        [Test]
        public void RejectsATruncatedOrOversizedList()
        {
            var shortPayload = Payload(3, 2);
            Assert.That(Fuel.Decode(shortPayload, shortPayload.Length, out _), Is.False);
            var tooMany = Payload(Wire.FuelPumpsMax + 1, (int)Wire.FuelPumpsMax + 1);
            Assert.That(Fuel.Decode(tooMany, tooMany.Length, out _), Is.False);
            Assert.That(Fuel.Decode(new byte[4], 4, out _), Is.False);
            Assert.That(Fuel.Decode(null, 0, out _), Is.False);
        }

        [Test]
        public void FindsTheNearestPumpWithinReach()
        {
            var pumps = new List<Fuel.Pump> { PumpAt(0f, 0f), PumpAt(5000f, 0f), PumpAt(5000f, 1500f) };
            Assert.That(Fuel.Nearest(pumps, new Vec3(4900f, 400f, 900f), Fuel.TowReach), Is.EqualTo(1));
            Assert.That(Fuel.Nearest(pumps, new Vec3(4900f, 1100f, 0f), Fuel.TowReach), Is.EqualTo(2));
            Assert.That(Fuel.Nearest(pumps, new Vec3(2500f, 0f, 0f), Fuel.TowReach), Is.EqualTo(-1));
            Assert.That(Fuel.Nearest(new List<Fuel.Pump>(), new Vec3(0f, 0f, 0f), Fuel.TowReach), Is.EqualTo(-1));
        }

        [Test]
        public void DryAndLowTanks()
        {
            Assert.That(Fuel.Dry(0f), Is.True);
            Assert.That(Fuel.Dry(0.2f), Is.True);
            Assert.That(Fuel.Dry(0.5f), Is.False);
            Assert.That(Fuel.Low(9.9f, 40f), Is.True);
            Assert.That(Fuel.Low(10f, 40f), Is.False);
            Assert.That(Fuel.Low(1f, 0f), Is.False);
        }

        [Test]
        public void PaysForEveryLitreWhileTheMoneyLasts()
        {
            float money = 100.5f;
            Assert.That(Fuel.Pay(4f, 2f, ref money), Is.EqualTo(4f));
            Assert.That(money, Is.EqualTo(92.5f).Within(1e-4f));
            Assert.That(Fuel.Pay(0f, 2f, ref money), Is.EqualTo(0f));
            Assert.That(Fuel.Pay(-1f, 2f, ref money), Is.EqualTo(0f));
            Assert.That(money, Is.EqualTo(92.5f).Within(1e-4f));
        }

        [Test]
        public void PaysOnlyForWhatTheMoneyCovers()
        {
            float money = 3f;
            Assert.That(Fuel.Pay(4f, 2f, ref money), Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(money, Is.EqualTo(0f).Within(1e-5f));
            Assert.That(Fuel.Broke(money, 2f), Is.True);

            money = -1f;  // never pays with debt
            Assert.That(Fuel.Pay(1f, 2f, ref money), Is.EqualTo(0f));
            Assert.That(money, Is.EqualTo(-1f));
        }

        [Test]
        public void FreeWithoutAPrice()
        {
            float money = 0f;
            Assert.That(Fuel.Pay(5f, 0f, ref money), Is.EqualTo(5f));
            Assert.That(money, Is.EqualTo(0f));
            Assert.That(Fuel.Broke(0f, 0f), Is.False);
        }

        [Test]
        public void BrokeUnderATenthOfALitre()
        {
            Assert.That(Fuel.Broke(0.19f, 2f), Is.True);
            Assert.That(Fuel.Broke(0.2f, 2f), Is.False);
        }

        [Test]
        public void TowingNeedsTheFee()
        {
            Assert.That(Fuel.CanTow(49.99f), Is.False);
            Assert.That(Fuel.CanTow(50f), Is.True);
        }

        [Test]
        public void TowedVehicleStandsSideOnWithItsFillerAtThePump()
        {
            // Pump north of its spot: the lane is south of the spot, the vehicle drives east or west.
            var pump = PumpAt(1000f, 2000f, 0f);
            Fuel.TowSpot(pump, -1, 0f, out var left, out var leftHeading);
            Assert.That(left.x, Is.EqualTo(1000f).Within(1e-3f));
            Assert.That(left.y, Is.EqualTo(2000f - Fuel.LaneOffset).Within(1e-3f));
            Assert.That(leftHeading, Is.EqualTo((float)(Math.PI / 2)).Within(1e-5f));  // east: its left is north

            Fuel.TowSpot(pump, 1, 0f, out _, out var rightHeading);
            Assert.That(rightHeading, Is.EqualTo((float)(3 * Math.PI / 2)).Within(1e-5f));  // west: its right is north

            // A filler 100 units behind the middle: the middle is 100 units further ahead (east).
            Fuel.TowSpot(pump, -1, -100f, out var back, out _);
            Assert.That(back.x, Is.EqualTo(1100f).Within(1e-3f));
            Assert.That(back.y, Is.EqualTo(2000f - Fuel.LaneOffset).Within(1e-3f));
        }

        [Test]
        public void HudLines()
        {
            Assert.That(Fuel.PourLine(12.345f, 24.7f, 32.06f, 40f), Is.EqualTo("Dwemer oil 12.3 L, 25 gold, tank 32.1 / 40 L"));
            Assert.That(Fuel.LowLine(8.26f, 40f), Is.EqualTo("Fuel 8.3 / 40 L"));
            Assert.That(Fuel.DryLine, Does.Contain("50 gold"));
        }
    }
}
