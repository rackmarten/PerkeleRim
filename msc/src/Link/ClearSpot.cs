using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // Where a vehicle restored from a save goes when it came back inside a wall or a rock: the nearest
    // spot around it where the ground under the whole vehicle is flat and none of its body is in the
    // island's statics. Unity-free: the caller says whether an offset is clear, the tests cover the order.
    public static class ClearSpot
    {
        // How uneven the ground under the vehicle may be (meters), as when parking it next to the player.
        public const float MaxStep = 0.4f;
        // The distance (meters) between the rings of candidates, and how many there are.
        public const float RingStep = 3f;
        public const int Rings = 4;

        public static bool Flat(float low, float high) => high - low <= MaxStep;

        // Offsets (x right, z ahead of the vehicle, meters), nearest first: where it is, then rings of
        // eight around it, each starting ahead, then behind, then the sides, then the diagonals.
        public static List<Vec3> Offsets()
        {
            var list = new List<Vec3> { new Vec3(0f, 0f, 0f) };
            var turns = new[] { 0, 4, 2, 6, 1, 7, 3, 5 };  // eighths of a turn
            for (int r = 1; r <= Rings; r++)
                foreach (var t in turns)
                {
                    double a = t * Math.PI / 4.0;
                    list.Add(new Vec3((float)Math.Sin(a) * r * RingStep, 0f, (float)Math.Cos(a) * r * RingStep));
                }
            return list;
        }

        // The first offset the caller finds clear, or null when none is.
        public static Vec3? First(Func<Vec3, bool> clear)
        {
            foreach (var offset in Offsets())
                if (clear(offset)) return offset;
            return null;
        }
    }
}
