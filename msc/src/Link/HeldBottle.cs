using System;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // The bottle of a drink shown for Skyrim (HeldDrink, protocol/cpp/held_drink.h): which of the
    // Drink object's children it and the hand holding it are, where the fist closes on it, and the
    // message from its pose. Unity-free, so the tests cover it; the caller passes points already in
    // the camera's space.
    public static class HeldBottle
    {
        // The bottle MSC's drinking of drink has in the hand, below Drink (FSM_MAP "Drinking"): the
        // beer, booze and spirit bottles hang on Hand itself, the milk carton and juice can on their
        // own hand. Their meshes stand along +Z, top up.
        public static string Path(uint drink)
        {
            switch (drink)
            {
                case Wire.DrinkBeer: return "Hand/BeerBottle";
                case Wire.DrinkBooze: return "Hand/BoozeBottle";
                case Wire.DrinkSpirit: return "Hand/SpiritBottle";
                case Wire.DrinkMilk: return "Hand/HandMilk/Milk";
                case Wire.DrinkJuice: return "Hand/HandJuice/JuiceCan";
                default: return null;
            }
        }

        // The hand holding it (its armature's finger bones are Fingers), below Drink.
        public static string Hand(uint drink)
        {
            switch (drink)
            {
                case Wire.DrinkBeer:
                case Wire.DrinkBooze:
                case Wire.DrinkSpirit: return "Hand/HandBottles";
                case Wire.DrinkMilk: return "Hand/HandMilk";
                case Wire.DrinkJuice: return "Hand/HandJuice";
                default: return null;
            }
        }

        // The middle joint of each finger in MSC's hand armature: the fist closes around them.
        public static readonly string[] Fingers = { "Bone_009", "Bone_010", "Bone_011", "Bone_012" };

        // Where the fist (the middle of its fingers) holds the bottle: the nearest point on the
        // bottle's axis from bottom to top, not past its ends.
        public static Vec3 Grip(Vec3 bottom, Vec3 top, Vec3 fist)
        {
            float ax = top.x - bottom.x, ay = top.y - bottom.y, az = top.z - bottom.z;
            float length2 = ax * ax + ay * ay + az * az;
            if (length2 < 1e-12f) return bottom;
            float t = ((fist.x - bottom.x) * ax + (fist.y - bottom.y) * ay + (fist.z - bottom.z) * az) / length2;
            t = Math.Max(0f, Math.Min(1f, t));
            return new Vec3(bottom.x + ax * t, bottom.y + ay * t, bottom.z + az * t);
        }

        // bottom, top: the middle of the mesh's base and top; side: its +X; grip: Grip. All in the
        // Unity camera's own space (+X right, +Y up, +Z forward, meters), as
        // Transform.InverseTransformPoint gives them.
        public static unsafe HeldDrink Message(uint form, Vec3 bottom, Vec3 top, Vec3 side, Vec3 grip)
        {
            var m = new HeldDrink { form = form };
            // The camera's space swaps like the world's: Skyrim's camera has +Y forward, +Z up.
            var b = Coords.UnityToSkyrim(bottom);
            var t = Coords.UnityToSkyrim(top);
            var s = Coords.UnityToSkyrim(side);
            var g = Coords.UnityToSkyrim(grip);
            m.bottom[0] = b.x; m.bottom[1] = b.y; m.bottom[2] = b.z;
            m.top[0] = t.x; m.top[1] = t.y; m.top[2] = t.z;
            m.side[0] = s.x; m.side[1] = s.y; m.side[2] = s.z;
            m.grip[0] = g.x; m.grip[1] = g.y; m.grip[2] = g.z;
            return m;
        }

        // The bottle is down or thrown: Skyrim drops its model.
        public static HeldDrink None() => new HeldDrink();
    }
}
