using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Watches the vehicle the player drives for crashes MSC survives and hands them to DeathGuard as
    // damage for Skyrim (CrashMeter has the thresholds). Logs every impact above CrashMeter.LogFloor.
    public class CrashSensor
    {
        readonly CrashMeter meter = new CrashMeter();
        readonly Action<string> log;
        VehicleLink vehicle;
        int moves;

        public CrashSensor(Action<string> log) => this.log = log;

        // Every FixedUpdate, before the physics step: driven = the vehicle the player sits in while
        // MSC has the player, else null.
        public void Step(VehicleLink driven)
        {
            var body = driven != null ? driven.Body : null;
            if (driven != vehicle || (driven != null && driven.Moves != moves))
            {
                vehicle = driven;
                moves = driven != null ? driven.Moves : 0;
                meter.Reset();
            }
            if (body == null || body.isKinematic) { meter.Reset(); return; }

            var v = body.velocity;
            float impact = meter.Step(v.x, v.y, v.z, Time.fixedDeltaTime, Physics.gravity.y);
            if (impact > 0f)
            {
                var up = body.transform.up;
                impact = CrashMeter.Soften(impact, meter.AlongUp(up.x, up.y, up.z), up.y);
            }
            driven.Impacts(meter.InImpact, impact);
            if (impact < CrashMeter.LogFloor) return;
            float severity = meter.Hit(impact, Time.realtimeSinceStartup);
            var contact = driven.Contact(0.2f);
            log($"crash: the {driven.Name} hit {impact:0.0} m/s at {v.magnitude * 3.6f:0} km/h, severity {severity:0.00}" + (contact != null ? ", touching " + contact : ""));
            DeathGuard.Impact(impact, severity);
        }
    }
}
