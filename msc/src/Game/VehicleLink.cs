using System;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // One MSC vehicle on the collision island, driven by PlayerLink (one instance per vehicle):
    //  - when the player is put down on a new anchor on foot, the vehicle is parked next to it;
    //  - when the player sits in it, the vehicle carries the player to the new anchor instead;
    //  - while there is no island ground under it, it is held (kinematic) and keeps its speed for
    //    when the ground arrives, so it never falls off the edge of the streamed area;
    //  - writes VehicleState (Skyrim world space) every frame if it has a wire id (only the van so
    //    far; the protocol carries one vehicle).
    public unsafe class VehicleLink
    {
        // Drop the vehicle from this high above the ground so its wheels settle on it.
        const float ParkClearance = 0.6f;
        const float GroundCheckEvery = 0.25f;
        // Half width and half length (meters) of the ground that must be flat where a vehicle parks
        // (van sized; generous for the moped), and how uneven it may be.
        static readonly Vector2 ParkHalfSize = new Vector2(1f, 2.3f);
        const float MaxParkStep = 0.4f;
        // The parts MSC turns and spins per wheel, in VehicleState.wheelSpin order (the van's names).
        static readonly string[] WheelModels = { "wheelFL/tire", "wheelFR/tire", "wheelRL/tire", "wheelRR/tire" };

        readonly string objectName;
        readonly string driverName;   // PlayerCurrentVehicle while driving it
        readonly Vector3 parkOffset;  // from the player's feet, in the player's frame (meters)
        readonly uint wireId;         // VehicleState.vehicle, 0 = not sent to Skyrim
        readonly string keyVariable;  // global Int the ignition checks, null = no key needed
        readonly Island island;
        readonly Action<string> log;
        readonly Func<Vector3, Vector3?> findGround;

        Transform car;
        Transform[] wheels;
        Rigidbody body;
        uint placedGeneration;
        bool held;
        Vector3 heldVelocity, heldAngular;
        float nextGroundCheck;
        bool lastDriven;
        float nextFind;

        public VehicleLink(string objectName, string driverName, Vector3 parkOffset, uint wireId, string keyVariable,
            Island island, Func<Vector3, Vector3?> findGround, Action<string> log)
        {
            this.objectName = objectName;
            this.driverName = driverName;
            this.parkOffset = parkOffset;
            this.wireId = wireId;
            this.keyVariable = keyVariable;
            this.island = island;
            this.findGround = findGround;
            this.log = log;
        }

        // The ground the vehicle stood on before it was first moved, for the island chunks: the
        // wheels may only see some layers, and grip may come from its material or tag. Null until known.
        public Collider Ground { get; private set; }

        bool PlayerDriving
        {
            get
            {
                var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
                return v != null && v.Value == driverName;
            }
        }

        public void Find()
        {
            if (car != null || Time.realtimeSinceStartup < nextFind) return;
            nextFind = Time.realtimeSinceStartup + 1f;
            var go = GameObject.Find(objectName);
            if (go == null) return;
            car = go.transform;
            body = go.GetComponent<Rigidbody>();
            placedGeneration = 0;
            held = false;
            Ground = ColliderBelow(car);
            wheels = new Transform[WheelModels.Length];
            int found = 0;
            for (int i = 0; i < wheels.Length; i++)
                if ((wheels[i] = car.Find(WheelModels[i])) != null) found++;
            if (wireId != 0) log($"vehicle: {driverName} has {found}/{wheels.Length} wheel models");
            // The pose goes out every rendered frame; without this it only moves on 50 Hz physics steps.
            if (body != null && body.interpolation == RigidbodyInterpolation.None)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
                log($"vehicle: {driverName} rigidbody interpolation on");
            }
            log($"vehicle: found {objectName} (rigidbody {(body != null)}), resting on " +
                (Ground != null ? $"{Ground.name} layer {Ground.gameObject.layer} ({LayerMask.LayerToName(Ground.gameObject.layer)}) tag {Ground.tag} material {(Ground.sharedMaterial != null ? Ground.sharedMaterial.name : "none")}" : "nothing"));
            GiveKey();
            Refuel();
        }

        // Fills the tank (<vehicle>/FuelTank :: Data) to its capacity, so a vehicle from a fresh save
        // runs. Logs the tank's floats, since their names were never dumped.
        void Refuel()
        {
            var tank = car.Find("FuelTank");
            if (tank == null) return;
            foreach (var fsm in tank.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Data") continue;
                HutongGames.PlayMaker.FsmFloat level = null, capacity = null;
                var names = new System.Text.StringBuilder();
                foreach (var f in fsm.FsmVariables.FloatVariables)
                {
                    names.Append($" {f.Name}={f.Value:0.##}");
                    if (f.Name.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0) level = f;
                    else if (f.Name.IndexOf("Capacity", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.StartsWith("Max", StringComparison.OrdinalIgnoreCase)) capacity = f;
                }
                log($"vehicle: {driverName} fuel tank floats:{names}");
                if (level != null && capacity != null && capacity.Value > 0f && level.Value < capacity.Value)
                {
                    level.Value = capacity.Value;
                    log($"vehicle: filled the {driverName} tank ({level.Name} = {capacity.Name} = {capacity.Value:0.##})");
                }
            }
        }

        // Newer MSC versions make you fetch the van key from your uncle first. Vehicles here are
        // ready to drive, so hand keys over the way MSC's own debug "Keys" command does (saved with the game).
        void GiveKey()
        {
            if (keyVariable == null) return;
            var key = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmInt(keyVariable);
            if (key == null || key.Value >= 1) return;
            key.Value = 1;
            log($"vehicle: gave the player the {driverName} key");
        }

        // True when moving the vehicle also moves the player (sitting in it, parented to it).
        public bool Carries(Transform player) => car != null && PlayerDriving && player.IsChildOf(car);

        // Moves the vehicle (and whatever rides in it) by delta, keeping its speed.
        public void Shift(Vector3 delta)
        {
            if (car == null) return;
            car.position += delta;
            if (body != null) body.position = car.position;
        }

        // Brings the vehicle the player sits in to a new anchor: across so the feet land over it, and
        // down onto its wheels at the ground there. Putting the seated feet on the ground sank the
        // vehicle into it, and the physics threw it, killing the player.
        public void CarryTo(Vector3 feet, Vector3 anchorGround)
        {
            if (car == null) return;
            var delta = anchorGround - feet;
            delta.y = anchorGround.y + ParkClearance - car.position.y;
            Shift(delta);
        }

        // Call every LateUpdate. playing: MSC owns the player and it stands on the current island.
        public void Update(bool playing, Transform player, Vector3 playerFeet)
        {
            var link = LinkHost.Current;
            var state = new VehicleState { vehicle = wireId, generation = island.Generation };
            if (car == null || !playing)
            {
                if (link != null && wireId != 0) link.WriteVehicle(ref state);
                return;
            }

            bool driven = Carries(player);
            if (driven != lastDriven)
            {
                lastDriven = driven;
                log(driven ? $"vehicle: player got in the {driverName}" : $"vehicle: player got out of the {driverName}");
            }

            if (placedGeneration != island.Generation)
            {
                placedGeneration = island.Generation;
                // A driven vehicle came along in PlayerLink.Settle already.
                if (!driven) Park(player, playerFeet);
            }

            if (Time.realtimeSinceStartup >= nextGroundCheck)
            {
                nextGroundCheck = Time.realtimeSinceStartup + GroundCheckEvery;
                Hold(!findGround(car.position).HasValue);
            }

            state.flags = Wire.VehicleValid | (driven ? Wire.VehicleDriven : 0);
            Fill(ref state);
            if (link != null && wireId != 0) link.WriteVehicle(ref state);
        }

        void Park(Transform player, Vector3 feet)
        {
            var below = ColliderBelow(car);
            if (below != null)
                log($"vehicle: {driverName} was standing on {below.name} (layer {below.gameObject.layer}, tag {below.tag}, material {(below.sharedMaterial != null ? below.sharedMaterial.name : "none")})");
            var yaw = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
            var spot = feet + yaw * parkOffset;
            var ground = FlatSpot(feet, yaw) ?? findGround(spot);
            spot = (ground ?? spot) + Vector3.up * ParkClearance;
            car.position = spot;
            car.rotation = yaw;
            if (body != null)
            {
                body.position = spot;
                body.rotation = yaw;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            heldVelocity = heldAngular = Vector3.zero;
            log($"vehicle: parked the {driverName} for gen {island.Generation} at ({spot.x:0.00},{spot.y:0.00},{spot.z:0.00}){(ground.HasValue ? "" : ", no ground yet, holding it")}");
        }

        // A spot next to the player whose ground is flat under the whole vehicle: the usual one, then
        // further ahead, further back, further out, all on the vehicle's own side so the van and the
        // moped never pick the same one. Parked over a rock, the vehicle spawned inside it and was
        // thrown, sometimes into the player. Returns the highest ground point there, or null.
        Vector3? FlatSpot(Vector3 feet, Quaternion yaw)
        {
            var outward = new Vector3(Mathf.Sign(parkOffset.x) * 3f, 0f, 0f);
            var offsets = new[]
            {
                parkOffset,
                parkOffset + Vector3.forward * 4f,
                parkOffset - Vector3.forward * 4f,
                parkOffset + outward,
            };
            foreach (var offset in offsets)
            {
                var center = feet + yaw * offset;
                float low = float.MaxValue, high = float.MinValue;
                bool ok = true;
                // The center, then the four corners.
                for (int i = 0; i < 5 && ok; i++)
                {
                    var local = i == 0 ? Vector3.zero : new Vector3((i & 1) != 0 ? ParkHalfSize.x : -ParkHalfSize.x, 0f, (i & 2) != 0 ? ParkHalfSize.y : -ParkHalfSize.y);
                    var g = findGround(center + yaw * local);
                    if (!g.HasValue) { ok = false; break; }
                    low = Mathf.Min(low, g.Value.y);
                    high = Mathf.Max(high, g.Value.y);
                }
                if (ok && high - low <= MaxParkStep)
                {
                    if (offset != parkOffset) log($"vehicle: ground uneven where the {driverName} parks, parking it at offset {offset}");
                    return new Vector3(center.x, high, center.z);
                }
            }
            log($"vehicle: no flat spot for the {driverName} around the player");
            return null;
        }

        void Hold(bool hold)
        {
            if (body == null || hold == held) return;
            held = hold;
            if (hold)
            {
                heldVelocity = body.velocity;
                heldAngular = body.angularVelocity;
                body.isKinematic = true;
                log($"vehicle: no ground under the {driverName}, holding it");
            }
            else
            {
                body.isKinematic = false;
                body.velocity = heldVelocity;
                body.angularVelocity = heldAngular;
                log($"vehicle: ground under the {driverName}, letting it go");
            }
        }

        void Fill(ref VehicleState s)
        {
            var pos = island.IslandToSkyrim(new Vec3(car.position.x, car.position.y, car.position.z));
            s.position[0] = pos.x;
            s.position[1] = pos.y;
            s.position[2] = pos.z;
            var r = car.rotation;
            var q = Coords.UnityToSkyrim(new Quat(r.x, r.y, r.z, r.w));
            s.rotation[0] = q.x;
            s.rotation[1] = q.y;
            s.rotation[2] = q.z;
            s.rotation[3] = q.w;
            var v = body != null ? (held ? heldVelocity : body.velocity) : Vector3.zero;
            var sv = Coords.UnityToSkyrim(new Vec3(v.x, v.y, v.z));
            s.velocity[0] = sv.x;
            s.velocity[1] = sv.y;
            s.velocity[2] = sv.z;
            FillWheels(ref s);
        }

        // Steer and spin from where MSC turned the wheel models, relative to the car (their rest pose
        // is unrotated). Unity: +X right, +Y up, +Z forward, left-handed.
        void FillWheels(ref VehicleState s)
        {
            var toCar = Quaternion.Inverse(car.rotation);
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] == null) continue;
                var q = toCar * wheels[i].rotation;
                // Spinning keeps the axle; steering (about up) turns it: axle = (cos a, 0, -sin a).
                var axle = q * Vector3.right;
                float steer = Mathf.Atan2(-axle.z, axle.x);
                if (i < 2) s.steer = steer;  // front wheels; the protocol carries one angle
                // Undo the steer; what is left turns about the axle. Rolling forward tips up toward +Z.
                var up = Quaternion.AngleAxis(-steer * Mathf.Rad2Deg, Vector3.up) * q * Vector3.up;
                s.wheelSpin[i] = Mathf.Atan2(up.z, up.y);
            }
        }

        // The first collider straight below t that isn't part of t itself.
        static Collider ColliderBelow(Transform t)
        {
            float best = float.MaxValue;
            Collider ground = null;
            foreach (var hit in Physics.RaycastAll(t.position + Vector3.up, Vector3.down, 10f))
            {
                if (hit.collider.transform.IsChildOf(t) || hit.distance >= best) continue;
                best = hit.distance;
                ground = hit.collider;
            }
            return ground;
        }
    }
}
