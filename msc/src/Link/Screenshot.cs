using System;
using System.Text;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // The ScreenshotEvent payload (F11 in Skyrim): the PNG file MSC saves its view to, named by Skyrim
    // so the pair matches (protocol/cpp/screenshot.h). Unity-free, so the tests cover it.
    public static class Screenshot
    {
        const int HeaderSize = 8;  // sizeof(ScreenshotEvent)

        public static byte[] Encode(string path)
        {
            var bytes = Encoding.UTF8.GetBytes(path ?? "");
            if (bytes.Length == 0 || bytes.Length > Wire.ScreenshotPathMax) return null;
            var payload = new byte[HeaderSize + bytes.Length];
            BitConverter.GetBytes((uint)bytes.Length).CopyTo(payload, 0);
            Array.Copy(bytes, 0, payload, HeaderSize, bytes.Length);
            return payload;
        }

        // The file path in a payload, false when it's malformed.
        public static bool Decode(byte[] payload, int size, out string path)
        {
            path = null;
            if (payload == null || size < HeaderSize || size > payload.Length) return false;
            uint length = BitConverter.ToUInt32(payload, 0);
            if (length == 0 || length > Wire.ScreenshotPathMax || HeaderSize + length > size) return false;
            path = Encoding.UTF8.GetString(payload, HeaderSize, (int)length);
            return true;
        }
    }
}
