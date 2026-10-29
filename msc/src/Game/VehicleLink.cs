using System;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // One MSC vehicle on the collision island, driven by PlayerLink (one instance per vehicle):
    //  - when the player is put down on a new anchor on foot, the vehicle is parked next to it, or
    //    at the spot the anchor names for it (ParkAt); a vehicle on call (the moped, the Kekmet, the
    //    Gifu) is instead put away until the player calls it (Summon), which parks it in front of the
    //    player wherever it was;
    //  - when the player sits in it, the vehicle carries the player to the new anchor instead;
    //  - while there is no island ground under it, it is held (kinematic) and keeps its speed for
    //    when the ground arrives, so it never falls off the edge of the streamed area;
    //  - writes VehicleState (Skyrim world space) and VehicleParts (where every object of its model
    //    is relative to the body, and whether MSC draws it) every frame into its wire id's slot.
    public unsafe class VehicleLink
    {
        // Drop the vehicle from this high above the ground so its wheels settle on it, or from a bit
        // above the height it stood at in MSC when that is more (the Kekmet's big wheels).
        const float ParkClearance = 0.6f;
        const float RestMargin = 0.15f;
        const float GroundCheckEvery = 0.25f;
        // Half width and half length (meters) of the ground that must be flat where a vehicle parks
        // (van sized; generous for the moped), and how uneven it may be.
        static readonly Vector2 VanHalfSize = new Vector2(1f, 2.3f);
        // The parts MSC turns and spins per wheel, in VehicleState.wheelSpin order (the van's names).
        static readonly string[] WheelModels = { "wheelFL/tire", "wheelFR/tire", "wheelRL/tire", "wheelRR/tire" };

        readonly string objectName;
        readonly string driverName;   // PlayerCurrentVehicle while driving it
        readonly Vector3 parkOffset;  // from the player's feet, in the player's frame (meters); x = 0: ahead
        readonly Vector3? callOffset; // where Summon parks it instead of parkOffset, null = parkOffset
        bool calling;                 // Summon is parking it
        readonly uint wireId;         // VehicleState.vehicle
        readonly bool onCall;         // only parked by Summon, put away until then
        readonly string keyVariable;  // global Int the ignition checks, null = no key needed
        readonly Island island;
        readonly Action<string> log;
        readonly Func<Vector3, Vector3?> findGround;
        readonly VehicleTune tune;

        Transform car;
        Transform[] wheels;
        float clearance = ParkClearance;
        readonly VehicleModel model = new VehicleModel();
        Rigidbody body;
        uint placedGeneration;
        bool held;
        Vector3 heldVelocity, heldAngular;
        float nextGroundCheck;
        float nextTopUp;
        bool toppedUp;  // KeepFull logged its first top-up
        bool lastDriven;
        float nextFind;
        float nextReport;   // realtime of the next riding report
        float reportUntil;  // reports every half second until then (after righting it)
        // Where it was left in Skyrim (pose and worldspace or interior), kept up to date while it is
        // on the island; null until it was first placed. A new anchor elsewhere puts it away.
        Snapshot.Body home;
        bool away;
        // Where it last stood on its wheels (the ground under it, Skyrim units), for putting it back
        // when it sinks into the island: the nearest surface above could be a cliff.
        bool haveStood;
        Vec3 stood;
        float nextRescueLog;
        bool restoring;       // Restore placed it; before it is let go it must not be inside the island's statics
        uint spotGeneration;  // anchor generation the spot below is for, 0 = none
        Vec3 spot;            // Skyrim units, the ground under the vehicle's middle
        float spotHeading;    // Skyrim radians

        public VehicleLink(string objectName, string driverName, Vector3 parkOffset, uint wireId, bool onCall, string keyVariable,
            VehicleTune.Settings tuning, Island island, Func<Vector3, Vector3?> findGround, Action<string> log, Vector3? callOffset = null)
        {
            this.callOffset = callOffset;
            this.objectName = objectName;
            this.driverName = driverName;
            this.parkOffset = parkOffset;
            this.wireId = wireId;
            this.onCall = onCall;
            this.keyVariable = keyVariable;
            this.island = island;
            this.findGround = findGround;
            this.log = log;
            tune = new VehicleTune(tuning, driverName, log);
        }

        // The ground the vehicle stood on before it was first moved, for the island chunks: the
        // wheels may only see some layers, and grip may come from its material or tag. Null until known.
        public Collider Ground { get; private set; }

        // For CrashSensor: the vehicle's body, its name, and a count of the times it was moved or held
        // by hand (its velocity jumps then without a hit).
        public Rigidbody Body => body;
        public string Name => driverName;
        public Transform Car => car;  // null until the vehicle is found
        public string ObjectName => objectName;
        public Vector3 Position => car != null ? car.position : Vector3.zero;
        public int Moves { get; private set; }
        // The moped keeps a full tank (MSC fills it only from a two-stroke jerry can); the van, the
        // Kekmet and the Gifu burn their diesel and fill up at Skyrim's pumps (FuelLink).
        public bool EndlessFuel { get; set; }
        // Litres, for a tank whose Data has no MaxCapacity (the Gifu's); 0 reads it from the tank.
        public float TankSize { get; set; }
        float capLimit;  // Fuel.PourLimit read from the cap trigger, 0 = none
        bool capRead;    // read, or nothing to read
        float nextCapRead;
        // Half width and half length (meters) of the ground that must be flat where it parks, around
        // its origin: van sized unless set (the Gifu's is longer).
        public Vector2 ParkHalfSize { get; set; } = VanHalfSize;
        public string Contact(float within) => tune.Contact(within);
        // The driven vehicle's impacts this physics step (CrashSensor), for the engine's stall guard.
        public void Impacts(bool inImpact, float impact) => tune.Impacts(inImpact, impact);

        bool PlayerDriving
        {
            get
            {
                var v = HutongGames.PlayMaker.FsmVariables.GlobalVariables.FindFsmString("PlayerCurrentVehicle");
                return v != null && v.Value == driverName;
            }
        }

        // The active root object named name. GameObject.Find reads a '/' as a path, and the Gifu's
        // name has one (GIFU(750/450psi)), so such a name is looked up among the rigidbodies instead.
        static GameObject FindRoot(string name)
        {
            if (name.IndexOf('/') < 0) return GameObject.Find(name);
            foreach (var body in UnityEngine.Object.FindObjectsOfType<Rigidbody>())
                if (body.transform.parent == null && body.name == name) return body.gameObject;
            return null;
        }

        public void Find()
        {
            if (car != null || Time.realtimeSinceStartup < nextFind) return;
            nextFind = Time.realtimeSinceStartup + 1f;
            var go = FindRoot(objectName);
            if (go == null) return;
            car = go.transform;
            body = go.GetComponent<Rigidbody>();
            Moves++;
            placedGeneration = 0;
            held = false;
            float rest;
            Ground = ColliderBelow(car, out rest);
            clearance = Ground != null ? Mathf.Clamp(rest + RestMargin, ParkClearance, 3f) : ParkClearance;
            tune.Attach(car, body);
            wheels = new Transform[WheelModels.Length];
            int found = 0;
            for (int i = 0; i < wheels.Length; i++)
                if ((wheels[i] = car.Find(WheelModels[i])) != null) found++;
            log($"vehicle: {driverName} has {found}/{wheels.Length} wheel models, parks {clearance:0.00} m up");
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

        // The vehicle's model (VehicleModel, named like PlayerCurrentVehicle: Hayosiko, Jonnez,
        // Kekmet), sent to Skyrim once per link session: Skyrim draws the vehicle from it (VehicleView
        // finds it by name) and poses its nodes from VehicleParts.
        public void SendModel(ModelSender models, Transform player)
        {
            if (car == null) return;
            model.Id = models.IdForBuilt(driverName, () => car != null ? model.Read(driverName, car, player, models, log) : null);
        }

        // The tank's level and capacity (<vehicle>/FuelTank :: Data); either may be null.
        void FuelTank(out HutongGames.PlayMaker.FsmFloat level, out HutongGames.PlayMaker.FsmFloat capacity, System.Text.StringBuilder names)
        {
            level = capacity = null;
            var tank = car != null ? car.Find("FuelTank") : null;
            if (tank == null) return;
            foreach (var fsm in tank.GetComponents<PlayMakerFSM>())
            {
                if (fsm.FsmName != "Data") continue;
                foreach (var f in fsm.FsmVariables.FloatVariables)
                {
                    if (names != null) names.Append($" {f.Name}={f.Value:0.##}");
                    if (f.Name.IndexOf("Level", StringComparison.OrdinalIgnoreCase) >= 0) level = f;
                    else if (f.Name.IndexOf("Capacity", StringComparison.OrdinalIgnoreCase) >= 0 || f.Name.StartsWith("Max", StringComparison.OrdinalIgnoreCase)) capacity = f;
                }
            }
        }

        // The tank's level and capacity in litres; false when the vehicle or its tank isn't found.
        public bool Tank(out float level, out float capacity)
        {
            HutongGames.PlayMaker.FsmFloat l, c;
            FuelTank(out l, out c, null);
            level = l != null ? l.Value : 0f;
            if (c == null) ReadCapLimit();
            capacity = Fuel.Capacity(c != null ? c.Value : float.NaN, capLimit > 0f ? capLimit : TankSize);
            return l != null && capacity > 0f;
        }

        public void SetFuel(float level)
        {
            HutongGames.PlayMaker.FsmFloat l, c;
            FuelTank(out l, out c, null);
            if (l != null) l.Value = level;
        }

        // Which side the fuel filler is on (+1 right, -1 left) and how far ahead of the vehicle's
        // middle (Skyrim units, negative behind); false without one.
        public bool Filler(out int side, out float forward)
        {
            side = -1;
            forward = 0f;
            var filler = car != null ? car.Find("LOD/FuelFiller") : null;
            if (filler == null) return false;
            var local = car.InverseTransformPoint(filler.position);
            side = local.x >= 0f ? 1 : -1;
            forward = (float)(local.z * Coords.UnitsPerMeter);
            return true;
        }

        // Logs the tank's floats, since their names were never dumped, and fills an endless tank to
        // its capacity, so the moped from a fresh save runs.
        void Refuel()
        {
            HutongGames.PlayMaker.FsmFloat level, capacity;
            var names = new System.Text.StringBuilder();
            FuelTank(out level, out capacity, names);
            if (names.Length > 0) log($"vehicle: {driverName} fuel tank floats:{names}");
            if (EndlessFuel && level != null && capacity != null && capacity.Value > 0f && level.Value < capacity.Value)
            {
                level.Value = capacity.Value;
                log($"vehicle: filled the {driverName} tank ({level.Name} = {capacity.Name} = {capacity.Value:0.##})");
            }
        }

        // A tank without MaxCapacity (the Gifu's): the level its cap trigger stops pouring at
        // (Fuel.PourLimit), read once its FSM is initialized (PlayMaker can't give a state's actions
        // before: the Gifu's LOD is off until Kumpo is near), tried once a second until then.
        void ReadCapLimit()
        {
            if (capRead || car == null || Time.realtimeSinceStartup < nextCapRead) return;
            nextCapRead = Time.realtimeSinceStartup + 1f;
            try { capLimit = CapLimit(); }
            catch (Exception e)
            {
                capRead = true;
                log($"vehicle: {driverName} cap trigger unreadable ({e.Message}), {TankSize:0} L assumed");
            }
        }

        float CapLimit()
        {
            var cap = car.Find("LOD/FuelFiller/OpenCap");
            if (cap == null) { capRead = true; return 0f; }
            PlayMakerFSM trigger = null;
            foreach (var fsm in cap.GetComponentsInChildren<PlayMakerFSM>(true))
                if (fsm.FsmName == "Trigger") trigger = fsm;
            if (trigger == null) { capRead = true; return 0f; }
            if (trigger.Fsm == null || !trigger.Fsm.Initialized) return 0f;
            capRead = true;
            var floats = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();
            var compares = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, float>>();
            {
                var fsm = trigger;
                foreach (var f in fsm.FsmVariables.FloatVariables)
                    floats.Add(new System.Collections.Generic.KeyValuePair<string, float>(f.Name, f.Value));
                foreach (var state in fsm.FsmStates)
                    foreach (var action in state.Actions ?? new HutongGames.PlayMaker.FsmStateAction[0])
                    {
                        if (action == null || action.GetType().Name != "FloatCompare") continue;
                        var one = action.GetType().GetField("float1")?.GetValue(action) as HutongGames.PlayMaker.FsmFloat;
                        var two = action.GetType().GetField("float2")?.GetValue(action) as HutongGames.PlayMaker.FsmFloat;
                        if (one != null && two != null && one.UseVariable && !two.UseVariable)
                            compares.Add(new System.Collections.Generic.KeyValuePair<string, float>(one.Name, two.Value));
                    }
            }
            float limit = Fuel.PourLimit(floats, compares);
            var seen = new System.Text.StringBuilder();
            foreach (var f in floats) seen.Append($" {f.Key}={f.Value:0.##}");
            foreach (var c in compares) seen.Append($" {c.Key}<>{c.Value:0.##}");
            log($"vehicle: {driverName} tank has no capacity, its cap trigger has{seen}: " +
                (limit > 0f ? $"{limit:0.#} L" : $"nothing, {TankSize:0} L assumed"));
            return limit;
        }

        // The moped's fuel is endless (EndlessFuel): topped up once a second.
        void KeepFull()
        {
            HutongGames.PlayMaker.FsmFloat level, capacity;
            FuelTank(out level, out capacity, null);
            if (level == null || capacity == null || capacity.Value <= 0f || level.Value >= capacity.Value) return;
            if (!toppedUp) log($"vehicle: {driverName} fuel kept full (was {level.Value:0.##} of {capacity.Value:0.##})");
            toppedUp = true;
            level.Value = capacity.Value;
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

        // Parks the vehicle at spot (Skyrim world space, on the ground) facing heading for that
        // anchor generation, instead of next to the player.
        public void ParkAt(uint generation, Vec3 spot, float heading)
        {
            spotGeneration = generation;
            this.spot = spot;
            spotHeading = heading;
        }

        void Remember()
        {
            if (home == null) home = new Snapshot.Body { Name = objectName, InSpace = true };
            home.Position = island.IslandToSkyrim(Vec3From(car.position));
            var r = car.rotation;
            home.Rotation = new Quat(r.x, r.y, r.z, r.w);
            home.Worldspace = island.Worldspace;
            home.Cell = island.Cell;
        }

        bool InHomeSpace() => home != null && (!home.InSpace || (home.Worldspace == island.Worldspace && home.Cell == island.Cell));

        // Back where it was left, standing still (held until the ground there has arrived).
        void PutHome()
        {
            away = false;
            var at = island.SkyrimToIsland(home.Position);
            var position = new Vector3(at.x, at.y, at.z);
            var rotation = new Quaternion(home.Rotation.x, home.Rotation.y, home.Rotation.z, home.Rotation.w);
            Moves++;
            car.position = position;
            car.rotation = rotation;
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            heldVelocity = heldAngular = Vector3.zero;
            nextGroundCheck = 0f;
            log($"vehicle: the {driverName} is where it was left, ({home.Position.x:0},{home.Position.y:0},{home.Position.z:0})");
        }

        // Left in another worldspace or interior than the new anchor's: held far below the island,
        // out of the way, until the player is back in its space.
        void PutAway()
        {
            away = true;
            Hold(true);
            Moves++;
            car.position = new Vector3(car.position.x, AwayHeight, car.position.z);
            if (body != null) body.position = car.position;
            heldVelocity = heldAngular = Vector3.zero;
            log($"vehicle: the {driverName} stays in worldspace {home.Worldspace:X8} cell {home.Cell:X8}, put away for gen {island.Generation}");
        }

        // On call and not called yet: held far below the island like PutAway, not sent to Skyrim.
        void Stash()
        {
            away = true;
            Hold(true);
            Moves++;
            car.position = new Vector3(car.position.x, AwayHeight, car.position.z);
            if (body != null) body.position = car.position;
            heldVelocity = heldAngular = Vector3.zero;
            log($"vehicle: the {driverName} waits to be called, put away for gen {island.Generation}");
        }

        // The player calls the vehicle: it is parked in front of the player wherever it was (another
        // worldspace, put away, far behind). Not while the player sits in it.
        public void Summon(Transform player, Vector3 feet)
        {
            if (car == null || player == null) return;
            if (Carries(player))
            {
                log($"vehicle: the player sits in the {driverName}, not calling it");
                return;
            }
            away = false;
            calling = true;
            try { Park(player, feet); }
            finally { calling = false; }
            nextGroundCheck = 0f;  // let it go as soon as the ground under it is there
            log($"vehicle: called the {driverName}");
        }

        const float AwayHeight = -5000f;

        // Tows the vehicle onto spot (Skyrim world space, on the ground) facing heading, wherever it
        // was: a dry tank called at a pump (FuelLink). Not while the player sits in it.
        public void TowTo(Transform player, Vec3 spot, float heading)
        {
            if (car == null || player == null || Carries(player)) return;
            away = false;
            spotGeneration = island.Generation;
            this.spot = spot;
            spotHeading = heading;
            Park(player, Vector3.zero);
            nextGroundCheck = 0f;
            log($"vehicle: towed the {driverName} to ({spot.x:0},{spot.y:0},{spot.z:0}) heading {heading:0.00}, parking brake {ParkingBrake()}");
        }

        // Pulls the parking brake the way its lever does (Dashboard/ParkingBrake :: Use, docs/FSM_MAP.md):
        // the van's knob all the way out (KnobPos 0.1, which its Brake FSM reads every frame), the
        // Kekmet's lever flipped by MSC's own Flip state when it is off. Says what it did, for the log.
        string ParkingBrake()
        {
            var lever = car != null ? (car.Find("LOD/Dashboard/ParkingBrake") ?? car.Find("Dashboard/ParkingBrake")) : null;
            PlayMakerFSM use = null;
            if (lever != null)
                foreach (var f in lever.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "Use") use = f;
            if (use == null) return "missing";
            var knob = use.FsmVariables.FindFsmFloat("KnobPos");
            if (knob != null)
            {
                knob.Value = 0.1f;
                return "pulled";
            }
            var on = use.FsmVariables.FindFsmBool("Brake");
            if (on == null) return "unknown";
            if (on.Value) return "already on";
            var flip = use.Fsm.GetState("Flip");
            if (flip == null || SaveSync.SwitchState == null) return "no Flip state";
            SaveSync.SwitchState.Invoke(use.Fsm, new object[] { flip });
            return "flipped on";
        }

        // Where the vehicle is and how it moves, for a snapshot (SaveSync); null when not found.
        // Put away, it is where it was left.
        public Snapshot.Body Capture(Transform player)
        {
            if (car == null) return null;
            if (away && home == null) return null;  // on call and never called: nothing to keep
            if (away)
            {
                HutongGames.PlayMaker.FsmFloat tank, size;
                FuelTank(out tank, out size, null);
                return new Snapshot.Body
                {
                    Name = objectName, Position = home.Position, Rotation = home.Rotation, InSpace = true,
                    Worldspace = home.Worldspace, Cell = home.Cell, Fuel = tank != null ? tank.Value : float.NaN,
                };
            }
            var p = island.IslandToSkyrim(Vec3From(car.position));
            var r = car.rotation;
            var v = body != null ? (held ? heldVelocity : body.velocity) : Vector3.zero;
            var a = body != null ? (held ? heldAngular : body.angularVelocity) : Vector3.zero;
            HutongGames.PlayMaker.FsmFloat level, capacity;
            FuelTank(out level, out capacity, null);
            return new Snapshot.Body
            {
                Name = objectName,
                Position = p,
                Rotation = new Quat(r.x, r.y, r.z, r.w),
                Velocity = Vec3From(v),
                Angular = Vec3From(a),
                Fuel = level != null ? level.Value : float.NaN,
                Driven = player != null && Carries(player),
                InSpace = true,
                Worldspace = island.Worldspace,
                Cell = island.Cell,
            };
        }

        // Puts the vehicle back where a snapshot had it (the player in it comes along), on the
        // current island. Without ground there yet it is held with that speed, as when it drives off
        // the streamed area.
        public void Restore(Snapshot.Body b)
        {
            if (car == null) return;
            if (b.InSpace && (b.Worldspace != island.Worldspace || b.Cell != island.Cell))
            {
                home = b;  // the save had it in another space; it waits there
                PutAway();
                return;
            }
            away = false;
            var at = island.SkyrimToIsland(b.Position);
            var position = new Vector3(at.x, at.y, at.z);
            var rotation = new Quaternion(b.Rotation.x, b.Rotation.y, b.Rotation.z, b.Rotation.w);
            var velocity = new Vector3(b.Velocity.x, b.Velocity.y, b.Velocity.z);
            var angular = new Vector3(b.Angular.x, b.Angular.y, b.Angular.z);
            Moves++;
            car.position = position;
            car.rotation = rotation;
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                if (body.isKinematic)
                {
                    heldVelocity = velocity;
                    heldAngular = angular;
                }
                else
                {
                    body.velocity = velocity;
                    body.angularVelocity = angular;
                }
            }
            nextGroundCheck = 0f;
            restoring = true;
            HutongGames.PlayMaker.FsmFloat level, capacity;
            FuelTank(out level, out capacity, null);
            if (level != null && !float.IsNaN(b.Fuel)) level.Value = b.Fuel;
            log($"vehicle: the {driverName} is back at ({b.Position.x:0},{b.Position.y:0},{b.Position.z:0}) at {velocity.magnitude * 3.6f:0} km/h" +
                (level != null && !float.IsNaN(b.Fuel) ? $", fuel {b.Fuel:0.#}" : ""));
        }

        // True when moving the vehicle also moves the player (sitting in it, parented to it).
        public bool Carries(Transform player) => car != null && PlayerDriving && player.IsChildOf(car);

        // Moves the vehicle (and whatever rides in it) by delta, keeping its speed.
        public void Shift(Vector3 delta)
        {
            if (car == null) return;
            car.position += delta;
            Moves++;
            if (body != null) body.position = car.position;
        }

        // Brings the vehicle the player sits in to a new anchor: across so the feet land over it, and
        // down onto its wheels at the ground there. Putting the seated feet on the ground sank the
        // vehicle into it, and the physics threw it, killing the player.
        public void CarryTo(Vector3 feet, Vector3 anchorGround)
        {
            if (car == null) return;
            var delta = anchorGround - feet;
            delta.y = anchorGround.y + clearance - car.position.y;
            Shift(delta);
        }

        // A vehicle this far (meters) below the island surface under it went through it (a hard
        // crash into a thin streamed collider). Left there it sank on, the player in it dragged
        // thousands of units under Skyrim's ground, and Skyrim took that for a teleport every frame.
        const float FellThroughDepth = 1.5f;
        // The ground where it last stood may be this far (meters) from the height it was at then.
        const float StoodDrift = 3f;

        // Remembers where the vehicle stands on its wheels: upright and about a ride height over ground.
        void RememberStood(Vector3 ground)
        {
            if (body == null || body.isKinematic || car.up.y < Upright.MaxTiltCos) return;
            float height = car.position.y - ground.y;
            if (height < clearance - 0.5f || height > clearance + 0.5f) return;
            stood = island.IslandToSkyrim(Vec3From(ground));
            haveStood = true;
        }

        // The vehicle sank into the island; nearest is the closest island surface, above it. Back where
        // it last stood when that is near, else onto the surface just above it, else left alone: that
        // surface could be a cliff, and the player in a vehicle put on top of one fell from it.
        void PutBackAfterSinking(Vector3 nearest)
        {
            Vector3? stoodGround = null;
            float distance = float.MaxValue;
            if (haveStood)
            {
                var at = island.SkyrimToIsland(stood);
                var p = new Vector3(at.x, at.y, at.z);
                var g = findGround(p);
                if (g.HasValue && Mathf.Abs(g.Value.y - p.y) <= StoodDrift)
                {
                    stoodGround = g;
                    distance = Vector3.Distance(car.position, p);
                }
            }
            var pick = FallRescue.Vehicle(stoodGround.HasValue, distance, car.position.y, nearest.y);
            if (pick == FallRescue.Pick.Stood)
            {
                SetUpright(stoodGround.Value);
                log($"vehicle: the {driverName} fell through the island, put back on its wheels where it last stood, {distance:0} m away");
            }
            else if (pick == FallRescue.Pick.Above)
            {
                SetUpright(nearest);
                log($"vehicle: the {driverName} fell through the island, put back on its wheels on top");
            }
            else if (Time.realtimeSinceStartup >= nextRescueLog)
            {
                nextRescueLog = Time.realtimeSinceStartup + 5f;
                log($"vehicle: the {driverName} is under the island, {nearest.y - car.position.y:0} m below the nearest surface and nowhere it stood within reach, left alone");
            }
        }

        // The island's statics (walls, rocks) a vehicle must not be set down in: spheres along the
        // body, above the wheels so the ground under it is not counted.
        const string IslandName = "PerkeleRim.Island";
        const float BodyRadius = 0.6f;
        static readonly Vector3[] BodySpheres = { new Vector3(0f, 1.1f, -1.7f), new Vector3(0f, 1.1f, 0f), new Vector3(0f, 1.1f, 1.7f) };

        bool InIsland(Vector3 position, Quaternion rotation)
        {
            foreach (var local in BodySpheres)
                foreach (var c in Physics.OverlapSphere(position + rotation * local, BodyRadius))
                    if (c.transform.parent != null && c.transform.parent.name == IslandName) return true;
            return false;
        }

        // A vehicle restored from a save inside a wall or rock (it came back lodged in Whiterun's gate
        // wall, on its side, and bounced) is stood upright on the nearest clear spot with flat ground
        // under it (ClearSpot, the ground rule of parking it next to the player), before it is let go.
        // True when it was moved.
        bool MoveToClearSpot()
        {
            if (!InIsland(car.position, car.rotation)) return false;
            var f = car.forward;
            var u = car.up;
            var yaw = Quaternion.Euler(0f, Upright.Heading(f.x, f.z, u.x, u.z), 0f);
            var from = car.position;
            var placed = from;
            var offset = ClearSpot.First(o =>
            {
                float high;
                var center = from + yaw * new Vector3(o.x, 0f, o.z);
                if (!FlatGround(center, yaw, out high)) return false;
                placed = new Vector3(center.x, high + clearance, center.z);
                return !InIsland(placed, yaw);
            });
            if (!offset.HasValue)
            {
                log($"vehicle: the {driverName} came back inside the island's statics and no clear spot is near, left as it is");
                return false;
            }
            Moves++;
            car.position = placed;
            car.rotation = yaw;
            if (body != null)
            {
                body.position = placed;
                body.rotation = yaw;
                if (!body.isKinematic)
                {
                    body.velocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
            }
            heldVelocity = heldAngular = Vector3.zero;
            log($"vehicle: the {driverName} came back inside the island's statics, stood upright {new Vector2(offset.Value.x, offset.Value.z).magnitude:0.0} m away on clear ground");
            return true;
        }

        // On its wheels at ground, facing the way it faced (its forward flattened).
        void SetUpright(Vector3 ground)
        {
            Shift(new Vector3(ground.x - car.position.x, ground.y + clearance - car.position.y, ground.z - car.position.z));
            var f = car.forward;
            var u = car.up;
            car.rotation = Quaternion.Euler(0f, Upright.Heading(f.x, f.z, u.x, u.z), 0f);
            if (body != null)
            {
                body.rotation = car.rotation;
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        // MSC's push key next to the vehicle (the player on foot): a rolled vehicle that has come to
        // rest goes back on its wheels, lifted a little, facing the way it faced. MSC's own push only
        // moves one standing on its wheels.
        public bool RightIfRolled(Transform player)
        {
            if (car == null || body == null || held || away) return false;
            float distance = Vector3.Distance(player.position, car.position);
            if (!Upright.CanRight(car.up.y, body.velocity.magnitude, body.angularVelocity.magnitude, distance)) return false;
            var ground = findGround(car.position);
            if (!ground.HasValue) return false;
            SetUpright(ground.Value);
            log($"vehicle: put the {driverName} back on its wheels ({distance:0.0} m from the player)");
            reportUntil = Time.realtimeSinceStartup + 3f;
            nextReport = 0f;
            return true;
        }

        // Call every LateUpdate, after the player's pose was taken. playing: MSC owns the player and
        // it stands on the current island. view: MSC's first-person camera (its seat goes out while driven).
        public void Update(bool playing, Transform player, Vector3 playerFeet, Transform view)
        {
            var link = LinkHost.Current;
            var state = new VehicleState { vehicle = wireId, generation = island.Generation };
            var partState = new VehicleParts { vehicle = wireId, generation = island.Generation };
            if (car == null || !playing)
            {
                if (link != null && wireId != 0)
                {
                    link.WriteVehicle(ref state);
                    link.WriteVehicleParts(ref partState);
                }
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
                if (driven) away = false;
                else if (spotGeneration == island.Generation) Park(player, playerFeet);
                else if (home == null && onCall) Stash();
                else if (home == null) Park(player, playerFeet);
                else if (InHomeSpace()) PutHome();
                else PutAway();
            }

            if (away)
            {
                // Left in another worldspace or interior: Skyrim hides it until the player is back there.
                if (link != null && wireId != 0)
                {
                    link.WriteVehicle(ref state);
                    link.WriteVehicleParts(ref partState);
                }
                return;
            }
            Remember();
            tune.Update(driven);
            if (EndlessFuel && Time.realtimeSinceStartup >= nextTopUp)
            {
                nextTopUp = Time.realtimeSinceStartup + 1f;
                KeepFull();
            }

            if (Time.realtimeSinceStartup >= nextGroundCheck)
            {
                nextGroundCheck = Time.realtimeSinceStartup + GroundCheckEvery;
                var ground = findGround(car.position);
                if (ground.HasValue && restoring)
                {
                    restoring = false;
                    if (MoveToClearSpot()) ground = findGround(car.position);
                }
                Hold(!ground.HasValue);
                if (ground.HasValue)
                {
                    if (ground.Value.y - car.position.y > FellThroughDepth) PutBackAfterSinking(ground.Value);
                    else RememberStood(ground.Value);
                }
            }

            Report(driven);
            state.flags = Wire.VehicleValid | (driven ? Wire.VehicleDriven : 0);
            Fill(ref state);
            if (driven && view != null) FillSeat(ref state, view);
            partState.frame = (uint)Time.frameCount;
            model.Fill(car, ref partState);
            if (link != null && wireId != 0)
            {
                link.WriteVehicle(ref state);
                link.WriteVehicleParts(ref partState);
            }
        }

        // Roughly where a vehicle's wheels touch, relative to its middle (meters).
        static readonly Vector3[] WheelCorners =
        {
            new Vector3(0.7f, 0f, 1.2f), new Vector3(-0.7f, 0f, 1.2f), new Vector3(0.7f, 0f, -1.2f), new Vector3(-0.7f, 0f, -1.2f),
        };

        // The highest ground under the wheels around spot: a vehicle parked over a gap (the garage's
        // pit) stands on its sides, not on whatever is under its middle. Null when none has arrived.
        Vector3? GroundUnderWheels(Vector3 spot, Quaternion yaw)
        {
            float? top = null;
            foreach (var corner in WheelCorners)
            {
                var g = findGround(spot + yaw * corner);
                if (g.HasValue && (!top.HasValue || g.Value.y > top.Value)) top = g.Value.y;
            }
            return top.HasValue ? new Vector3(spot.x, top.Value, spot.z) : (Vector3?)null;
        }

        void Park(Transform player, Vector3 feet)
        {
            var below = ColliderBelow(car);
            if (below != null)
                log($"vehicle: {driverName} was standing on {below.name} (layer {below.gameObject.layer}, tag {below.tag}, material {(below.sharedMaterial != null ? below.sharedMaterial.name : "none")})");
            Quaternion yaw;
            Vector3 spot;
            Vector3? ground;
            if (!calling && spotGeneration == island.Generation)
            {
                var at = island.SkyrimToIsland(this.spot);
                spot = new Vector3(at.x, at.y, at.z);
                yaw = Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(spotHeading), 0f);
                ground = GroundUnderWheels(spot, yaw) ?? findGround(spot);
            }
            else
            {
                yaw = Quaternion.Euler(0f, player.eulerAngles.y, 0f);
                spot = feet + yaw * Offset;
                ground = FlatSpot(feet, yaw) ?? findGround(spot);
            }
            spot = (ground ?? spot) + Vector3.up * clearance;
            Moves++;
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
        // moped never pick the same one; a vehicle parked ahead (called) tries further ahead, then
        // ahead to either side, never behind. Parked over a rock, the vehicle spawned inside it and
        // was thrown, sometimes into the player. Returns the highest ground point there, or null.
        Vector3 Offset => calling && callOffset.HasValue ? callOffset.Value : parkOffset;

        // Whether the ground is there and flat (ClearSpot.Flat) under the whole vehicle parked at
        // center: its middle, then its four corners. high: the highest ground point of those.
        bool FlatGround(Vector3 center, Quaternion yaw, out float high)
        {
            float low = float.MaxValue;
            high = float.MinValue;
            for (int i = 0; i < 5; i++)
            {
                var local = i == 0 ? Vector3.zero : new Vector3((i & 1) != 0 ? ParkHalfSize.x : -ParkHalfSize.x, 0f, (i & 2) != 0 ? ParkHalfSize.y : -ParkHalfSize.y);
                var g = findGround(center + yaw * local);
                if (!g.HasValue) return false;
                low = Mathf.Min(low, g.Value.y);
                high = Mathf.Max(high, g.Value.y);
            }
            return ClearSpot.Flat(low, high);
        }

        Vector3? FlatSpot(Vector3 feet, Quaternion yaw)
        {
            var parkOffset = Offset;
            var outward = new Vector3(Mathf.Sign(parkOffset.x) * 3f, 0f, 0f);
            var offsets = parkOffset.x == 0f
                ? new[]
                {
                    parkOffset,
                    parkOffset + Vector3.forward * 3f,
                    parkOffset + Vector3.right * 3f,
                    parkOffset - Vector3.right * 3f,
                }
                : new[]
                {
                    parkOffset,
                    parkOffset + Vector3.forward * 4f,
                    parkOffset - Vector3.forward * 4f,
                    parkOffset + outward,
                };
            foreach (var offset in offsets)
            {
                var center = feet + yaw * offset;
                float high;
                if (FlatGround(center, yaw, out high))
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
            Moves++;
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

        // How it rides (every 2 s while driven, every half second for a while after righting it):
        // speed, tilt, MSC's center of mass and the frame time, to tell a tip-over or a stall apart.
        void Report(bool driven)
        {
            float now = Time.realtimeSinceStartup;
            bool righted = now < reportUntil;
            if ((!driven && !righted) || now < nextReport || body == null) return;
            nextReport = now + (righted ? 0.5f : 2f);
            var e = car.eulerAngles;
            float roll = e.z > 180f ? e.z - 360f : e.z;
            float pitch = e.x > 180f ? e.x - 360f : e.x;
            var speed = held ? heldVelocity : body.velocity;
            log($"vehicle: {driverName} {(driven ? "ridden" : "righted")} {speed.magnitude * 3.6f:0} km/h, roll {roll:0}, pitch {pitch:0}, " +
                $"center of mass {body.centerOfMass}, {(body.isKinematic ? "kinematic, " : "")}frame {Time.deltaTime * 1000f:0} ms, {body.mass:0} kg");
        }

        // Where MSC's camera is in the vehicle right now (Skyrim axes and units in the vehicle's frame):
        // Skyrim hangs the driven vehicle around its own camera by this. A seat Skyrim learned while
        // stopped went stale whenever the camera moved in the vehicle (leaning, the moped's head
        // joint, its kickstand tilt), and the controls were drawn off from where MSC picks them.
        void FillSeat(ref VehicleState s, Transform view)
        {
            var local = Quaternion.Inverse(car.rotation) * (view.position - car.position);
            var seat = Coords.UnityToSkyrim(Vec3From(local));
            s.seat[0] = seat.x;
            s.seat[1] = seat.y;
            s.seat[2] = seat.z;
            s.flags |= Wire.VehicleSeat;
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

        static Vec3 Vec3From(Vector3 v) => new Vec3(v.x, v.y, v.z);

        // The first collider straight below t that isn't part of t itself.
        static Collider ColliderBelow(Transform t)
        {
            float height;
            return ColliderBelow(t, out height);
        }

        // Also how high t's origin is above it (meters).
        static Collider ColliderBelow(Transform t, out float height)
        {
            float best = float.MaxValue;
            Collider ground = null;
            foreach (var hit in Physics.RaycastAll(t.position + Vector3.up, Vector3.down, 10f))
            {
                if (hit.collider.transform.IsChildOf(t) || hit.distance >= best) continue;
                best = hit.distance;
                ground = hit.collider;
            }
            height = ground != null ? best - 1f : 0f;
            return ground;
        }
    }
}
