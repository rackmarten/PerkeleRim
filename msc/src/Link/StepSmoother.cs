using System;

namespace PerkeleRim
{
    // Skyrim's stairs under MSC's walk: the character controller climbs each step in one physics
    // step (and PlayerLink snaps it down each one going down), and Skyrim's camera, put at MSC's
    // feet plus its eye, jumped with every step. This keeps an offset for the eye that takes those
    // jumps out and lets them in over about Tau, so the view glides up and down stairs instead.
    // A jump counts as a step when it is steeper than 45 degrees (walking up a slope isn't), at
    // least MinStep and at most MaxStep (taller is a real climb or fall).
    // Unity-free; feet in Unity meters, y up.
    public class StepSmoother
    {
        public const float MinStep = 0.02f;
        public const float MaxStep = 0.7f;
        public const float Tau = 0.12f;  // seconds

        bool have;
        Vec3 last;

        // Meters to add to the eye's height.
        public float Offset { get; private set; }

        public float Update(Vec3 feet, bool onGround, float dt)
        {
            if (have && onGround)
            {
                float dy = feet.y - last.y;
                float dx = feet.x - last.x, dz = feet.z - last.z;
                float rise = Math.Abs(dy);
                if (rise >= MinStep && rise <= MaxStep && rise > (float)Math.Sqrt(dx * dx + dz * dz)) Offset -= dy;
            }
            Offset *= (float)Math.Exp(-Math.Max(dt, 0f) / Tau);
            Offset = Math.Max(-MaxStep, Math.Min(MaxStep, Offset));
            last = feet;
            have = true;
            return Offset;
        }

        public void Reset()
        {
            have = false;
            Offset = 0f;
        }
    }
}
