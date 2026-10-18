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
    // Skyrim's sloped, rough roads it always fell onto its side. Only near the lean it should have
    // and with wheels on the ground (Righting): a moped really falling is left to fall, or it slid
    // along the terrain trying to stand up.
    // Its pitch is damped on the ground. A bike lying on its side has all its wheels off the ground
    // too, but that is no jump: it gets no help in the air (Righting.AirHelps), or it stood itself up
    // and dragged the rider along the ground.
    public class AirAssist : MonoBehaviour
    {
        // Roll spring (1/s^2) and damping (1/s) on the ground.
        // The spring beats gravity on the moped's tall center of mass (g / ~0.5 m), or it can't hold
        // itself up.
        const float Upright = 40f;
        const float UprightDamping = 10f;
        // Pitch damping on the ground (1/s).
        const float PitchDamping = 4f;
        // Seconds in the air before it helps, so plain bumps are left alone.
        const float After = 0.15f;
        // How fast the wrong angle closes (per second), and how fast the spin follows that (per second).
        const float Turn = 4f;
        const float Follow = 8f;
        // Below this flat speed (m/s), or flying backwards, it keeps facing its own way.
        const float MinFlatSpeed = 2f;

        public bool Riding;  // set by VehicleLink every frame
        // Seconds the wheels have all been off the ground (0 on it), for VehicleTune's stall guard.
        public float Airborne => airborne;
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
                return;
            }
            airborne += Time.fixedDeltaTime;
            if (airborne < After) return;
            if (!Righting.AirHelps((body.rotation * Vector3.up).y, body.velocity.magnitude)) return;

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

        readonly Righting righting = new Righting();
        bool fallen;  // logged once per fall

        // Rolls it toward upright tilted into the turn: lean = atan(speed * yaw rate / g).
        void Balance()
        {
            var up = body.rotation * Vector3.up;
            var f = body.rotation * Vector3.forward;
            var forward = new Vector3(f.x, 0f, f.z);
            if (forward.sqrMagnitude < 1e-4f) return;
            forward.Normalize();
            var right = Vector3.Cross(Vector3.up, forward);
            float speed = Vector3.Dot(body.velocity, forward);
            float yawRate = Vector3.Dot(body.angularVelocity, Vector3.up);
            float lean = Righting.WantedLean(speed, yawRate);
            var wantedUp = Vector3.up * Mathf.Cos(lean) + right * Mathf.Sin(lean);

            float gain = righting.Update(Vector3.Angle(up, wantedUp), WheelsDown());
            if (righting.Fallen != fallen && Log != null) Log(righting.Fallen ? "air assist: tipped over, left to fall" : "air assist: upright again, balancing");
            fallen = righting.Fallen;
            if (gain <= 0f) return;

            var axis = body.rotation * Vector3.forward;
            float error = Vector3.Dot(Vector3.Cross(up, wantedUp), axis);  // sin of the roll still to go
            float rollRate = Vector3.Dot(body.angularVelocity, axis);
            body.AddTorque(axis * (Upright * error - UprightDamping * rollRate) * gain, ForceMode.Acceleration);

            var side = body.rotation * Vector3.right;
            body.AddTorque(-side * (PitchDamping * Vector3.Dot(body.angularVelocity, side)) * gain, ForceMode.Acceleration);
        }

        int WheelsDown()
        {
            var wheels = axles != null ? axles.allWheels : null;
            int n = 0;
            if (wheels != null)
                foreach (var w in wheels)
                    if (w != null && w.onGroundDown) n++;
            return n;
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
