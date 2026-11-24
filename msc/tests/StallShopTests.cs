using System;
using NUnit.Framework;
using PerkeleRim.Protocol;

namespace PerkeleRim.Tests
{
    public class StallShopTests
    {
        // Teimo's real spot and heading (PerkeleRim.esp), the counter 55 units out in front of him at 80 up.
        static StallShop.Stall Stall()
        {
            var stall = new StallShop.Stall
            {
                Generation = 4,
                Teimo = new Vec3(24960f, -41990f, -171f),
                Heading = 5.06f,
                CounterHalf = new Vec3(40f, 25f, 40f),
            };
            stall.Counter = At(stall, 0f, 55f, 80f);
            stall.Goods.Add(new StallShop.Good { Product = Wire.ShopBeer, Position = At(stall, 120f, 108f, 10f), Heading = 1f });
            stall.Goods.Add(new StallShop.Good { Product = Wire.ShopCigarettes, Position = At(stall, -55f, 60f, 85f), Heading = 2f });
            return stall;
        }

        // A point of the stall's frame (teimo_shop.h StallToWorld) in Skyrim's world.
        static Vec3 At(StallShop.Stall stall, float x, float y, float z)
        {
            float c = (float)Math.Cos(stall.Heading), s = (float)Math.Sin(stall.Heading);
            return new Vec3(stall.Teimo.x + x * c + y * s, stall.Teimo.y - x * s + y * c, stall.Teimo.z + z);
        }

        [Test]
        public void RoundTripsTheStall()
        {
            var stall = Stall();
            var payload = StallShop.Encode(stall);
            Assert.That(payload.Length, Is.EqualTo(368));
            Assert.That(StallShop.Decode(payload, payload.Length, out var got), Is.True);
            Assert.That(got.Generation, Is.EqualTo(4u));
            Assert.That(got.Heading, Is.EqualTo(5.06f));
            Assert.That(got.Counter.y, Is.EqualTo(stall.Counter.y));
            Assert.That(got.CounterHalf.z, Is.EqualTo(40f));
            Assert.That(got.Goods.Count, Is.EqualTo(2));
            Assert.That(got.Goods[1].Product, Is.EqualTo(Wire.ShopCigarettes));
            Assert.That(got.Goods[1].Position.x, Is.EqualTo(stall.Goods[1].Position.x));
            Assert.That(got.Goods[1].Heading, Is.EqualTo(2f));
        }

        [Test]
        public void RefusesShortStallsAndTooManyGoods()
        {
            var payload = StallShop.Encode(Stall());
            Assert.That(StallShop.Decode(payload, payload.Length - 1, out _), Is.False);
            payload[4] = 17;  // count past kStallGoodsMax
            Assert.That(StallShop.Decode(payload, payload.Length, out _), Is.False);
            Assert.That(StallShop.DecodePay(new byte[7], 7, out _), Is.False);
        }

        [Test]
        public void TheStallFrameUndoesItsPlacement()
        {
            var stall = Stall();
            var local = StallShop.ToStall(At(stall, 12f, -34f, 56f), stall.Teimo, stall.Heading);
            Assert.That(local.x, Is.EqualTo(12f).Within(0.01f));
            Assert.That(local.y, Is.EqualTo(-34f).Within(0.01f));
            Assert.That(local.z, Is.EqualTo(56f).Within(0.01f));
        }

        [Test]
        public void ACaseOnTheCounterIsOnTheBillOneBesideItIsNot()
        {
            var stall = Stall();
            Assert.That(StallShop.OnCounter(At(stall, 0f, 55f, 82f), stall), Is.True);
            Assert.That(StallShop.OnCounter(At(stall, 35f, 40f, 70f), stall), Is.True);   // near a corner, sunk into the board a little
            Assert.That(StallShop.OnCounter(At(stall, 55f, 60f, 68f), stall), Is.False);  // the counter's end, where the cigarettes stand
            Assert.That(StallShop.OnCounter(At(stall, 0f, 95f, 82f), stall), Is.False);   // in front of it
            Assert.That(StallShop.OnCounter(At(stall, 0f, 55f, 30f), stall), Is.False);   // under it
            Assert.That(StallShop.OnCounter(At(stall, 0f, 55f, 140f), stall), Is.False);  // held up over it
        }

