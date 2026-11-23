using System.Text;

namespace PerkeleRim.Protocol
{
    // TeimoLine (Skyrim -> MSC, v51): which of MSC's own Teimo lines to play and where Teimo stands.
    public static unsafe class TeimoLines
    {
        // The clip (a MasterAudio variation in MSC's Teimo group) and his head's position in Skyrim
        // world space, or false for a malformed message.
        public static bool Decode(byte[] payload, int size, out string clip, out Vec3 position)
        {
            clip = null;
            position = new Vec3(0f, 0f, 0f);
            if (payload == null || size < sizeof(TeimoLine) || payload.Length < sizeof(TeimoLine)) return false;
            TeimoLine line;
            fixed (byte* p = payload) line = *(TeimoLine*)p;
            if (line.length == 0 || line.length > Wire.TeimoClipMax) return false;
            var bytes = new byte[line.length];
            for (int i = 0; i < bytes.Length; i++) bytes[i] = line.clip[i];
            clip = Encoding.UTF8.GetString(bytes);
            position = new Vec3(line.position[0], line.position[1], line.position[2]);
            return true;
        }

        // The message for clip at position (tests; Skyrim encodes its own).
        public static byte[] Encode(string clip, Vec3 position)
        {
            var bytes = Encoding.UTF8.GetBytes(clip);
            var line = new TeimoLine { length = (uint)System.Math.Min(bytes.Length, (int)Wire.TeimoClipMax) };
            line.position[0] = position.x;
            line.position[1] = position.y;
            line.position[2] = position.z;
            for (int i = 0; i < line.length; i++) line.clip[i] = bytes[i];
            var payload = new byte[sizeof(TeimoLine)];
            fixed (byte* p = payload) *(TeimoLine*)p = line;
            return payload;
        }
    }
}
