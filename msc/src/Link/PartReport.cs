using System;
using System.Text;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // The PartState message (v55): one of the Satsuma's parts as MSC's database has it, for Skyrim's bolt-state probe.
    // Unity-free, so the tests cover it; mirrored by protocol/cpp/parts.h. MSC's side that reads it is Game/PartLink.cs.
    public struct PartReport
    {
        public string Name;          // the database entry: Door_Left, Radiator, WheelFL
        public uint Flags;           // Wire.Part*
        public uint Generation;      // anchor the position is for
        public Vec3 Position;        // Skyrim world units, with PartPlaced
        public int Bolts;            // Screw FSMs below the part
        public int BoltsTight;       // of them at stage 8
        public int Stages;           // all their stages added up
        public float Tightness;      // BoltCheck.Tightness
        public float TightnessFull;  // BoltCheck.BoltedYES

        public const int Size = 80;  // sizeof(PartState)
        const int NameAt = 40;

        public static uint FlagsFor(bool installed, bool bolted, bool damaged, bool placed)
        {
            return (installed ? Wire.PartInstalled : 0u) | (bolted ? Wire.PartBolted : 0u) | (damaged ? Wire.PartDamaged : 0u) |
                   (placed ? Wire.PartPlaced : 0u);
        }

        // Whether now has to go out after last went: anything but the position changed, a new anchor, or it moved
        // more than PartMoved while placed. last null: never sent (or sent again from scratch).
        public static bool ShouldSend(PartReport? last, PartReport now)
        {
            if (last == null) return true;
            var l = last.Value;
            if (l.Flags != now.Flags || l.Bolts != now.Bolts || l.BoltsTight != now.BoltsTight || l.Stages != now.Stages ||
                l.Generation != now.Generation || l.Name != now.Name)
                return true;
            if ((now.Flags & Wire.PartPlaced) == 0) return false;
            float dx = now.Position.x - l.Position.x, dy = now.Position.y - l.Position.y, dz = now.Position.z - l.Position.z;
            return dx * dx + dy * dy + dz * dz > Wire.PartMoved * Wire.PartMoved;
        }

        // Counts of a part's bolts from their stages (0..8 each).
        public static void CountBolts(int[] stages, out int bolts, out int tight, out int sum)
        {
            CountBolts(stages, stages != null ? stages.Length : 0, out bolts, out tight, out sum);
        }

        // The first count of stages (a buffer kept between reads).
        public static void CountBolts(int[] stages, int count, out int bolts, out int tight, out int sum)
        {
            bolts = stages != null ? Math.Min(count, stages.Length) : 0;
            tight = 0;
            sum = 0;
            for (int i = 0; i < bolts; i++)
            {
                int s = Math.Max(0, Math.Min(8, stages[i]));
                sum += s;
                if (s == 8) tight++;
            }
        }

        public byte[] Encode()
        {
            var payload = new byte[Size];
            BitConverter.GetBytes(Position.x).CopyTo(payload, 0);
            BitConverter.GetBytes(Position.y).CopyTo(payload, 4);
            BitConverter.GetBytes(Position.z).CopyTo(payload, 8);
            BitConverter.GetBytes(Flags).CopyTo(payload, 12);
            BitConverter.GetBytes(Generation).CopyTo(payload, 16);
            BitConverter.GetBytes(Clamp16(Bolts)).CopyTo(payload, 20);
            BitConverter.GetBytes(Clamp16(Math.Min(BoltsTight, Bolts))).CopyTo(payload, 22);
            BitConverter.GetBytes(Clamp16(Math.Min(Stages, 8 * Bolts))).CopyTo(payload, 24);
            BitConverter.GetBytes(Tightness).CopyTo(payload, 28);
            BitConverter.GetBytes(TightnessFull).CopyTo(payload, 32);
            var bytes = Encoding.UTF8.GetBytes(Name ?? "");
            int length = Math.Min(bytes.Length, (int)Wire.PartNameMax);
            while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80) length--;  // inside a character
            BitConverter.GetBytes((uint)length).CopyTo(payload, 36);
            Array.Copy(bytes, 0, payload, NameAt, length);
            return payload;
        }

        // The report in a payload, null when it's malformed (the same rules as parts.h).
        public static PartReport? Decode(byte[] payload, int size)
        {
            if (payload == null || size < Size || payload.Length < Size) return null;
            uint length = BitConverter.ToUInt32(payload, 36);
            int bolts = BitConverter.ToUInt16(payload, 20), tight = BitConverter.ToUInt16(payload, 22), stages = BitConverter.ToUInt16(payload, 24);
            if (length == 0 || length > Wire.PartNameMax || tight > bolts || stages > 8 * bolts) return null;
            return new PartReport
            {
                Position = new Vec3(BitConverter.ToSingle(payload, 0), BitConverter.ToSingle(payload, 4), BitConverter.ToSingle(payload, 8)),
                Flags = BitConverter.ToUInt32(payload, 12),
                Generation = BitConverter.ToUInt32(payload, 16),
                Bolts = bolts,
                BoltsTight = tight,
                Stages = stages,
                Tightness = BitConverter.ToSingle(payload, 28),
                TightnessFull = BitConverter.ToSingle(payload, 32),
                Name = Encoding.UTF8.GetString(payload, NameAt, (int)length),
            };
        }

        static ushort Clamp16(int v) { return (ushort)Math.Max(0, Math.Min(ushort.MaxValue, v)); }
    }
}
