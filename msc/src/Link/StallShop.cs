using System;
using System.Collections.Generic;
using System.Globalization;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // Shopping at Teimo's stall (TeimoStall, TeimoPay, TeimoShop; protocol/cpp/teimo_shop.h has the
    // stall's half): which of MSC's shop goods the stall sells, what lies on the counter, what's
    // been carried off, and whether Kumpo can pay. MSC's shop works the same way (docs/FSM_MAP.md,
    // "The store"): a product's Buy FSM adds its Price to the register's total, and the register
    // takes the total off PlayerMoney when it is no more than he has. Unity-free.
    public static class StallShop
    {
        public struct Product
        {
            public string Name;     // MSC's product: its Buy FSM's object under STORE/LOD/ActivateStore and its Spawner/CreateItems FSM
            public string Group;    // FoodProducts or CarProducts
            public string Label;    // its prompt's name, as MSC's Buy FSM writes it
            public float Price;     // marks, MSC's price when its Buy FSM can't be read
        }

        // By Wire.Shop* (the prices are MSC's own, read from its Buy FSMs on 24 November 2026).
        public static readonly Product[] Products =
        {
            new Product { Name = "Beer", Group = "FoodProducts", Label = "BEER", Price = 149f },
            new Product { Name = "Sausages", Group = "FoodProducts", Label = "SAUSAGES", Price = 15.0891f },
            new Product { Name = "Pizza", Group = "FoodProducts", Label = "PIZZA", Price = 13.7111f },
            new Product { Name = "Chips", Group = "FoodProducts", Label = "CHIPS", Price = 20.6011f },
            new Product { Name = "MacaronBox", Group = "FoodProducts", Label = "MACARON BOX", Price = 16.4671f },
            new Product { Name = "Juice", Group = "FoodProducts", Label = "JUICE CONCENTRATE", Price = 17.8451f },
            new Product { Name = "Milk", Group = "FoodProducts", Label = "MILK", Price = 7.579001f },
            new Product { Name = "Cigarettes", Group = "FoodProducts", Label = "CIGARETTES", Price = 13.95f },
            new Product { Name = "MotorOil", Group = "CarProducts", Label = "MOTOR OIL", Price = 149f },
            new Product { Name = "Coffee", Group = "FoodProducts", Label = "COFFEE", Price = 27.4911f },
            new Product { Name = "MosquitoSpray", Group = "FoodProducts", Label = "MOSQUITO SPRAY", Price = 32.95f },
        };

        public const float Around = 560f;    // units (8 m) from Teimo: a good carried past this is stolen (teimo_talk.h kAround)
        public const float Away = 1050f;     // units (15 m): with Kumpo this far, Teimo sets out new goods for the gone ones (kGone)
        public const float CounterBelow = 20f;  // units under the counter's top a good's origin may still sit (it sinks into the board a little)

        public struct Good
        {
            public uint Product;
            public Vec3 Position;  // Skyrim world space
            public float Heading;
        }

        public sealed class Stall
        {
            public uint Generation;
            public Vec3 Teimo;
            public float Heading;
            public Vec3 Counter;
            public Vec3 CounterHalf;
            public readonly List<Good> Goods = new List<Good>();
        }

        public static bool Known(uint product) => product < Products.Length;

        // False for a malformed message.
        public static unsafe bool Decode(byte[] payload, int size, out Stall stall)
        {
            stall = null;
            if (payload == null || size < sizeof(TeimoStall) || payload.Length < sizeof(TeimoStall)) return false;
            TeimoStall m;
            fixed (byte* p = payload) m = *(TeimoStall*)p;
            if (m.count > Wire.StallGoodsMax) return false;
            stall = new Stall
            {
                Generation = m.generation,
                Teimo = new Vec3(m.teimo[0], m.teimo[1], m.teimo[2]),
                Heading = m.heading,
                Counter = new Vec3(m.counter[0], m.counter[1], m.counter[2]),
                CounterHalf = new Vec3(m.counterHalf[0], m.counterHalf[1], m.counterHalf[2]),
            };
            for (int i = 0; i < m.count; i++)
            {
                byte* g = m.goods + i * sizeof(StallGood);
                var good = *(StallGood*)g;
                stall.Goods.Add(new Good
                {
                    Product = good.product,
                    Position = new Vec3(good.position[0], good.position[1], good.position[2]),
                    Heading = good.heading,
                });
            }
            return true;
        }

        // The message for a stall (tests; Skyrim makes its own, teimo_shop.h MakeStall).
        public static unsafe byte[] Encode(Stall stall)
        {
            var m = new TeimoStall { generation = stall.Generation, count = (uint)Math.Min(stall.Goods.Count, (int)Wire.StallGoodsMax), heading = stall.Heading };
            m.teimo[0] = stall.Teimo.x; m.teimo[1] = stall.Teimo.y; m.teimo[2] = stall.Teimo.z;
            m.counter[0] = stall.Counter.x; m.counter[1] = stall.Counter.y; m.counter[2] = stall.Counter.z;
            m.counterHalf[0] = stall.CounterHalf.x; m.counterHalf[1] = stall.CounterHalf.y; m.counterHalf[2] = stall.CounterHalf.z;
            for (int i = 0; i < m.count; i++)
            {
                var good = new StallGood { product = stall.Goods[i].Product, heading = stall.Goods[i].Heading };
                good.position[0] = stall.Goods[i].Position.x;
                good.position[1] = stall.Goods[i].Position.y;
                good.position[2] = stall.Goods[i].Position.z;
                *(StallGood*)(m.goods + i * sizeof(StallGood)) = good;
            }
            var payload = new byte[sizeof(TeimoStall)];
            fixed (byte* p = payload) *(TeimoStall*)p = m;
            return payload;
        }

        public static unsafe bool DecodePay(byte[] payload, int size, out uint seq)
        {
            seq = 0;
            if (payload == null || size < sizeof(TeimoPay) || payload.Length < sizeof(TeimoPay)) return false;
            fixed (byte* p = payload) seq = ((TeimoPay*)p)->seq;
            return true;
        }

        public static unsafe byte[] EncodeShop(uint kind, Bill bill, float money, uint seq)
        {
            var m = new TeimoShop { kind = kind, count = (uint)bill.Count, total = bill.Total, money = money, products = bill.Products, seq = seq };
            var payload = new byte[sizeof(TeimoShop)];
            fixed (byte* p = payload) *(TeimoShop*)p = m;
            return payload;
        }

        public static unsafe bool DecodeShop(byte[] payload, int size, out TeimoShop shop)
        {
            shop = default(TeimoShop);
            if (payload == null || size < sizeof(TeimoShop) || payload.Length < sizeof(TeimoShop)) return false;
            fixed (byte* p = payload) shop = *(TeimoShop*)p;
            return true;
        }

        // A point of Skyrim's world in the stall's frame (teimo_shop.h: +x Teimo's right, +y toward
        // the customers), relative to a_origin.
        public static Vec3 ToStall(Vec3 p, Vec3 origin, float heading)
        {
            float c = (float)Math.Cos(heading), s = (float)Math.Sin(heading);
            float dx = p.x - origin.x, dy = p.y - origin.y;
            return new Vec3(dx * c - dy * s, dx * s + dy * c, p.z - origin.z);
        }

        // A good at p (its origin, Skyrim world space) lies on the counter: over its top, from a
        // little under the board up to CounterHalf.z above it.
        public static bool OnCounter(Vec3 p, Stall stall)
        {
            var local = ToStall(p, stall.Counter, stall.Heading);
            return Math.Abs(local.x) <= stall.CounterHalf.x && Math.Abs(local.y) <= stall.CounterHalf.y && local.z >= -CounterBelow && local.z <= stall.CounterHalf.z;
        }

        static float Flat(Vec3 a, Vec3 b) => (float)Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));

        // A good at p has left the stall (carried, thrown or driven off): stolen unless paid for.
        public static bool TakenAway(Vec3 p, Stall stall) => Flat(p, stall.Teimo) > Around;

        // Kumpo at p is away from the stall: Teimo restocks behind his back.
        public static bool KumpoAway(Vec3 p, Stall stall) => Flat(p, stall.Teimo) > Away;

        public struct Bill
        {
            public int Count;
            public float Total;     // marks
            public uint Products;   // bit (1 << Wire.Shop*) per kind

            public void Add(uint product, float price)
            {
                Count++;
                Total += price;
                if (product < 32) Products |= 1u << (int)product;
            }

            public bool Same(Bill other) => Count == other.Count && Math.Abs(Total - other.Total) < 0.005f && Products == other.Products;
        }

        // MSC's register: Check money lets the purchase through while the total is no more than
        // PlayerMoney ("NOT ENOUGH MONEY" otherwise), then takes the total off it.
        public static bool CanPay(float total, float money) => total <= money;

        // What MSC's Buy FSM shows while its crosshair is on a product: "BEER 149.00 MK".
        public static string Prompt(uint product, float price)
        {
            string marks = price.ToString("0.00", CultureInfo.InvariantCulture) + " MK";
            return Known(product) ? Products[product].Label + " " + marks : marks;
        }

        public const string NotEnough = "NOT ENOUGH MONEY";  // MSC's register's prompt when he can't pay, for 3 s
        public const float NotEnoughSeconds = 3f;
    }
}
