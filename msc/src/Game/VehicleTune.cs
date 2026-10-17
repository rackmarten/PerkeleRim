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
            public bool Air;          // lands its jumps while ridden (AirAssist)
        }

        // The van's wheels sit far behind its bumpers, so the body's overhang reached a lip or a ramp
        // before the wheels and stopped the van dead. With its bottom lifted the overhang clips into
        // the ground instead and the wheels climb. On Skyrim's steep roads the rear wheels spun uphill
        // and locked braking downhill, so the back slid out: more grip both ways, a weaker rear brake.
        public static readonly Settings Van = new Settings { Mass = 1.2f, CenterDrop = 0.2f, Spring = 0.85f, Travel = 1.2f, BottomLift = 0.25f, Grip = 1.3f, SideGrip = 1.3f, RearBrake = 0.75f };
        // The moped keeps MSC's mass and follows MSC's center of mass moves: MSC balances it by moving the center (-0.1 m while
        // ridden, 0.4 m at speed, 0.2 m tipped over or left alone, a bit to the side on its
        // kickstand), and the extra mass stalled its little engine pulling away. Its 8 cm of travel
        // bottomed out in Skyrim's road holes and UnityCar's bump stop threw it up and onto its side,
        // and the fork sprang back from a bump and threw the rider over the handlebar: softer and
        // longer, giving way to a bump and slow to spring back. Twice the travel was too much, so
        // 1.6 times; the fork is softer still than the rear. Its center of mass still goes 15 cm
        // below wherever MSC puts it (FollowCenter), or every bump pitched it over the front wheel.
        public static readonly Settings Moped = new Settings { Mass = 1f, CenterDrop = 0.15f, Spring = 0.6f, Travel = 1.6f, Bump = 0.8f, Rebound = 1.5f, FrontSpring = 0.75f, Air = true };
        // Skyrim is all hills: the tractor gets the van's softer, longer suspension, twice the engine
        // torque (it barely got rolling uphill even in low first) and more grip to put it down.
        public static readonly Settings Kekmet = new Settings { Mass = 1.2f, CenterDrop = 0.1f, Spring = 0.85f, Travel = 1.2f, Power = 2f, Grip = 1.3f };
        const string BodyCollider = "CarCollider";

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
            probe = car.GetComponent<ContactProbe>();
            if (probe == null) probe = car.gameObject.AddComponent<ContactProbe>();
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
            if (car == null || body == null || Time.realtimeSinceStartup < tuneAt) return;
            if (!tuned) Tune();
            FollowCenter();
            if (Time.realtimeSinceStartup >= nextWatch)
            {
                nextWatch = Time.realtimeSinceStartup + WatchEvery;
                Watch();
            }
        }

        void Tune()
        {
            tuned = true;
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
                if (c.isTrigger || c.name != BodyCollider) continue;
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
