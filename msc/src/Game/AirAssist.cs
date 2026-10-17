using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Lands a ridden vehicle's jumps (the moped): once all its wheels have been off the ground for a
    // moment, its spin is eased toward level (no roll, the nose not diving) and facing the way it
    // flies, so it comes down on its wheels. Without it a hole or a bump on Skyrim's rough roads
    // threw the moped up, it rolled in the air, landed on its side and nothing could catch it.
    // On the ground it balances like a rider: the roll is eased toward the lean the turn asks for
    // (none going straight), since MSC only balances it by moving its center of mass and on
    // Skyrim's sloped, rough roads it always fell onto its side. Past Give up it is left to fall.
    // Its pitch is damped on the ground, and the last part of each wheel's travel pushes back
    // harder and harder (Cushion): UnityCar's bump stop kicked the bike up at every bump, over the
    // handlebar or into the air and onto its side.
    public class AirAssist : MonoBehaviour
    {
        // Roll spring (1/s^2) and damping (1/s) on the ground, the most it leans into a turn (rad),
        // and the tilt from upright (degrees) past which it no longer helps. The spring beats gravity
        // on the moped's tall center of mass (g / ~0.5 m), or it can't hold itself up.
        const float Upright = 40f;
        const float UprightDamping = 10f;
        const float MaxLean = 0.6f;
        const float GiveUp = 70f;
        // Pitch damping on the ground (1/s).
        const float PitchDamping = 4f;
        // Past this share of its travel a wheel pushes up on the body with up to Cushion times the
        // vehicle's weight at full travel, growing with the square, and damps the squeeze.
        const float CushionFrom = 0.6f;
        const float Cushion = 1.5f;
        const float CushionDamping = 0.3f;  // of the weight per m/s squeezing

        // Seconds in the air before it helps, so plain bumps are left alone.
        const float After = 0.15f;
        // How fast the wrong angle closes (per second), and how fast the spin follows that (per second).
        const float Turn = 4f;
        const float Follow = 8f;
        // Below this flat speed (m/s), or flying backwards, it keeps facing its own way.
        const float MinFlatSpeed = 2f;

        public bool Riding;  // set by VehicleLink every frame
        public Action<string> Log;

        Rigidbody body;
        Axles axles;
        float airborne;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            axles = GetComponent<Axles>();
        }

        void FixedUpdate()
        {
            if (!Riding || body == null || body.isKinematic) { airborne = 0f; return; }
            if (!InAir())
            {
                if (airborne >= After && Log != null) Log($"air assist: landed after {airborne:0.0} s in the air");
                airborne = 0f;
                Balance();
                CushionWheels();
                return;
            }
            airborne += Time.fixedDeltaTime;
            if (airborne < After) return;

            var v = body.velocity;
            var flat = new Vector3(v.x, 0f, v.z);
            var f = body.rotation * Vector3.forward;
            var own = new Vector3(f.x, 0f, f.z);
            var heading = flat.sqrMagnitude > MinFlatSpeed * MinFlatSpeed && Vector3.Dot(flat, own) > 0f ? flat : own;
            if (heading.sqrMagnitude < 1e-4f) return;  // nose straight up or down: no way to tell

            var delta = Quaternion.LookRotation(heading.normalized, Vector3.up) * Quaternion.Inverse(body.rotation);
            float angle;
            Vector3 axis;
            delta.ToAngleAxis(out angle, out axis);
            if (angle > 180f) angle -= 360f;
            if (float.IsNaN(axis.x) || float.IsInfinity(axis.x)) return;
            var wanted = axis * (angle * Mathf.Deg2Rad * Turn);
            body.angularVelocity = Vector3.Lerp(body.angularVelocity, wanted, 1f - Mathf.Exp(-Follow * Time.fixedDeltaTime));
        }

        bool fallen;  // past GiveUp, logged once per fall

        // Rolls it toward upright tilted into the turn: lean = atan(speed * yaw rate / g).
        void Balance()
        {
            var up = body.rotation * Vector3.up;
            bool down = Vector3.Angle(up, Vector3.up) > GiveUp;
            if (down != fallen && Log != null) Log(down ? "air assist: tipped over, left to fall" : "air assist: upright again, balancing");
            fallen = down;
            if (down) return;

            var f = body.rotation * Vector3.forward;
            var forward = new Vector3(f.x, 0f, f.z);
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            float speed = Vector3.Dot(body.velocity, forward);
            float yawRate = Vector3.Dot(body.angularVelocity, Vector3.up);
            float lean = Mathf.Clamp(Mathf.Atan(speed * yawRate / 9.81f), -MaxLean, MaxLean);
            var wantedUp = Vector3.up * Mathf.Cos(lean) + right * Mathf.Sin(lean);

            var axis = body.rotation * Vector3.forward;
            float error = Vector3.Dot(Vector3.Cross(up, wantedUp), axis);  // sin of the roll still to go
            float rollRate = Vector3.Dot(body.angularVelocity, axis);
            body.AddTorque(axis * (Upright * error - UprightDamping * rollRate), ForceMode.Acceleration);

            var side = body.rotation * Vector3.right;
            body.AddTorque(-side * (PitchDamping * Vector3.Dot(body.angularVelocity, side)), ForceMode.Acceleration);
        }

        float[] lastCompression;
        float nextCushionLog;
        int cushionLogs;
        bool sampled;  // logged one wheel's compression, to check it is in meters

        // Wheel.compression is meters into its suspensionTravel; past that UnityCar's bump stop kicks.
        void CushionWheels()
        {
            var wheels = axles != null ? axles.allWheels : null;
            if (wheels == null) return;
            if (lastCompression == null || lastCompression.Length != wheels.Length) lastCompression = new float[wheels.Length];
            float weight = body.mass * Physics.gravity.magnitude;
            var up = body.rotation * Vector3.up;
            float deepest = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null || !w.onGroundDown || w.suspensionTravel <= 1e-4f) { if (w != null) lastCompression[i] = w.compression; continue; }
                if (!sampled && Log != null) { sampled = true; Log($"air assist: wheel {w.name} compression {w.compression:0.###} of {w.suspensionTravel:0.###} m travel"); }
                float share = w.compression / w.suspensionTravel;
                float squeeze = (w.compression - lastCompression[i]) / Time.fixedDeltaTime;
                lastCompression[i] = w.compression;
                deepest = Mathf.Max(deepest, share);
                if (share <= CushionFrom) continue;
                float into = Mathf.Min((share - CushionFrom) / (1f - CushionFrom), 1.5f);
                float push = weight * (Cushion * into * into + CushionDamping * Mathf.Max(squeeze, 0f));
                body.AddForceAtPosition(up * push, w.transform.position);
            }
            if (deepest > 0.95f && Time.time >= nextCushionLog && cushionLogs < 30 && Log != null)
            {
                nextCushionLog = Time.time + 1f;
                cushionLogs++;
                Log($"air assist: a wheel {deepest * 100f:0}% into its travel, cushioned");
            }
        }

        bool InAir()
        {
            var wheels = axles != null ? axles.allWheels : null;
            if (wheels == null || wheels.Length == 0) return false;
            foreach (var w in wheels)
                if (w != null && w.onGroundDown) return false;
            return true;
        }
    }
}
