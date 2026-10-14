using System;
using System.Collections.Generic;

namespace PerkeleRim
{
    // Thrown items and the piss stream as attacks on Skyrim's actors (ActorLink sends them as
    // ImpactEvent). An item that leaves the player's hand within ThrowWindow seconds of the right
    // mouse button (MSC's throw) is followed for up to FlightMax seconds; a bottle among them flies
    // BottleBoost times as fast as MSC throws it, to be a ranged attack worth having. The first
    // capsule it passes through while still at SpeedMin or faster is hit. Unity-free.
    public static class Throwing
    {
        public const float BottleBoost = 2f;
        public const float ThrowWindow = 0.5f;  // seconds from the right button to the item leaving the hand
        public const float BoostWindow = 0.3f;  // seconds after that for MSC's throw speed to show up
        public const float FlightMax = 4f;      // seconds a thrown item is followed
        public const float SpeedMin = 2f;       // meters per second (Damage::kThrownSpeedMin): slower hits nothing
        public const float ItemRadius = 0.1f;   // meters around a thrown item's center
        public const float Bounce = 0.3f;       // share of its speed an item keeps, turned back, after a hit
        public const float PissEvery = 0.5f;    // seconds between piss hits on one actor
        public const float DropRadius = 0.05f;  // meters around one piss drop

        // MSC's bottles: the empty one thrown after a beer, booze and spirit.
        public static bool IsBottle(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var n = name.ToLowerInvariant();
            return n.Contains("bottle") || n.StartsWith("booze") || n.StartsWith("spirit");
        }

        // The body something of radius radius passes through moving from from to to: the first along
        // the way, -1 for none. A point (from == to) works too.
        public static int Sweep(Vec3 from, Vec3 to, float radius, IList<ActorBody> bodies)
        {
            var way = new Vec3(to.x - from.x, to.y - from.y, to.z - from.z);
            float length = (float)Math.Sqrt(way.x * way.x + way.y * way.y + way.z * way.z);
            var direction = length > 1e-6f ? new Vec3(way.x / length, way.y / length, way.z / length) : new Vec3(0f, 0f, 1f);
            float distance;
            return Actors.Punch(from, direction, length, radius, bodies, out distance);
        }
    }

    // When the piss stream may hit an actor again: at most once every Throwing.PissEvery seconds each.
    public sealed class PissPacer
    {
        readonly Dictionary<uint, float> last = new Dictionary<uint, float>();

        public bool Ready(uint actor, float now)
        {
            float at;
            if (last.TryGetValue(actor, out at) && now - at < Throwing.PissEvery) return false;
            last[actor] = now;
            return true;
        }

        public void Clear() => last.Clear();
    }
}
