using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Applies Springs.Push to every wheel of a vehicle on the island while it is ridden: the last
    // part of each wheel's travel pushes back harder and harder instead of meeting UnityCar's
    // bump stop (VehicleTune sets From and Strength per vehicle; parked, the wheels push nothing). Wheel.compression is meters into
    // its suspensionTravel.
    public class ProgressiveSprings : MonoBehaviour
    {
        public float From = 0.6f;
        public float Strength = 1.5f;
        public bool Riding;  // set by VehicleTune every frame; a parked vehicle's wheels don't push (Springs.Applies)
        public Action<string> Log;

        Rigidbody body;
        Axles axles;
        float[] lastCompression;
        float nextLog;
        int logs;

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            axles = GetComponent<Axles>();
        }

        void FixedUpdate()
        {
            if (body == null || body.isKinematic || Strength <= 0f || !Springs.Applies(Riding)) return;
            var wheels = axles != null ? axles.allWheels : null;
            if (wheels == null) return;
            if (lastCompression == null || lastCompression.Length != wheels.Length) lastCompression = new float[wheels.Length];
            float weight = body.mass * Physics.gravity.magnitude;
            var up = body.rotation * Vector3.up;
            float deepest = 0f;
            for (int i = 0; i < wheels.Length; i++)
            {
                var w = wheels[i];
                if (w == null) continue;
                float squeeze = (w.compression - lastCompression[i]) / Time.fixedDeltaTime;
                lastCompression[i] = w.compression;
                if (!w.onGroundDown || w.suspensionTravel <= 1e-4f) continue;
                float share = w.compression / w.suspensionTravel;
                deepest = Mathf.Max(deepest, share);
                float push = Springs.Push(share, squeeze, weight, From, Strength);
                if (push > 0f) body.AddForceAtPosition(up * push, w.transform.position);
            }
            if (deepest > 0.95f && Time.time >= nextLog && logs < 30 && Log != null)
            {
                nextLog = Time.time + 1f;
                logs++;
                Log($"springs: a wheel {deepest * 100f:0}% into its travel, cushioned ({name})");
            }
        }
    }
}
