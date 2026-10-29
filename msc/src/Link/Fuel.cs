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
        public const float FreeTowReach = 7000f;  // units (100 m): a dry vehicle this close to the pump is towed for nothing
        public const float TowReach = 2100f;      // units (30 m): a call this close to a pump tows
        public const float LaneOffset = 134f;     // units from the pump's spot to the lane's middle
        public const float DryLevel = 0.25f;      // litres: at or below, the tank counts as dry (the van stalls at 0.2)
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

        // A tank's capacity in litres: its own MaxCapacity when it has one (read, NaN without), else
        // the size known for the vehicle (the Gifu's FuelTank :: Data has only FuelLevel), else 0,
        // which counts as no tank FuelLink can fill or show.
        public static float Capacity(float read, float known) =>
            !float.IsNaN(read) && read > 0f ? read : Math.Max(0f, known);

        // The capacity a tank without MaxCapacity has by its cap trigger (CapTrigger_Fuel<vehicle> ::
        // Trigger): a float of its own named like a maximum or a capacity, else the largest constant
        // it compares a level or fuel float against (where the pour stops), else 0. floats: its
        // variables; compares: its FloatCompare actions as (variable, constant).
        public static float PourLimit(IEnumerable<KeyValuePair<string, float>> floats, IEnumerable<KeyValuePair<string, float>> compares)
        {
            foreach (var f in floats)
                if (f.Value > 0f && (f.Key.IndexOf("Capacity", StringComparison.OrdinalIgnoreCase) >= 0 || f.Key.StartsWith("Max", StringComparison.OrdinalIgnoreCase)))
                    return f.Value;
            float best = 0f;
            foreach (var c in compares)
                if ((c.Key.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0 || c.Key.IndexOf("Fuel", StringComparison.OrdinalIgnoreCase) >= 0) && c.Value > best)
                    best = c.Value;
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

        // What towing a dry vehicle standing at `at` onto the pump costs: nothing when it is already
        // close (it rolled off after the last tow), else TowFee.
        public static float TowFeeFrom(Vec3 at, Pump pump)
        {
            float dx = at.x - pump.Position.x, dy = at.y - pump.Position.y;
            return dx * dx + dy * dy <= FreeTowReach * FreeTowReach ? 0f : TowFee;
        }

        public static bool CanTow(float money, float fee) => money >= fee;

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
        public static string TowedLine(float fee) => fee > 0f ? $"Towed to the pump for {fee:0} gold" : "Towed back to the pump";
    }
}
