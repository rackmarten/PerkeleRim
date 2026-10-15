using System;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Makes an MSC vehicle hold Skyrim's roads (MSC's cars are UnityCar: CarDynamics on the root,
    // Axles copied onto raycast Wheels): heavier, a lower center of mass, a softer and longer
    // suspension. None of this is in MSC's save, so nothing is put back.
    public class VehicleTune
    {
        public struct Settings
        {
            public float Mass;        // times MSC's mass
            public float CenterDrop;  // meters the center of mass goes down
            public float Spring;      // spring rate times this (and times Mass, so it sags as before)
            public float Travel;      // suspension travel times this
            public float BottomLift;  // meters the body collider's bottom goes up
        }

        // The van's wheels sit far behind its bumpers, so the body's overhang reached a lip or a ramp
        // before the wheels and stopped the van dead. With its bottom lifted the overhang clips into
        // the ground instead and the wheels climb.
        public static readonly Settings Van = new Settings { Mass = 1.2f, CenterDrop = 0.2f, Spring = 0.85f, Travel = 1.2f, BottomLift = 0.25f };
        public static readonly Settings Moped = new Settings { Mass = 1.2f, CenterDrop = 0.1f, Spring = 1f, Travel = 1f };
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
        float tuneAt, nextWatch;
        bool tuned;
        float mass0, tunedMass;
        Vector3 tunedCenter;
        int resets;

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
            tuneAt = Time.realtimeSinceStartup + TuneDelay;
        }

        // What the vehicle last touched, if within the last seconds (for the crash log), else null.
        public string Contact(float within) => probe != null ? probe.Recent(within) : null;

        // Every frame the vehicle is on the island.
        public void Update()
        {
            if (car == null || body == null || Time.realtimeSinceStartup < tuneAt) return;
            if (!tuned) Tune();
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
            SetCenter(dynamics);
            var sb = new System.Text.StringBuilder();
            var axles = car.GetComponent<Axles>();
            if (dynamics != null && axles != null)
            {
                float damping = settings.Mass * Mathf.Sqrt(settings.Spring);
                foreach (var axle in new[] { axles.frontAxle, axles.rearAxle })
                {
                    if (axle == null) continue;
                    sb.Append($" axle travel {axle.suspensionTravel:0.###} rate {axle.suspensionRate:0} bump {axle.bumpRate:0} rebound {axle.reboundRate:0}");
                    axle.suspensionTravel *= settings.Travel;
                    axle.suspensionRate *= settings.Mass * settings.Spring;
                    axle.bumpRate *= damping;
                    axle.reboundRate *= damping;
                }
                if (axles.otherAxles != null)
                    foreach (var axle in axles.otherAxles)
                    {
                        if (axle == null) continue;
                        axle.suspensionTravel *= settings.Travel;
                        axle.suspensionRate *= settings.Mass * settings.Spring;
                        axle.bumpRate *= damping;
                        axle.reboundRate *= damping;
                    }
                dynamics.SetWheelsParams();
            }
            log($"vehicle tune: {name} mass {mass0:0} -> {tunedMass:0} kg, center of mass {center0} -> {tunedCenter}" +
                (dynamics == null ? ", no CarDynamics (suspension left alone)" : $", suspension x{settings.Mass * settings.Spring:0.##} rate x{settings.Travel:0.##} travel, was{sb}"));
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

        // MSC (or Unity, when colliders change) may put the mass or center of mass back; tune them
        // again and say so, to learn when it happens.
        void Watch()
        {
            if (!tuned) return;
            bool mass = Mathf.Abs(body.mass - tunedMass) > 0.5f && Mathf.Abs(body.mass - mass0) < 0.5f;
            bool center = (body.centerOfMass - tunedCenter).sqrMagnitude > 1e-6f;
            if (!mass && !center) return;
            if (mass) body.mass = tunedMass;
            if (center) SetCenter(car.GetComponent<CarDynamics>());
            if (++resets <= 5)
                log($"vehicle tune: {name} {(mass ? "mass " : "")}{(center ? "center of mass " : "")}was put back, tuned again");
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
