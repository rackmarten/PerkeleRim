using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // Skyrim's actors near the player (ActorList) as capsules on the island, and MSC's punch.
    //  - One kinematic CapsuleCollider per listed actor, moved with Skyrim's pose (carried on by its
    //    velocity between lists), on a layer of its own that only collides with the player's: the
    //    player bumps into people, while vehicles, their wheels and items pass through (and while
    //    the player drives they are off, so nothing the seated player has can stop the van). Huge
    //    actors (RunOver.Solid) go on a second layer that collides with everything but the
    //    capsules: a mammoth stops the van.
    //  - MSC's Hit key (H) throws MSC's own punch; a moment later, when the fist is out, a fist-sized
    //    sweep from the camera finds the capsule it lands on and Skyrim gets a PunchEvent.
    //  - An item thrown out of the hand (right button) is followed until it passes through a capsule
    //    (ImpactEvent, kImpactThrown) or slows down; bottles fly twice as fast (Throwing).
    //  - While the piss stream is on, its drops inside a capsule hit that actor (kImpactPiss).
    //  - The vehicle the player drives runs over the capsules its box touches (kImpactVehicle,
    //    RunOver); it passes through them, so it neither stops nor counts a crash, and the player
    //    swears (ranOver).
    //  - While MSC's push stance is up (J), capsules the hands touch are shoved out of the way
    //    (kImpactPush, Shove), again and again while the player walks into them.
    public unsafe class ActorLink
    {
        const float CarryOnMax = 0.25f;  // seconds a pose is carried on by its velocity at most
        const float PunchDelay = 0.15f;  // seconds from the key to the fist being out
        const float PunchReach = 2.0f;   // meters from the camera, a little past a sword's reach in Skyrim
        const float FistRadius = 0.15f;
        const float PushEvery = 0.8f;    // seconds between shoves of one actor: one stumble each
        const float PlayerRadius = 0.3f;
        const string FluidPath = "Pivot/AnimPivot/Camera/FPSCamera/Piss/Fluid";
        const int DropChecksMax = 64;    // piss drops looked at per frame
        // Without particles to read, the stream is an arc from Fluid along the camera at this speed.
        const float ArcSpeed = 3.5f;     // meters per second
        const float ArcSeconds = 1.2f;
        const int ArcSteps = 24;

        class Flight
        {
            public Rigidbody Body;
            public float Since;
            public bool Boosted;  // its throw speed seen (and doubled for a bottle)
            public Vector3 Last;
        }

        readonly Action<string> log;
        readonly Action<string> ranOver;  // the driven vehicle hit someone, for the log
        readonly List<Rigidbody> held = new List<Rigidbody>();
        readonly List<Flight> flights = new List<Flight>();
        float throwButtonAt = -10f;
        Transform fluid;
        bool fluidLooked, particlesLogged;
        ParticleSystem.Particle[] drops = new ParticleSystem.Particle[256];
        readonly HitPacer pissPacer = new HitPacer(Throwing.PissEvery);
        readonly HitPacer runOverPacer = new HitPacer(RunOver.IgnoreSeconds);
        readonly HitPacer pushPacer = new HitPacer(PushEvery);
        readonly List<KeyValuePair<int, Vec3>> touched = new List<KeyValuePair<int, Vec3>>();
        Rigidbody boxOf;  // the vehicle boxMin..boxMax were measured on
        Vector3 boxMin, boxMax;
        readonly HashSet<uint> pissed = new HashSet<uint>();
        readonly Dictionary<uint, CapsuleCollider> capsules = new Dictionary<uint, CapsuleCollider>();
        readonly List<uint> gone = new List<uint>();
        List<ActorBody> bodies = new List<ActorBody>();
        GameObject root;
        int layer = -1;  // the capsules' own layer, -1 = not picked yet
        int solidLayer = -1;  // heavy actors' capsules
        Rigidbody lastVehicle;
        Vector3 lastVehicleVelocity;  // lastVehicle's, last frame: before it hit something solid
        uint listFrame;
        float listTime;
        float punchAt = -1f;  // when the pending punch's fist is out, -1 = none
        readonly Island island;

        public ActorLink(Island island, Action<string> log, Action<string> ranOver)
        {
            this.island = island;
            this.log = log;
            this.ranOver = ranOver;
        }

        // Call every LateUpdate. playing: MSC owns the player and it stands on the current island;
        // driving: it sits in a vehicle (no punching), driven: that vehicle's body; view: MSC's
        // rendering camera; pushing: MSC's push stance is up (PlayerFunctions in On); feet: the
        // player's.
        public void Update(Link link, bool playing, Transform player, bool driving, Rigidbody driven, Transform view, bool pushing, Vector3 feet)
        {
            ActorList list;
            if (!playing || player == null || !link.ReadActors(out list))
            {
                Clear();
                listFrame = 0;
                punchAt = -1f;
                held.Clear();
                flights.Clear();
                pissPacer.Clear();
                runOverPacer.Clear();
                pushPacer.Clear();
                return;
            }
            if (list.frame != listFrame)
            {
                listFrame = list.frame;
                listTime = Time.time;
                bodies = Actors.FromList(ref list, island);
            }
            if (layer < 0) PickLayer(player.gameObject.layer);
            Place(driving);
            Punch(link, driving, view);
            Throws(link, player, driving);
            Piss(link, player, view);
            RunOverBodies(link, driven);
            if (pushing && !driving) Shove(link, player, view, feet);
        }

        // The driven vehicle's box: its own non-trigger colliders' bounds in its space, measured
        // once per vehicle (the player and loose parts with bodies of their own left out).
        void Measure(Rigidbody vehicle)
        {
            boxOf = vehicle;
            var space = vehicle.transform;
            bool any = false;
            foreach (var c in vehicle.GetComponentsInChildren<Collider>())
            {
                if (c.isTrigger || !c.enabled || c.attachedRigidbody != vehicle) continue;
                Bounds local;
                var box = c as BoxCollider;
                var mesh = c as MeshCollider;
                if (box != null) local = new Bounds(box.center, box.size);
                else if (mesh != null && mesh.sharedMesh != null) local = mesh.sharedMesh.bounds;
                else local = new Bounds(c.transform.InverseTransformPoint(c.bounds.center), c.transform.InverseTransformVector(c.bounds.size));
                var e = local.extents;
                for (int k = 0; k < 8; k++)
                {
                    var corner = local.center + new Vector3((k & 1) != 0 ? e.x : -e.x, (k & 2) != 0 ? e.y : -e.y, (k & 4) != 0 ? e.z : -e.z);
                    var p = space.InverseTransformPoint(c.transform.TransformPoint(corner));
                    if (!any) { boxMin = boxMax = p; any = true; }
                    else { boxMin = Vector3.Min(boxMin, p); boxMax = Vector3.Max(boxMax, p); }
                }
            }
            if (!any) boxMin = boxMax = Vector3.zero;
            log($"actors: {vehicle.name} runs people over as a box {boxMin} .. {boxMax}");
        }

        // Every capsule the driven vehicle's box touches while it moves fast enough is run over.
        void RunOverBodies(Link link, Rigidbody vehicle)
        {
            var v = vehicle != null ? vehicle.velocity : Vector3.zero;
            var before = vehicle == lastVehicle ? lastVehicleVelocity : Vector3.zero;
            lastVehicle = vehicle;
            lastVehicleVelocity = v;
            if (vehicle == null || bodies.Count == 0) return;
            if (before.magnitude > v.magnitude) v = before;  // stopped by a solid capsule this frame
            if (v.magnitude < RunOver.SpeedMin) return;
            if (vehicle != boxOf) Measure(vehicle);
            if (boxMin == boxMax) return;
            var space = vehicle.transform;
            var min = new Vec3(boxMin.x, boxMin.y, boxMin.z);
            var max = new Vec3(boxMax.x, boxMax.y, boxMax.z);
            foreach (var b in BodiesNow())
            {
                var bottom = space.InverseTransformPoint(new Vector3(b.Bottom.x, b.Bottom.y, b.Bottom.z));
                var top = space.InverseTransformPoint(new Vector3(b.Top.x, b.Top.y, b.Top.z));
                if (!RunOver.Touches(min, max, new Vec3(bottom.x, bottom.y, bottom.z), new Vec3(top.x, top.y, top.z), b.Radius)) continue;
                if (!runOverPacer.Ready(b.Id, Time.time)) continue;
                bool sent = SendImpact(link, b.Id, Wire.ImpactVehicle, v.magnitude, v);
                log($"actors: the {vehicle.name} ran over {b.Id:X8} at {v.magnitude * 3.6f:0} km/h{(sent ? "" : ", but the ring is full")}");
                ranOver($"ran over {b.Id:X8}");
            }
        }

        // The listed bodies where they are now, carried on by their velocity since the list.
        List<ActorBody> BodiesNow()
        {
            float carry = Mathf.Min(Time.time - listTime, CarryOnMax);
            var now = new List<ActorBody>(bodies.Count);
            foreach (var b in bodies)
            {
                var moved = b;
                moved.Feet = new Vec3(b.Feet.x + b.Velocity.x * carry, b.Feet.y + b.Velocity.y * carry, b.Feet.z + b.Velocity.z * carry);
                now.Add(moved);
            }
            return now;
        }

        static bool SendImpact(Link link, uint actor, uint kind, float speed, Vector3 way)
        {
            var skyrimWay = Coords.UnityToSkyrim(new Vec3(way.x, way.y, way.z));
            float length = Mathf.Sqrt(skyrimWay.x * skyrimWay.x + skyrimWay.y * skyrimWay.y + skyrimWay.z * skyrimWay.z);
            var impact = new ImpactEvent { actor = actor, kind = kind, speed = speed };
            if (length > 1e-6f)
            {
                impact.direction[0] = skyrimWay.x / length;
                impact.direction[1] = skyrimWay.y / length;
                impact.direction[2] = skyrimWay.z / length;
            }
            var payload = new byte[sizeof(ImpactEvent)];
            fixed (byte* p = payload) *(ImpactEvent*)p = impact;
            return link.Send(MessageType.ImpactEvent, payload, payload.Length);
        }

        // Items leaving the hand right after the throw button become flights; each flight is swept
        // against the capsules from where it was last frame.
        void Throws(Link link, Transform player, bool driving)
        {
            if (InputShim.GetMouseButton(1)) throwButtonAt = Time.time;
            foreach (var body in held)
            {
                if (body == null || body.transform.IsChildOf(player)) continue;
                if (Time.time - throwButtonAt > Throwing.ThrowWindow) continue;  // dropped, not thrown
                flights.Add(new Flight { Body = body, Since = Time.time, Boosted = !Throwing.IsBottle(body.name), Last = body.position });
                // Small and fast, it would pass through the island's thin ground between physics steps.
                body.collisionDetectionMode = CollisionDetectionMode.Continuous;
                log($"actors: {body.name} thrown");
            }
            held.Clear();
            if (!driving)
                foreach (var body in player.GetComponentsInChildren<Rigidbody>())
                    if (body.transform != player) held.Add(body);

            if (flights.Count == 0) return;
            var now = BodiesNow();
            for (int i = flights.Count - 1; i >= 0; i--)
            {
                var f = flights[i];
                if (f.Body == null || f.Body.transform.IsChildOf(player) || Time.time - f.Since > Throwing.FlightMax)
                {
                    flights.RemoveAt(i);
                    continue;
                }
                var v = f.Body.velocity;
                if (!f.Boosted && v.magnitude > 1f)
                {
                    f.Body.velocity = v *= Throwing.BottleBoost;
                    f.Boosted = true;
                    log($"actors: {f.Body.name} thrown at {v.magnitude:0.0} m/s (a bottle, {Throwing.BottleBoost}x MSC's throw)");
                }
                else if (!f.Boosted && Time.time - f.Since > Throwing.BoostWindow) f.Boosted = true;
                var at = f.Body.position;
                int hit = v.magnitude >= Throwing.SpeedMin
                    ? Throwing.Sweep(new Vec3(f.Last.x, f.Last.y, f.Last.z), new Vec3(at.x, at.y, at.z), Throwing.ItemRadius, now)
                    : -1;
                f.Last = at;
                if (hit < 0) continue;
                bool sent = SendImpact(link, now[hit].Id, Wire.ImpactThrown, v.magnitude, v);
                f.Body.velocity = -v * Throwing.Bounce;  // capsules don't collide with items: bounce it off by hand
                flights.RemoveAt(i);
                log($"actors: {f.Body.name} hit {now[hit].Id:X8} at {v.magnitude:0.0} m/s{(sent ? "" : ", but the ring is full")}");
            }
        }

        // While MSC's piss stream (Fluid) is on: its drops inside a capsule hit that actor, each one
        // every Throwing.PissEvery seconds at most.
        void Piss(Link link, Transform player, Transform view)
        {
            if (!fluidLooked)
            {
                fluidLooked = true;
                fluid = player.Find(FluidPath);
                if (fluid == null) log($"actors: no {FluidPath} under the player, piss hits nobody");
            }
            if (fluid == null || !fluid.gameObject.activeInHierarchy || bodies.Count == 0) return;
            var now = BodiesNow();
            pissed.Clear();
            int checks = 0;
            foreach (var system in fluid.GetComponentsInChildren<ParticleSystem>())
            {
                if (drops.Length < system.maxParticles) drops = new ParticleSystem.Particle[system.maxParticles];
                int n = system.GetParticles(drops);
                bool world = system.simulationSpace == ParticleSystemSimulationSpace.World;
                int step = Mathf.Max(1, n / DropChecksMax);
                for (int i = 0; i < n; i += step, checks++)
                {
                    var p = world ? drops[i].position : system.transform.TransformPoint(drops[i].position);
                    int hit = Throwing.Sweep(new Vec3(p.x, p.y, p.z), new Vec3(p.x, p.y, p.z), Throwing.DropRadius, now);
                    if (hit >= 0) pissed.Add(now[hit].Id);
                }
            }
            if (checks == 0)
            {
                if (!particlesLogged) { particlesLogged = true; log("actors: no piss particles to read, the stream is an arc from Fluid"); }
                var from = fluid.position;
                var v = (view != null ? view.forward : fluid.forward) * ArcSpeed;
                for (int i = 0; i < ArcSteps; i++)
                {
                    float t0 = ArcSeconds * i / ArcSteps, t1 = ArcSeconds * (i + 1) / ArcSteps;
                    var a = from + v * t0 + Physics.gravity * (0.5f * t0 * t0);
                    var b = from + v * t1 + Physics.gravity * (0.5f * t1 * t1);
                    int hit = Throwing.Sweep(new Vec3(a.x, a.y, a.z), new Vec3(b.x, b.y, b.z), Throwing.DropRadius, now);
                    if (hit >= 0) { pissed.Add(now[hit].Id); break; }
                }
            }
            var way = view != null ? view.forward : fluid.forward;
            foreach (var id in pissed)
            {
                if (!pissPacer.Ready(id, Time.time)) continue;
                bool sent = SendImpact(link, id, Wire.ImpactPiss, 0f, way);
                log($"actors: piss on {id:X8}{(sent ? "" : ", but the ring is full")}");
            }
        }

        // Two unused layers: one colliding with the player's layer only, one (heavy actors) with
        // everything but the capsules.
        void PickLayer(int playerLayer)
        {
            for (int i = 31; i >= 8 && solidLayer < 0; i--)
                if (string.IsNullOrEmpty(LayerMask.LayerToName(i)))
                {
                    if (layer < 0) layer = i;
                    else solidLayer = i;
                }
            if (solidLayer < 0)
            {
                layer = solidLayer = 0;
                log("actors: no two free layers, capsules go on Default and collide with everything");
                return;
            }
            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(layer, i, i != playerLayer);
                if (i != layer) Physics.IgnoreLayerCollision(solidLayer, i, i == solidLayer);
            }
            log($"actors: capsules on layer {layer}, colliding with the player's layer {playerLayer} ({LayerMask.LayerToName(playerLayer)}) only; heavy ones on {solidLayer}, solid to vehicles");
        }

        // driving: the player sits in a vehicle, light capsules are off (RunOver hits them by its box).
        void Place(bool driving)
        {
            float carry = Mathf.Min(Time.time - listTime, CarryOnMax);
            gone.Clear();
            foreach (var id in capsules.Keys) gone.Add(id);
            foreach (var b in bodies)
            {
                gone.Remove(b.Id);
                CapsuleCollider capsule;
                if (!capsules.TryGetValue(b.Id, out capsule)) capsule = capsules[b.Id] = Create(b.Id);
                bool solid = RunOver.Solid(b);
                int want = solid ? solidLayer : layer;
                if (capsule.gameObject.layer != want) capsule.gameObject.layer = want;
                if (capsule.enabled != (solid || !driving)) capsule.enabled = solid || !driving;
                capsule.radius = b.Radius;
                capsule.height = Mathf.Max(b.Height, b.Radius * 2f);
                var feet = new Vector3(b.Feet.x, b.Feet.y, b.Feet.z) + new Vector3(b.Velocity.x, b.Velocity.y, b.Velocity.z) * carry;
                capsule.transform.position = feet + Vector3.up * (capsule.height * 0.5f);
            }
            foreach (var id in gone)
            {
                UnityEngine.Object.Destroy(capsules[id].gameObject);
                capsules.Remove(id);
            }
        }

        CapsuleCollider Create(uint id)
        {
            if (root == null)
            {
                root = new GameObject("PerkeleRim.Actors");
                UnityEngine.Object.DontDestroyOnLoad(root);
            }
            var go = new GameObject($"PerkeleRim.Actor {id:X8}") { layer = layer };
            go.transform.parent = root.transform;
            var body = go.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var capsule = go.AddComponent<CapsuleCollider>();
            capsule.direction = 1;  // upright
            return capsule;
        }

        void Clear()
        {
            if (capsules.Count == 0) return;
            foreach (var c in capsules.Values) UnityEngine.Object.Destroy(c.gameObject);
            capsules.Clear();
            bodies.Clear();
            log("actors: cleared the capsules");
        }

        void Punch(Link link, bool driving, Transform view)
        {
            if (!driving && view != null && punchAt < 0f && cInput.GetKeyDown("Hit")) punchAt = Time.time + PunchDelay;
            if (punchAt < 0f || Time.time < punchAt) return;
            punchAt = -1f;
            if (driving || view == null) return;

            var now = BodiesNow();
            var from = view.position;
            var way = view.forward;
            float distance;
            int hit = Actors.Punch(new Vec3(from.x, from.y, from.z), new Vec3(way.x, way.y, way.z), PunchReach, FistRadius, now, out distance, Actors.ReachTop);
            if (hit < 0)
            {
                log($"actors: punch hit nobody ({now.Count} actors near)");
                return;
            }

            var skyrimWay = Coords.UnityToSkyrim(new Vec3(way.x, way.y, way.z));
            float length = Mathf.Sqrt(skyrimWay.x * skyrimWay.x + skyrimWay.y * skyrimWay.y + skyrimWay.z * skyrimWay.z);
            var punch = new PunchEvent { actor = now[hit].Id };
            punch.direction[0] = skyrimWay.x / length;
            punch.direction[1] = skyrimWay.y / length;
            punch.direction[2] = skyrimWay.z / length;
            var payload = new byte[sizeof(PunchEvent)];
            fixed (byte* p = payload) *(PunchEvent*)p = punch;
            bool sent = link.Send(MessageType.PunchEvent, payload, payload.Length);
            log($"actors: punch landed on {now[hit].Id:X8} at {distance:0.00} m{(sent ? "" : ", but the ring is full")}");
        }

        // MSC's push stance (J, both hands up): every capsule the hands touch in front is shoved away
        // from the player (ImpactEvent, kImpactPush), again every PushEvery seconds while it stays in
        // touch, so walking into someone moves them along.
        void Shove(Link link, Transform player, Transform view, Vector3 feet)
        {
            if (bodies.Count == 0) return;
            var facing = view != null ? view.forward : player.forward;
            var level = new Vector3(facing.x, 0f, facing.z);
            if (level.sqrMagnitude < 1e-6f) return;
            level.Normalize();
            var now = BodiesNow();
            Actors.Touching(new Vec3(feet.x, feet.y, feet.z), new Vec3(level.x, 0f, level.z), PlayerRadius, now, touched);
            foreach (var t in touched)
            {
                var b = now[t.Key];
                if (!pushPacer.Ready(b.Id, Time.time)) continue;
                bool sent = SendImpact(link, b.Id, Wire.ImpactPush, 0f, new Vector3(t.Value.x, 0f, t.Value.z));
                log($"actors: shoved {b.Id:X8}{(sent ? "" : ", but the ring is full")}");
            }
        }
    }
}
