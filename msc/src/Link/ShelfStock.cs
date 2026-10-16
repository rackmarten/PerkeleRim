using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // What goes on the garage's shelves (StockShelves): one MSC store item per spot, by spot index,
    // named by the Spawner/CreateItems FSM that makes it (docs/FSM_MAP.md, Carrying items). Spots
    // past the list start it over. Unity-free.
    public static class ShelfStock
    {
        public static readonly string[] Items = { "Beer", "Beer", "Sausages", "Pizza", "Chips", "MacaronBox" };

        public struct Spot
        {
            public string Item;     // Spawner/CreateItems FSM name
            public Vec3 Position;   // Skyrim world space
            public float Heading;   // Skyrim radians, clockwise from north
        }

        public static string ItemFor(int index) => Items[index % Items.Length];

        // False when the payload is too short for the spot count it claims.
        public static bool Decode(byte[] payload, int size, out uint generation, out List<Spot> spots)
        {
            generation = 0;
            spots = new List<Spot>();
            const int header = 8, spot = 16;
            if (payload == null || size < header || size > payload.Length) return false;
            generation = BitConverter.ToUInt32(payload, 0);
            uint count = BitConverter.ToUInt32(payload, 4);
            if (count > Wire.StockSpotsMax || size < header + count * spot) return false;
            for (int i = 0; i < count; i++)
            {
                int at = header + i * spot;
                spots.Add(new Spot
                {
                    Item = ItemFor(i),
                    Position = new Vec3(BitConverter.ToSingle(payload, at), BitConverter.ToSingle(payload, at + 4), BitConverter.ToSingle(payload, at + 8)),
                    Heading = BitConverter.ToSingle(payload, at + 12),
                });
            }
            return true;
        }
    }
}
