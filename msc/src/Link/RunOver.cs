using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // The vehicle MSC's player drives running Skyrim's actors over (ActorLink sends it as
    // ImpactEvent, kImpactVehicle). Capsules don't collide with vehicles, so the van carries on and
    // CrashMeter never sees the hit; instead the vehicle's box (its colliders' bounds in its own
    // space) is checked against each capsule every frame while it moves at SpeedMin or faster. One
    // actor is hit once per IgnoreSeconds: Skyrim knocks it over, which takes it off the list, but
    // the list lags a frame or two behind. An actor of HeavyMass or more (Damage::kHeavyMass: bears,
    // cows, giants, dragons) is solid to vehicles instead: the vehicle stops on it as on a wall
    // (and CrashMeter counts that), and the hit is sent at the speed from before the stop.
    // Unity-free.
    public static class RunOver
    {
        public const float SpeedMin = 2f;        // meters per second (Damage::kRunOverSpeedMin)
        public const float IgnoreSeconds = 2f;   // between two hits on one actor
        public const float Margin = 0.05f;       // meters around the box
        public const float HeavyMass = 250f;     // kilograms

        public static bool Solid(ActorBody body) => body.Mass >= HeavyMass;

        // Whether a capsule (its axis bottom to top and radius, in the vehicle's space) touches the
        // box min..max. The axis is sampled at most half a radius apart.
        public static bool Touches(Vec3 min, Vec3 max, Vec3 bottom, Vec3 top, float radius)
        {
            float reach = radius + Margin;
            var axis = new Vec3(top.x - bottom.x, top.y - bottom.y, top.z - bottom.z);
            float length = (float)Math.Sqrt(axis.x * axis.x + axis.y * axis.y + axis.z * axis.z);
            int steps = Math.Max(1, (int)Math.Ceiling(length / Math.Max(0.01f, radius * 0.5f)));
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                var p = new Vec3(bottom.x + axis.x * t, bottom.y + axis.y * t, bottom.z + axis.z * t);
                float dx = Outside(p.x, min.x, max.x), dy = Outside(p.y, min.y, max.y), dz = Outside(p.z, min.z, max.z);
                if (dx * dx + dy * dy + dz * dz <= reach * reach) return true;
            }
            return false;
        }

        static float Outside(float v, float min, float max) => v < min ? min - v : v > max ? v - max : 0f;
    }

    // When one actor may be hit again by a repeating source: at most once every `every` seconds.
    public sealed class HitPacer
    {
        readonly Dictionary<uint, float> last = new Dictionary<uint, float>();
        readonly float every;

        public HitPacer(float every) => this.every = every;

        public bool Ready(uint actor, float now)
        {
            float at;
            if (last.TryGetValue(actor, out at) && now - at < every) return false;
            last[actor] = now;
            return true;
        }

        public void Clear() => last.Clear();
    }
}