        [Test]
        public void GoodsPastEightMetresFromTeimoAreTakenAway()
        {
            var stall = Stall();
            Assert.That(StallShop.TakenAway(At(stall, 0f, 550f, 0f), stall), Is.False);
            Assert.That(StallShop.TakenAway(At(stall, 0f, 570f, 0f), stall), Is.True);
            Assert.That(StallShop.TakenAway(At(stall, 400f, 400f, 0f), stall), Is.True);  // 566 units
            Assert.That(StallShop.KumpoAway(At(stall, 0f, 1000f, 0f), stall), Is.False);
            Assert.That(StallShop.KumpoAway(At(stall, 0f, 1100f, 0f), stall), Is.True);
        }

        [Test]
        public void TheBillAddsMscsPricesAndKnowsTheKinds()
        {
            var bill = new StallShop.Bill();
            bill.Add(Wire.ShopBeer, 149f);
            bill.Add(Wire.ShopMilk, 7.579001f);
            bill.Add(Wire.ShopBeer, 149f);
            Assert.That(bill.Count, Is.EqualTo(3));
            Assert.That(bill.Total, Is.EqualTo(305.579f).Within(0.001f));
            Assert.That(bill.Products, Is.EqualTo((1u << (int)Wire.ShopBeer) | (1u << (int)Wire.ShopMilk)));
            var same = new StallShop.Bill();
            same.Add(Wire.ShopMilk, 7.579001f);
            same.Add(Wire.ShopBeer, 298f);
            same.Count = 3;
            Assert.That(bill.Same(same), Is.True);
            Assert.That(bill.Same(new StallShop.Bill()), Is.False);
        }

        [Test]
        public void PaysWhileTheTotalIsNoMoreThanTheMoneyAsMscsRegisterDoes()
        {
            Assert.That(StallShop.CanPay(149f, 149f), Is.True);
            Assert.That(StallShop.CanPay(149f, 300.73f), Is.True);
            Assert.That(StallShop.CanPay(149f, 148.99f), Is.False);
            Assert.That(StallShop.CanPay(149f, 20.73f), Is.False);
        }

        [Test]
        public void PromptsAsMscsShopDoes()
        {
            Assert.That(StallShop.Prompt(Wire.ShopBeer, 149f), Is.EqualTo("BEER 149.00 MK"));
            Assert.That(StallShop.Prompt(Wire.ShopSausages, 15.0891f), Is.EqualTo("SAUSAGES 15.09 MK"));
            Assert.That(StallShop.Prompt(99, 3f), Is.EqualTo("3.00 MK"));
        }

        [Test]
        public void EveryProductHasItsMscNamesAndAPrice()
        {
            Assert.That(StallShop.Products.Length, Is.EqualTo((int)Wire.ShopProducts));
            foreach (var p in StallShop.Products)
            {
                Assert.That(p.Name, Is.Not.Empty);
                Assert.That(p.Group, Is.EqualTo("FoodProducts").Or.EqualTo("CarProducts"));
                Assert.That(p.Label, Is.EqualTo(p.Label.ToUpperInvariant()));
                Assert.That(p.Price, Is.GreaterThan(0f));
            }
            Assert.That(StallShop.Products[Wire.ShopBeer].Name, Is.EqualTo("Beer"));
            Assert.That(StallShop.Products[Wire.ShopBeer].Price, Is.EqualTo(149f));
        }

        [Test]
        public void TheShopEventRoundTrips()
        {
            var bill = new StallShop.Bill();
            bill.Add(Wire.ShopBeer, 149f);
            var payload = StallShop.EncodeShop(Wire.ShopPaid, bill, 151.73f, 3);
            Assert.That(payload.Length, Is.EqualTo(24));
            Assert.That(StallShop.DecodeShop(payload, payload.Length, out var shop), Is.True);
            Assert.That(shop.kind, Is.EqualTo(Wire.ShopPaid));
            Assert.That(shop.count, Is.EqualTo(1u));
            Assert.That(shop.total, Is.EqualTo(149f));
            Assert.That(shop.money, Is.EqualTo(151.73f));
            Assert.That(shop.products, Is.EqualTo(1u));
            Assert.That(shop.seq, Is.EqualTo(3u));
        }
    }
}
