using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // One Skyrim actor from ActorList as an upright capsule on the collision island: Unity axes,
    // meters, island space.
    public struct ActorBody
    {
        public uint Id;
        public Vec3 Feet;
        public Vec3 Velocity;  // meters per second
        public float Radius;
        public float Height;
        public float Mass;     // kilograms (ActorPose.mass)

        // The capsule's axis, between the centers of its end spheres (one point for a squat capsule).
        public Vec3 Bottom => new Vec3(Feet.x, Feet.y + Math.Min(Radius, Height * 0.5f), Feet.z);
        public Vec3 Top => new Vec3(Feet.x, Feet.y + Math.Max(Height - Radius, Height * 0.5f), Feet.z);
    }

    // Skyrim's actors near the player on the island, and what MSC's punch lands on. Unity-free.
    public static class Actors
    {
        // The list's actors on the island, closest first as Skyrim sent them. Empty when the list is
        // for another anchor than the island's.
        public static unsafe List<ActorBody> FromList(ref ActorList list, Island island)
        {
            var bodies = new List<ActorBody>();
            if (!island.HasAnchor || list.generation != island.Generation) return bodies;
            int count = (int)Math.Min(list.count, Wire.ActorCapacity);
            for (int i = 0; i < count; i++)
            {
                var a = list.Get(i);
                if (a.id == 0) continue;
                bodies.Add(new ActorBody
                {
                    Id = a.id,
                    Feet = island.SkyrimToIsland(new Vec3(a.position[0], a.position[1], a.position[2])),
                    Velocity = Coords.SkyrimToUnity(new Vec3(a.velocity[0], a.velocity[1], a.velocity[2])),
                    Radius = (float)(a.radius * Coords.MetersPerUnit),
                    Height = (float)(a.height * Coords.MetersPerUnit),
                    Mass = a.mass,
                });
            }
            return bodies;
        }

        // The body a punch from origin along direction (unit) lands on: the first whose capsule a fist
        // of radius fist touches within reach meters. -1 when it hits nothing; distance is how far
        // along the punch the fist gets closest to that body's axis.
        public static int Punch(Vec3 origin, Vec3 direction, float reach, float fist, IList<ActorBody> bodies, out float distance)
        {
            int best = -1;
            distance = float.MaxValue;
            var end = Add(origin, Scale(direction, reach));
            for (int i = 0; i < bodies.Count; i++)
            {
                float s, t;
                float d = SegmentDistance(origin, end, bodies[i].Bottom, bodies[i].Top, out s, out t);
                if (d > bodies[i].Radius + fist || s * reach >= distance) continue;
                best = i;
                distance = s * reach;
            }
            return best;
        }

        // Closest distance between segments p0-p1 and q0-q1; s and t are where on each (0..1).
        public static float SegmentDistance(Vec3 p0, Vec3 p1, Vec3 q0, Vec3 q1, out float s, out float t)
        {
            var d1 = Sub(p1, p0);
            var d2 = Sub(q1, q0);
            var r = Sub(p0, q0);
            float a = Dot(d1, d1), e = Dot(d2, d2), f = Dot(d2, r);
            const float eps = 1e-8f;
            if (a <= eps && e <= eps) { s = t = 0f; return Length(r); }
            if (a <= eps)
            {
                s = 0f;
                t = Clamp01(f / e);
            }
            else
            {
                float c = Dot(d1, r);
                if (e <= eps)
                {
                    t = 0f;
                    s = Clamp01(-c / a);
                }
                else
                {
                    float b = Dot(d1, d2);
                    float denom = a * e - b * b;
                    s = denom > eps ? Clamp01((b * f - c * e) / denom) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f) { t = 0f; s = Clamp01(-c / a); }
                    else if (t > 1f) { t = 1f; s = Clamp01((b - c) / a); }
                }
            }
            return Length(Sub(Add(p0, Scale(d1, s)), Add(q0, Scale(d2, t))));
        }

        static Vec3 Add(Vec3 a, Vec3 b) => new Vec3(a.x + b.x, a.y + b.y, a.z + b.z);
        static Vec3 Sub(Vec3 a, Vec3 b) => new Vec3(a.x - b.x, a.y - b.y, a.z - b.z);
        static Vec3 Scale(Vec3 a, float k) => new Vec3(a.x * k, a.y * k, a.z * k);
        static float Dot(Vec3 a, Vec3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        static float Length(Vec3 a) => (float)Math.Sqrt(Dot(a, a));
        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
    }
}
