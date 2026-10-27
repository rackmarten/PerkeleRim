using System;
using System.Collections.Generic;
using System.Globalization;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // Fuel along Skyrim's roads (docs/PROTOCOL.md, "Fuel"): the dwemer oil pumps Skyrim sends
    // (FuelStations), paying for what MSC pours as it pours, towing a dry vehicle onto a pump, and
    // the HUD's fuel line. Positions are Skyrim world units, headings Skyrim radians (clockwise from
    // north). Unity-free.
    public static class Fuel
    {
        public const float TowFee = 50f;          // marks (gold) to tow a dry vehicle onto a pump
        public const float TowReach = 2100f;      // units (30 m): a call this close to a pump tows
        public const float LaneOffset = 134f;     // units from the pump's spot to the lane's middle
        public const float DryLevel = 0.05f;      // litres: at or below, the tank counts as dry
        public const float LowShare = 0.25f;      // the HUD warns under this share of the tank
        public const float MinLitres = 0.1f;      // money for less than this stops the pour
        public const string Name = "Dwemer oil";

        public struct Pump
        {
            public Vec3 Position;  // on the ground in front of the pump, where Kumpo stands
            public float Heading;  // from that spot toward the pump
        }

        // False when the payload is too short for the pump count it claims.
        public static bool Decode(byte[] payload, int size, out List<Pump> pumps)
        {
            pumps = new List<Pump>();
            const int header = 8, pump = 16;
            if (payload == null || size < header || size > payload.Length) return false;
            uint count = BitConverter.ToUInt32(payload, 0);
            if (count > Wire.FuelPumpsMax || size < header + count * pump) return false;
            for (int i = 0; i < count; i++)
            {
                int at = header + i * pump;
                pumps.Add(new Pump
                {
                    Position = new Vec3(BitConverter.ToSingle(payload, at), BitConverter.ToSingle(payload, at + 4), BitConverter.ToSingle(payload, at + 8)),
                    Heading = BitConverter.ToSingle(payload, at + 12),
                });
            }
            return true;
        }

        // The pump nearest at within reach (units, on the ground plane), -1 for none.
        public static int Nearest(IList<Pump> pumps, Vec3 at, float reach)
        {
            int best = -1;
            float bestSq = reach * reach;
            for (int i = 0; i < pumps.Count; i++)
            {
                float dx = pumps[i].Position.x - at.x, dy = pumps[i].Position.y - at.y;
                float sq = dx * dx + dy * dy;
                if (sq <= bestSq)
                {
                    bestSq = sq;
                    best = i;
                }
            }
            return best;
        }

        public static bool Dry(float level) => level <= DryLevel;

        public static bool Low(float level, float capacity) => capacity > 0f && level < capacity * LowShare;

        // Pays for poured litres at price marks a litre out of money (MSC's PlayerMoney, pennies
        // kept) and returns the litres paid for: all of them, or what the money covers (the caller
        // takes the rest back out of the tank). Free with no price.
        public static float Pay(float poured, float price, ref float money)
        {
            if (poured <= 0f) return 0f;
            if (price <= 0f) return poured;
            float cost = poured * price;
            if (money >= cost)
            {
                money -= cost;
                return poured;
            }
            float paid = Math.Max(0f, money) / price;
            money -= paid * price;
            return paid;
        }

        // Too little money left to pour any more.
        public static bool Broke(float money, float price) => price > 0f && money < price * MinLitres;

        public static bool CanTow(float money) => money >= TowFee;

        // Where a towed vehicle stands at pump: in the lane beside it, side on, with its filler
        // (fillerSide +1 on its right, -1 on its left; fillerForward units ahead of its middle,
        // negative behind) next to the pump's spot.
        public static void TowSpot(Pump pump, int fillerSide, float fillerForward, out Vec3 position, out float heading)
        {
            heading = Normalize(pump.Heading - fillerSide * (float)(Math.PI / 2));
            float awayX = -(float)Math.Sin(pump.Heading), awayY = -(float)Math.Cos(pump.Heading);
            float aheadX = (float)Math.Sin(heading), aheadY = (float)Math.Cos(heading);
            position = new Vec3(pump.Position.x + awayX * LaneOffset - aheadX * fillerForward,
                pump.Position.y + awayY * LaneOffset - aheadY * fillerForward, pump.Position.z);
        }

        static float Normalize(float a)
        {
            const float tau = (float)(2 * Math.PI);
            a %= tau;
            return a < 0f ? a + tau : a;
        }

        // ---- The HUD's fuel line ----

        // Invariant: MSC runs with Windows' culture, which may write 12,3.
        public static string PourLine(float litres, float paid, float level, float capacity) =>
            string.Format(CultureInfo.InvariantCulture, "{0} {1:0.0} L, {2:0} gold, tank {3:0.0} / {4:0} L", Name, litres, Math.Round(paid), level, capacity);

        public static string LowLine(float level, float capacity) =>
            string.Format(CultureInfo.InvariantCulture, "Fuel {0:0.0} / {1:0} L", level, capacity);

        public const string BrokeLine = "Out of gold";
        public static string DryLine => $"The tank is dry. Call it at a {Name} pump: {TowFee:0} gold";
        public static string NoTowMoneyLine => $"Towing costs {TowFee:0} gold";
        public static string TowedLine => $"Towed to the pump for {TowFee:0} gold";
    }
}
