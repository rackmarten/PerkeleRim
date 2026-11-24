using System;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // GarageBay (Skyrim -> MSC) and GarageDone (MSC -> Skyrim), the Satsuma bay's two messages
    // (docs/PROTOCOL.md, "The Satsuma garage"). Unity-free, so the tests cover them.
    public static class GarageMessages
    {
        public struct Request
        {
            public uint Generation;
            public uint What;      // Wire.Bay*
            public Vec3 Position;  // the bay marker, Skyrim world space
            public float Heading;  // radians clockwise from north, the way the bay opens
        }

        public const int BaySize = 32, DoneSize = 32;

        public static bool DecodeBay(byte[] payload, int size, out Request request)
        {
            request = new Request();
            if (payload == null || size < BaySize || size > payload.Length) return false;
            request.Generation = BitConverter.ToUInt32(payload, 0);
            request.What = BitConverter.ToUInt32(payload, 4);
            request.Position = new Vec3(BitConverter.ToSingle(payload, 8), BitConverter.ToSingle(payload, 12), BitConverter.ToSingle(payload, 16));
            request.Heading = BitConverter.ToSingle(payload, 20);
            return true;
        }

        public static byte[] EncodeBay(Request request)
        {
            var bytes = new byte[BaySize];
            Put(bytes, 0, request.Generation);
            Put(bytes, 4, request.What);
            Put(bytes, 8, request.Position.x);
            Put(bytes, 12, request.Position.y);
            Put(bytes, 16, request.Position.z);
            Put(bytes, 20, request.Heading);
            return bytes;
        }

        public static GarageDone Done(Request request, bool done, bool body, int slotted, int floor, int vehicles, int missed) => new GarageDone
        {
            generation = request.Generation,
            request = done ? request.What : 0u,
            body = body ? 1u : 0u,
            slotted = (uint)Math.Max(0, slotted),
            floor = (uint)Math.Max(0, floor),
            vehicles = (uint)Math.Max(0, vehicles),
            missed = (uint)Math.Max(0, missed),
            asked = request.What,
        };

        static void Put(byte[] bytes, int at, uint v) => Array.Copy(BitConverter.GetBytes(v), 0, bytes, at, 4);
        static void Put(byte[] bytes, int at, float v) => Array.Copy(BitConverter.GetBytes(v), 0, bytes, at, 4);
    }
}
