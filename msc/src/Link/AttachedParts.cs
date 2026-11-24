using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // The parts attached to a vehicle built from parts (the Satsuma) as PartSet carries them to Skyrim:
    // each part once, by its id, with the model it is drawn from and its pose relative to the body.
    // Game/SatsumaParts finds them in MSC's car; Skyrim draws each from its own model, so a part
    // going on or coming off changes only that part. Unity-free, so the tests cover it.
    public static class AttachedParts
    {
        public struct Entry
        {
            public uint Part;    // stable while MSC has the part object, never 0
            public uint Model;   // ModelChunk.model, 0 = not read yet
            public bool Visible;
            public Quat Rotation;  // relative to the body, Skyrim space
            public Vec3 Translation;
        }

        // Fills s with entries in their order, each part id once: an id 0 or one already in is left
        // out, and so is everything past PartSetCapacity. Returns how many were left out.
        public static unsafe int Encode(IList<Entry> entries, uint vehicle, uint generation, uint frame, ref PartSet s)
        {
            s = new PartSet { vehicle = vehicle, generation = generation, frame = frame };
            var seen = new HashSet<uint>();
            int count = 0, left = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                if (e.Part == 0 || count == Wire.PartSetCapacity || !seen.Add(e.Part))
                {
                    left++;
                    continue;
                }
                var part = new AttachedPart { part = e.Part, model = e.Model, flags = e.Visible ? Wire.PartVisible : 0u };
                part.rotation[0] = e.Rotation.x;
                part.rotation[1] = e.Rotation.y;
                part.rotation[2] = e.Rotation.z;
                part.rotation[3] = e.Rotation.w;
                part.translation[0] = e.Translation.x;
                part.translation[1] = e.Translation.y;
                part.translation[2] = e.Translation.z;
                s.Set(count++, part);
            }
            s.count = (uint)count;
            return left;
        }

        // Whether two lists name the same parts with the same models in the same order: the set as
        // Skyrim builds it is the same (only poses and visibility moved), for logging changes once.
        public static bool SameSet(IList<Entry> a, IList<Entry> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
                if (a[i].Part != b[i].Part || a[i].Model != b[i].Model) return false;
            return true;
        }
    }
}
