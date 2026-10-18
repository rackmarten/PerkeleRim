using System;

namespace PerkeleRim
{
    // Progressive springs for every vehicle: the base spring (UnityCar's linear one, tuned soft by
    // VehicleTune) rides the first part of a wheel's travel, and past From (a share of the travel)
    // the wheel pushes back harder and harder, with the square, up to Strength times the vehicle's
    // weight at full travel, plus a damper on the squeeze. UnityCar's own bump stop is nearly a
    // wall: it threw the moped over the handlebar and made the tractor hop. Unity-free, so the
    // tests cover it.
    public static class Springs
    {
        // Compression past full travel that still counts (the push stops growing there).
        public const float MaxOver = 1.5f;
        // The damper part per m/s of squeeze, in weights per unit of Strength.
        public const float DampingPerStrength = 0.2f;
        // The most a wheel pushes, in vehicle weights: a player landing on the seat squeezes a wheel
        // fast, and an uncapped damper flung the moped.
        public const float MaxWeights = 3f;
        // The moped's From: above how far its soft front wheels sag under its own weight at rest
        // (about 0.68 of the travel), or it was always in the progressive part, even parked.
        public const float MopedFrom = 0.75f;

        // Whether the springs push at all: only while the vehicle is ridden. A parked moped brushed by
        // the player or landed on tipped over or flew off when its wheels pushed back.
        public static bool Applies(bool riding) => riding;

        // The share of its travel a wheel sags under loadN (N) with a spring of rate (N/m) and travel (m).
        public static float RestShare(float loadN, float rate, float travel) => rate > 0f && travel > 0f ? loadN / rate / travel : 0f;

        // Extra upward force (N) on the body at one wheel: share = compression / travel (0..1),
        // squeeze = m/s the wheel is compressing, weight = the vehicle's weight (N).
        public static float Push(float share, float squeeze, float weight, float from, float strength)
        {
            if (!(share > from) || strength <= 0f || from >= 1f) return 0f;
            float into = Math.Min((share - from) / (1f - from), MaxOver);
            return Math.Min(weight * strength * (into * into + DampingPerStrength * Math.Max(squeeze, 0f)), weight * MaxWeights);
        }
    }
}
