using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Makes an MSC vehicle hold Skyrim's roads (MSC's cars are UnityCar: CarDynamics on the root,
    // Axles copied onto raycast Wheels): heavier, a lower center of mass, a softer and longer
    // suspension. None of this is in MSC's save, so nothing is put back. The center of mass goes
    // down from wherever MSC puts it: MSC moves some on purpose (the moped's FSMs balance it that
    // way), and a fixed center put back over theirs left the ridden moped top-heavy.
    public class VehicleTune
    {
        public struct Settings
        {
            public float Mass;        // times MSC's mass
            public float CenterDrop;  // meters the center of mass goes down
            public float Spring;      // spring rate times this (and times Mass, so it sags as before)
            public float Travel;      // suspension travel times this
            public float BottomLift;  // meters the body collider's bottom goes up
            public float Power;       // engine torque times this (Drivetrain.powerMultiplier), 0 = as MSC has it
            public float Grip;        // tire forward grip times this, 0 = as MSC has it
            public float SideGrip;    // tire sideways grip times this, 0 = as MSC has it
            public float RearBrake;   // rear axle brake torque times this, 0 = as MSC has it
            public float Bump;        // compression damping times this on top, 0 = 1
            public float Rebound;     // rebound damping times this on top, 0 = 1
            public float FrontSpring; // front axle spring rate times this on top (its damping by the root), 0 = 1
            public float CushionFrom; // share of a wheel's travel from which it pushes back progressively (Springs), 0 = 0.6
            public float Cushion;     // that push at full travel in vehicle weights per wheel, 0 = none
            public bool Air;          // lands its jumps while ridden (AirAssist)
            public bool SoftBumps;    // small bumps make no crash sound and never stall the engine (Bumps)
            public float LeaveTipUpY; // the rider may get off while its up axis y is under this, or at a walking pace (Leaving), 0 = MSC's rule
        }

        // A vehicle sunk into the island is pushed out by the physics at whatever speed it takes: the
        // van's nose dug into a downhill at 50 km/h and left it at 3000 km/h. The body may leave what
        // it overlaps at this speed (m/s) at most.
        const float MaxDepenetration = 4f;

        // The van's wheels sit far behind its bumpers, so the body's overhang reached a lip or a ramp
        // before the wheels and stopped the van dead. With its bottom lifted the overhang clips into
        // the ground instead and the wheels climb. On Skyrim's steep roads the rear wheels spun uphill
        // and locked braking downhill, so the back slid out: more grip both ways, a weaker rear brake.
        // Steep hills west of Whiterun still spun the rear wheels at 1.3x: 1.8x forward grip now, the
        // sideways grip stays so it is no twitchier on roads. The body's overhang and the thin
        // TrafficTrigger collider at its nose both reached the ground before the wheels did, so both
        // have their bottom lifted.
        public static readonly Settings Van = new Settings { Mass = 1.2f, CenterDrop = 0.2f, Spring = 0.85f, Travel = 1.2f, BottomLift = 0.25f, Grip = 1.8f, SideGrip = 1.3f, RearBrake = 0.75f, Cushion = 0.5f, LeaveTipUpY = Upright.MaxTiltCos };
        // The moped keeps MSC's mass and follows MSC's center of mass moves: MSC balances it by moving the center (-0.1 m while
        // ridden, 0.4 m at speed, 0.2 m tipped over or left alone, a bit to the side on its
        // kickstand), and the extra mass stalled its little engine pulling away. Its 8 cm of travel
        // bottomed out in Skyrim's road holes and UnityCar's bump stop threw it up and onto its side,
        // and the fork sprang back from a bump and threw the rider over the handlebar: softer and
        // longer, giving way to a bump and slow to spring back. Twice the travel was too much, so
        // 1.6 times; the fork is softer still than the rear. Its center of mass still goes 15 cm
        // below wherever MSC puts it (FollowCenter), or every bump pitched it over the front wheel.
        public static readonly Settings Moped = new Settings { Mass = 1f, CenterDrop = 0.15f, Spring = 0.6f, Travel = 2f, Bump = 0.8f, Rebound = 1.5f, FrontSpring = 0.75f, Grip = 1.2f, SideGrip = 1.3f, CushionFrom = Springs.MopedFrom, Cushion = 1.5f, Air = true, SoftBumps = true, LeaveTipUpY = Leaving.TippedUpY };
        // Skyrim is all hills: the tractor gets twice the engine torque (it barely got rolling uphill
        // even in low first) and more grip to put it down. MSC's tractor has 5 cm of travel and a
        // 75000 N/m spring, and each rear wheel carries about 9 kN: it sat 12 cm deep, on its bump
        // stop, so it hopped on the road like a solid block (the van's 1.2x travel changed nothing).
        // Now 30 cm of travel and a spring 0.96x MSC's (72000 N/m, a ride of about 1.5 Hz, sagging
        // about half the travel) with the damping to match: the bump stop's 1000 Ns/m was a
        // twentieth of what such a spring needs. Too much rebound damping (6.5) made it slow to
        // settle after a turn, now 4.5. Past half its travel the progressive springs take over.
        public static readonly Settings Kekmet = new Settings { Mass = 1.2f, CenterDrop = 0.1f, Spring = 0.8f, Travel = 6f, Bump = 3.5f, Rebound = 4.5f, CushionFrom = 0.5f, Cushion = 0.7f, Power = 2f, Grip = 1.3f, LeaveTipUpY = Upright.MaxTiltCos };
        // The Gifu as MSC has it at first, with more forward grip for Skyrim's hills like the others
        // and its center of mass a bit lower (a tall cab-over with its tank up high); it gets off at
        // the van's tilt.
        public static readonly Settings Gifu = new Settings { Mass = 1f, CenterDrop = 0.2f, Spring = 1f, Travel = 1f, Grip = 1.3f, LeaveTipUpY = Upright.MaxTiltCos };
        static readonly string[] BodyColliders = { "CarCollider", "TrafficTrigger" };

        // MSC sets things up in its own Start; tune after it.
        const float TuneDelay = 1f;
        const float WatchEvery = 1f;
        readonly Settings settings;
        readonly string name;
        readonly Action<string> log;
        Transform car;
        Rigidbody body;
        ContactProbe probe;
        AirAssist air;
        ProgressiveSprings springs;
        readonly StallGuard stallGuard = new StallGuard();
        HutongGames.PlayMaker.FsmFloat[] stallLimits;  // the Starter FSM's RPM limits, 50
        float[] stallLimitValues;
        bool guarding;
        int guardLogs;
        PlayMakerFSM starter, driveTrigger;
        float tuneAt, nextWatch;
        bool tuned;
        float mass0, tunedMass;
        float power0, tunedPower;  // Drivetrain.powerMultiplier as MSC had it and as tuned, 0 = not tuned
        Vector3 tunedCenter;
        int resets, moves;

        public VehicleTune(Settings settings, string name, Action<string> log)
        {
            this.settings = settings;
            this.name = name;
            this.log = log;
        }

        public void Attach(Transform car, Rigidbody body)
        {
            this.car = car;
            this.body = body;
            tuned = false;
            if (body != null) body.maxDepenetrationVelocity = MaxDepenetration;
            probe = car.GetComponent<ContactProbe>();
            if (probe == null) probe = car.gameObject.AddComponent<ContactProbe>();
            springs = car.GetComponent<ProgressiveSprings>();
            if (springs == null) springs = car.gameObject.AddComponent<ProgressiveSprings>();
            springs.From = settings.CushionFrom > 0f ? settings.CushionFrom : 0.6f;
            springs.Strength = settings.Cushion;
            springs.Log = log;
            air = null;
            if (settings.Air)
            {
                air = car.GetComponent<AirAssist>();
                if (air == null) air = car.gameObject.AddComponent<AirAssist>();
                air.Log = log;
            }
            tuneAt = Time.realtimeSinceStartup + TuneDelay;
        }

        // What the vehicle last touched, if within the last seconds (for the crash log), else null.
        public string Contact(float within) => probe != null ? probe.Recent(within) : null;

        // Every frame the vehicle is on the island; riding: the player sits in it.
        public void Update(bool riding)
        {
            if (air != null) air.Riding = riding;
            if (springs != null) springs.Riding = riding;
            if (settings.SoftBumps) KeepEngine(riding);
            if (settings.LeaveTipUpY > 0f) FreeLeave(riding);
            if (car == null || body == null || Time.realtimeSinceStartup < tuneAt) return;
            if (!tuned) Tune();
            FollowCenter();
            if (Time.realtimeSinceStartup >= nextWatch)
            {
                nextWatch = Time.realtimeSinceStartup + WatchEvery;
                Watch();
            }
        }

        // The driven vehicle's impacts each physics step (CrashSensor): a bump shields the engine from
        // stalling for a moment; a crash stalls it (the guard may have held it up through it).
        public void Impacts(bool inImpact, float impact)
        {
            if (!settings.SoftBumps) return;
            if (!stallGuard.Step(inImpact, impact, Time.realtimeSinceStartup)) return;
            Guard(false);
            if (Starter() != null)
            {
                starter.SendEvent("OFF");
                log($"vehicle tune: {name} crash of {impact:0.0} m/s stalls the engine");
            }
        }

        // Every frame: the engine can't stall while a bump, a hop or a slide is going on.
        void KeepEngine(bool riding)
        {
            bool hopping = air != null && air.Airborne > 0f;
            bool sliding = body != null && !body.isKinematic && Bumps.Sliding(Vector3.Dot(body.velocity, car.right));
            Guard(riding && (hopping || sliding || stallGuard.Protects(Time.realtimeSinceStartup)));
            if (!riding) stallGuard.Reset();
        }

        // MSC stalls the moped in its Starter FSM: the Motor running state sends OFF when the
        // engine's RPM (read from the speedometer) falls under 50. The guard moves that limit below
        // zero and puts it back; the physics and the Drivetrain are left alone (setting
        // Drivetrain.canStall instead made the engine's RPM infinite and crashed MSC).
        const float NeverStalls = -1f;

        void Guard(bool on)
        {
            if (on == guarding) return;
            if (Starter() == null) return;
            if (stallLimits == null)
            {
                var state = Array.Find(starter.FsmStates, st => st.Name == "Motor running");
                var found = new System.Collections.Generic.List<HutongGames.PlayMaker.FsmFloat>();
                if (state != null)
                    foreach (var action in state.Actions)
                    {
                        if (action.GetType().Name != "FloatCompare") continue;
                        var field = action.GetType().GetField("float2");
                        var limit = field != null ? field.GetValue(action) as HutongGames.PlayMaker.FsmFloat : null;
                        if (limit != null && !limit.UseVariable) found.Add(limit);
                    }
                stallLimits = found.ToArray();
                stallLimitValues = new float[stallLimits.Length];
                for (int i = 0; i < stallLimits.Length; i++) stallLimitValues[i] = stallLimits[i].Value;
                log($"vehicle tune: {name} engine stall limits found: {stallLimits.Length}");
            }
            guarding = on;
            if (guardLogs++ < 20) log($"vehicle tune: {name} engine {(on ? "shielded from stalling" : "may stall again")}");
            for (int i = 0; i < stallLimits.Length; i++) stallLimits[i].Value = on ? NeverStalls : stallLimitValues[i];
        }

        PlayMakerFSM Starter()
        {
            if (starter == null && car != null) starter = FindFsm("Starter", "Starter");
            return starter;
        }

        // A rider on a tipped or slow moped may step off: MSC's DriveTrigger lets the player out when
        // the vehicle's speed on every axis is under its MinimumRequiredVelocity.
        void FreeLeave(bool riding)
        {
            if (car == null || !riding) return;
            if (driveTrigger == null) driveTrigger = FindFsm("LOD/PlayerTrigger/DriveTrigger", "PlayerTrigger");
            if (driveTrigger == null) return;
            var limit = driveTrigger.FsmVariables.FindFsmFloat("MinimumRequiredVelocity");
            if (limit == null) return;
            float wanted = Leaving.Limit(car.up.y, settings.LeaveTipUpY);
            if (Mathf.Abs(limit.Value - wanted) < 1e-3f) return;
            if ((wanted >= Leaving.Anything) != (limit.Value >= Leaving.Anything))
                log($"vehicle tune: {name} {(wanted >= Leaving.Anything ? "is tipped, the rider may get off at any speed" : "stands again, the rider may get off at a walking pace")}");
            limit.Value = wanted;
        }

        PlayMakerFSM FindFsm(string path, string fsmName)
        {
            var t = car.Find(path);
            if (t == null) return null;
            foreach (var f in t.GetComponents<PlayMakerFSM>())
                if (f.FsmName == fsmName) return f;
            return null;
        }

        void Tune()
        {
            tuned = true;
            if (settings.SoftBumps) BumpSound.Register(car, log);
            var dynamics = car.GetComponent<CarDynamics>();
            mass0 = body.mass;
            tunedMass = mass0 * settings.Mass;
            body.mass = tunedMass;
            if (settings.BottomLift > 0) LiftBottom();
            var center0 = body.centerOfMass;
            tunedCenter = center0 - Vector3.up * settings.CenterDrop;
            if (settings.CenterDrop > 0f) SetCenter(dynamics);
            var sb = new System.Text.StringBuilder();
            var axles = car.GetComponent<Axles>();
            if (dynamics != null && axles != null)
            {
                float damping = settings.Mass * Mathf.Sqrt(settings.Spring);
                foreach (var axle in new[] { axles.frontAxle, axles.rearAxle })
                {
                    if (axle == null) continue;
                    sb.Append($" axle travel {axle.suspensionTravel:0.###} rate {axle.suspensionRate:0} bump {axle.bumpRate:0} rebound {axle.reboundRate:0} grip {axle.forwardGripFactor:0.##}/{axle.sidewaysGripFactor:0.##} brake {axle.brakeFrictionTorque:0}");
                    TuneAxle(axle, damping, axle == axles.frontAxle && settings.FrontSpring > 0f ? settings.FrontSpring : 1f);
                    if (axle == axles.rearAxle && settings.RearBrake > 0f) axle.brakeFrictionTorque *= settings.RearBrake;
                }
                if (axles.otherAxles != null)
                    foreach (var axle in axles.otherAxles)
                        if (axle != null) TuneAxle(axle, damping, 1f);
                dynamics.SetWheelsParams();
            }
            var drivetrain = car.GetComponent<Drivetrain>();
            if (drivetrain != null && settings.Power > 0f)
            {
                power0 = drivetrain.powerMultiplier;
                SetPower(drivetrain, power0 * settings.Power);
                sb.Append($", engine power x{power0:0.##} -> x{tunedPower:0.##}, clutch {drivetrain.clutchMaxTorque:0} Nm");
            }
            log($"vehicle tune: {name} mass {mass0:0} -> {tunedMass:0} kg, center of mass {center0} -> {tunedCenter}" +
                (dynamics == null ? ", no CarDynamics (suspension left alone)" : $", suspension x{settings.Mass * settings.Spring:0.##} rate x{settings.Travel:0.##} travel{(settings.FrontSpring > 0f ? $", front rate x{settings.FrontSpring:0.##} on top" : "")}, was{sb}"));
        }

        void TuneAxle(Axle axle, float damping, float spring)
        {
            damping *= Mathf.Sqrt(spring);
            axle.suspensionTravel *= settings.Travel;
            axle.suspensionRate *= settings.Mass * settings.Spring * spring;
            axle.bumpRate *= damping * (settings.Bump > 0f ? settings.Bump : 1f);
            axle.reboundRate *= damping * (settings.Rebound > 0f ? settings.Rebound : 1f);
            if (settings.Grip > 0f) axle.forwardGripFactor *= settings.Grip;
            if (settings.SideGrip > 0f) axle.sidewaysGripFactor *= settings.SideGrip;
        }

        // UnityCar scales the engine's torque, friction and inertia by powerMultiplier, and the
        // clutch's grip with it (CalcClutchTorque), so the engine pulls harder but revs the same.
        void SetPower(Drivetrain drivetrain, float multiplier)
        {
            tunedPower = multiplier;
            drivetrain.powerMultiplier = multiplier;
            drivetrain.CalcClutchTorque();
            drivetrain.CalcIdleThrottle();
        }

        // Moves the bottom of the body colliders up by BottomLift (in the car's space), the top stays.
        // A box gets shorter; a mesh has the vertices below the new bottom moved up to it.
        void LiftBottom()
        {
            foreach (var c in car.GetComponentsInChildren<Collider>(true))
            {
                if (c.isTrigger || Array.IndexOf(BodyColliders, c.name) < 0) continue;
                var t = c.transform;
                var box = c as BoxCollider;
                var mesh = c as MeshCollider;
                if (box != null)
                {
                    // Assumes the box is upright in the car (MSC's are); its local y is the car's.
                    float scale = Mathf.Abs(t.lossyScale.y / car.lossyScale.y);
                    float lift = Mathf.Min(settings.BottomLift / Mathf.Max(scale, 1e-4f), box.size.y * 0.5f);
                    box.center += Vector3.up * lift * 0.5f;
                    box.size -= Vector3.up * lift;
                    log($"vehicle tune: {name} {c.name} box bottom up {lift * scale:0.00} m");
                }
                else if (mesh != null && mesh.sharedMesh != null)
                {
                    Vector3[] v;
                    try { v = mesh.sharedMesh.vertices; }
                    catch (Exception e) { log($"vehicle tune: {name} {c.name} mesh not readable: {e.Message}"); continue; }
                    float bottom = float.MaxValue;
                    var inCar = new Vector3[v.Length];
                    for (int i = 0; i < v.Length; i++)
                    {
                        inCar[i] = car.InverseTransformPoint(t.TransformPoint(v[i]));
                        bottom = Mathf.Min(bottom, inCar[i].y);
                    }
                    float floor = bottom + settings.BottomLift;
                    int moved = 0;
                    for (int i = 0; i < v.Length; i++)
                        if (inCar[i].y < floor)
                        {
                            inCar[i].y = floor;
                            v[i] = t.InverseTransformPoint(car.TransformPoint(inCar[i]));
                            moved++;
                        }
                    var lifted = UnityEngine.Object.Instantiate(mesh.sharedMesh);
                    lifted.vertices = v;
                    lifted.RecalculateBounds();
                    mesh.sharedMesh = null;  // makes PhysX cook the new mesh
                    mesh.sharedMesh = lifted;
                    log($"vehicle tune: {name} {c.name} mesh bottom {bottom:0.00} -> {floor:0.00} m in the car, {moved}/{v.Length} vertices moved{(mesh.convex ? "" : ", not convex")}");
                }
                else log($"vehicle tune: {name} {c.name} is a {c.GetType().Name}, bottom left alone");
            }
        }

        void SetCenter(CarDynamics dynamics)
        {
            // CarDynamics keeps its own copy (its CoG child, x/y/zlocalPosition) and puts it back on the body.
            if (dynamics != null) dynamics.SetCenterOfMass(tunedCenter);
            else body.centerOfMass = tunedCenter;
        }

        // Every frame: MSC moved the center of mass (CarDynamics copies its CoG object onto the body
        // each physics step), so it goes down by CenterDrop from MSC's new spot.
        void FollowCenter()
        {
            if (settings.CenterDrop <= 0f) return;
            var center = body.centerOfMass;
            if ((center - tunedCenter).sqrMagnitude <= 1e-6f) return;
            tunedCenter = center - Vector3.up * settings.CenterDrop;
            SetCenter(car.GetComponent<CarDynamics>());
            if (++moves <= 5) log($"vehicle tune: {name} center of mass moved by MSC to {center}, now {tunedCenter}");
        }

        // MSC (or Unity, when colliders change) may put the mass or engine power back; tune them
        // again and say so, to learn when it happens.
        void Watch()
        {
            if (!tuned) return;
            bool mass = Mathf.Abs(body.mass - tunedMass) > 0.5f && Mathf.Abs(body.mass - mass0) < 0.5f;
            var drivetrain = tunedPower > 0f ? car.GetComponent<Drivetrain>() : null;
            bool power = drivetrain != null && Mathf.Abs(drivetrain.powerMultiplier - tunedPower) > 1e-3f;
            if (!mass && !power) return;
            if (mass) body.mass = tunedMass;
            float set = power ? drivetrain.powerMultiplier : 0f;
            if (power) SetPower(drivetrain, set * settings.Power);  // MSC changed it: tune its new value
            if (++resets <= 5)
                log($"vehicle tune: {name} {(mass ? "mass " : "")}{(power ? $"engine power (MSC set x{set:0.##}) " : "")}was put back, tuned again");
        }
    }

    // Remembers the vehicle's last collision (which of its colliders, against what, where and which
    // way in its own space), so a crash or a stop on a bridge lip says what it hit.
    public class ContactProbe : MonoBehaviour
    {
        string last;
        float at = float.NegativeInfinity;

        void OnCollisionEnter(Collision collision)
        {
            if (collision.contacts.Length == 0) return;
            var c = collision.contacts[0];
            var p = transform.InverseTransformPoint(c.point);
            var n = transform.InverseTransformDirection(c.normal);
            last = $"{c.thisCollider.name} on {collision.collider.name} at ({p.x:0.00}, {p.y:0.00}, {p.z:0.00}) normal ({n.x:0.00}, {n.y:0.00}, {n.z:0.00})";
            at = Time.time;
        }

        public string Recent(float within) => Time.time - at <= within ? last : null;
    }
}
