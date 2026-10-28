using System;

namespace PerkeleRim
{
    // MSC's body for a piece of Skyrim's junk (JunkLink, docs/PROTOCOL.md "Throwing junk"): a copy of
    // MSC's empty beer bottle, so it is picked up, carried and thrown exactly like one, with its
    // collider sized to the item's bounds. Skyrim draws the item's own model on it, and once it has
    // come to rest out of the hand, Skyrim drops the real item there. Unity-free.
    public static class JunkBody
    {
        public const float SizeMin = 0.04f;      // meters along each axis, so a flat item still lands
        public const float SizeMax = 0.6f;       // and at most, so a big basket still fits the hand
        public const float RestSpeed = 0.15f;    // meters per second: slower is lying still
        public const float RestSeconds = 0.5f;   // that long, and it has landed
        public const float LooseMax = 15f;       // seconds out of the hand: landed wherever it is by then
        public const float AnswerTimeout = 2f;   // seconds for Skyrim's JunkOut after the take
        public const float FallMax = 20f;        // meters below the player's feet: it fell off the island, back to the inventory

        // A loose body this far below the player's feet (meters) is lost, not landed.
        public static bool Lost(float belowFeet) => belowFeet > FallMax;

        static float Side(float meters) => Math.Min(SizeMax, Math.Max(SizeMin, meters));

        // The collider box for Skyrim bounds (the model's own space, units) in Unity's local space
        // (meters): center and size, each axis within SizeMin..SizeMax.
        public static void Box(Vec3 boundsMin, Vec3 boundsMax, out Vec3 center, out Vec3 size)
        {
            var a = Coords.SkyrimToUnity(boundsMin);
            var b = Coords.SkyrimToUnity(boundsMax);
            center = new Vec3((a.x + b.x) * 0.5f, (a.y + b.y) * 0.5f, (a.z + b.z) * 0.5f);
            size = new Vec3(Side(Math.Abs(b.x - a.x)), Side(Math.Abs(b.y - a.y)), Side(Math.Abs(b.z - a.z)));
        }
    }

    // When a body out of the hand has landed: lying still for RestSeconds, or out for LooseMax.
    public sealed class JunkLanding
    {
        float looseSince = -1f;
        float stillSince = -1f;

        // Call every frame. held: in MSC's hand (or still on its way there); speed: meters per second.
        public bool Update(bool held, float speed, float now)
        {
            if (held)
            {
                looseSince = -1f;
                stillSince = -1f;
                return false;
            }
            if (looseSince < 0f) looseSince = now;
            if (now - looseSince >= JunkBody.LooseMax) return true;
            if (speed >= JunkBody.RestSpeed) { stillSince = -1f; return false; }
            if (stillSince < 0f) stillSince = now;
            return now - stillSince >= JunkBody.RestSeconds;
        }
    }
}
