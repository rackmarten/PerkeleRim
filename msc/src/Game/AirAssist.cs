using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Lands a ridden vehicle's jumps (the moped): once all its wheels have been off the ground for a
    // moment, its spin is eased toward level (no roll, the nose not diving) and facing the way it
    // flies, so it comes down on its wheels. Without it a hole or a bump on Skyrim's rough roads
    // threw the moped up, it rolled in the air, landed on its side and nothing could catch it.
    public class AirAssist : MonoBehaviour
    {
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
            if (!Riding || body == null || body.isKinematic || !InAir())
            {
                if (airborne >= After && Log != null) Log($"air assist: landed after {airborne:0.0} s in the air");
                airborne = 0f;
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
