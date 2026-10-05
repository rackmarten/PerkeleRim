using System;
using System.Text;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // The LookAtText message: MSC's interaction prompt for what its crosshair is on (text and hand
    // icon), shown by Skyrim next to its own crosshair. Unity-free, so the tests cover it.
    public static class LookAt
    {
        const int HeaderSize = 8;  // sizeof(LookAtText)

        // Payload for a_text (null or empty = no text) and LookAt* flags. Text over LookAtTextMax
        // bytes of UTF-8 is cut at a character boundary.
        public static byte[] Encode(string text, uint flags = 0)
        {
            var bytes = Encoding.UTF8.GetBytes(text ?? "");
            int length = Math.Min(bytes.Length, (int)Wire.LookAtTextMax);
            while (length > 0 && length < bytes.Length && (bytes[length] & 0xC0) == 0x80) length--;  // inside a character

            var payload = new byte[HeaderSize + length];
            BitConverter.GetBytes((uint)length).CopyTo(payload, 0);
            BitConverter.GetBytes(flags).CopyTo(payload, 4);
            Array.Copy(bytes, 0, payload, HeaderSize, length);
            return payload;
        }

        // The text in a payload, or null when it's malformed.
        public static string Decode(byte[] payload, int size)
        {
            uint flags;
            return Decode(payload, size, out flags);
        }

        public static string Decode(byte[] payload, int size, out uint flags)
        {
            flags = 0;
            if (payload == null || size < HeaderSize || size > payload.Length) return null;
            uint length = BitConverter.ToUInt32(payload, 0);
            if (length > Wire.LookAtTextMax || HeaderSize + length > size) return null;
            flags = BitConverter.ToUInt32(payload, 4);
            return Encoding.UTF8.GetString(payload, HeaderSize, (int)length);
        }
    }
}
