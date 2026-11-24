using System;
using System.Collections.Generic;
using PerkeleRim.Protocol;
using UnityEngine;

namespace PerkeleRim.Game
{
    // The Satsuma bay next to the garage (GarageBay, layout and slot map in Link/SatsumaBay):
    //  - the one-time move (BayMoveSatsuma): the Satsuma's body onto its stand, as MSC has it built
    //    (attached parts stay on it), and every loose Satsuma part at MSC's home onto its slot;
    //  - the garage board (BayPartsHome, BayVehiclesHome): every loose Satsuma part on the island
    //    or at MSC's home back onto its slot, every vehicle to its spot by the garage (the body too);
    //  - BayStand: a save that had the move, with an MSC that hasn't placed the body this session
    //    (restarted since): the body back on its stand;
    //  - BayCheck: counts the Satsuma's parts on the island on and off their slots, for the test kit.
    // Waits for the island's ground under the whole bay (20 s at most), then does it all in one frame
    // and answers GarageDone. MSC's shops and junkyard keep their parts: only home and the island count.
    //
    // It also keeps the Satsuma's loose parts on the island where they lie in Skyrim (Keep): the
    // island is only Skyrim's ground around Kumpo, so a part left behind (on the bay's shelves while he
    // drives to Riverwood) would lose the ground under it and fall, and a new anchor would move it. A
    // kept part's Skyrim pose and space are noted while it lies on ground; without ground it is held
    // kinematic there, in another worldspace or interior it waits far below, and on each new anchor it
    // goes back to its pose. After MSC starts again its parts on the island are where its own save had
    // them for an anchor long gone: held where they are, and the first stand request of the session
    // (Kumpo near the bay of a save that had the move) lays them all on their slots again.
    // MSC's spanner set goes with the parts (on the floor by the body's nose) and is kept the same way.
    public unsafe class GarageMover
    {
        public const string SpannerSetPath = "ITEMS/spanner set(itemx)";
        public static bool IsSpannerSet(Transform t) => t.root.name == "ITEMS" && t.parent == t.root && t.name == "spanner set(itemx)";
        const float GroundWait = 20f;
        const float Clearance = 0.02f;   // meters between a part's bottom and the surface it's set on
        const float HomeRadius = 60f;    // meters around MSC's home (YARD/Building/Garage)
        const float IslandBelow = 300f;  // Unity y: above it is the island (at y 1000), below MSC's own map
        const float OnSlot = 0.35f;      // meters from its slot (flat) a part still counts as on it
        const float BodyDrop = 0.03f;    // meters above the stand the body is set down at
        const float LostBelow = -200f;   // Unity y: below MSC's own map, fallen (or put away by Keep)
        const float AwayHeight = -5000f; // Unity y kept parts in another space wait at
        const float KeepScan = 1f;       // seconds between looking for parts to keep

        readonly Island island;
        readonly Func<Vector3, Vector3?> findGround;
        readonly Action<string> log;
        readonly Func<VehicleLink> satsuma;
        readonly Func<VehicleLink[]> vehicles;
        readonly Func<Transform> player;
        readonly List<GarageMessages.Request> pending = new List<GarageMessages.Request>();
        float since;
        bool bodyPlaced;  // this MSC session has set the body down on its stand (or seen it placed)

        class Kept
        {
            public Rigidbody Body;
            public bool Known;          // Position, Rotation and the space are noted (it lay on ground)
            public Vec3 Position;       // Skyrim world space
            public Quaternion Rotation; // Unity's (the island only moves, never turns)
            public uint Worldspace, Cell;
            public bool Held;           // kinematic by Keep
            public bool Away;           // waiting below, in another space than the island's
        }
        readonly Dictionary<Rigidbody, Kept> kept = new Dictionary<Rigidbody, Kept>();
        float nextKeepScan;
        uint keptGeneration;

        public GarageMover(Island island, Func<Vector3, Vector3?> findGround, Func<VehicleLink> satsuma, Func<VehicleLink[]> vehicles, Func<Transform> player, Action<string> log)
        {
            this.island = island;
            this.findGround = findGround;
            this.satsuma = satsuma;
            this.vehicles = vehicles;
            this.player = player;
            this.log = log;
        }

        public void OnMessage(byte[] payload, int size)
        {
            GarageMessages.Request r;
            if (!GarageMessages.DecodeBay(payload, size, out r))
            {
                log($"garage: bad GarageBay ({size} bytes)");
                return;
            }
            if (pending.Count == 0) since = Time.realtimeSinceStartup;
            pending.Add(r);
            log($"garage: asked for {What(r.What)} for gen {r.Generation}");
        }

        // Every LateUpdate. playing: MSC owns the player and it stands on the current island.
        public void Update(Link link, bool playing)
        {
            Keep(playing);
            if (pending.Count == 0 || !playing || link == null) return;
            var r = pending[0];
            if (r.Generation != island.Generation)
            {
                log($"garage: the island moved on to gen {island.Generation}, {What(r.What)} for gen {r.Generation} dropped");
                Answer(link, GarageMessages.Done(r, false, false, 0, 0, 0, 0));
                return;
            }
            if (!BayGround(r))
            {
                if (Time.realtimeSinceStartup - since < GroundWait) return;
                log($"garage: no ground at the bay after {GroundWait} s, {What(r.What)} not done");
                Answer(link, GarageMessages.Done(r, false, false, 0, 0, 0, 0));
                return;
            }

            bool body = false;
            int slotted = 0, floor = 0, brought = 0, missed = 0;
            var car = satsuma();
            // The first stand request since MSC started: its parts on the island are where its save had
            // them for another anchor; the bay is laid out again, as by the board.
            bool restarted = (r.What & Wire.BayStand) != 0 && !bodyPlaced;
            if ((r.What & (Wire.BayMoveSatsuma | Wire.BayVehiclesHome)) != 0 || restarted)
                body = SetBodyDown(car, r);
            else body = car != null && car.Car != null && OnStand(car.Car, r);
            if ((r.What & (Wire.BayMoveSatsuma | Wire.BayPartsHome)) != 0 || restarted)
            {
                PutParts(r, (r.What & Wire.BayPartsHome) != 0 || restarted, out slotted, out floor);
                PutSpannerSet(r, (r.What & Wire.BayPartsHome) != 0 || restarted);
            }
            if ((r.What & Wire.BayVehiclesHome) != 0)
                brought = BringVehicles(r);
            if ((r.What & Wire.BayCheck) != 0)
                Count(r, out slotted, out missed);
            log($"garage: {What(r.What)} done: body {(body ? "on its stand" : "not there")}, {slotted} parts on their slots, {floor} on the floor, {brought} vehicles home{((r.What & Wire.BayCheck) != 0 ? $", {missed} off their slots" : "")}");
            Answer(link, GarageMessages.Done(r, true, body, slotted, floor, brought, missed));
        }

        void Answer(Link link, GarageDone done)
        {
            pending.RemoveAt(0);
            since = Time.realtimeSinceStartup;
            var payload = new byte[sizeof(GarageDone)];
            fixed (byte* p = payload) *(GarageDone*)p = done;
            if (!link.Send(MessageType.GarageDone, payload, payload.Length)) log("garage: GarageDone didn't fit in the ring");
        }

        static string What(uint what)
        {
            var parts = new List<string>();
            if ((what & Wire.BayMoveSatsuma) != 0) parts.Add("the move");
            if ((what & Wire.BayPartsHome) != 0) parts.Add("parts home");
            if ((what & Wire.BayVehiclesHome) != 0) parts.Add("vehicles home");
            if ((what & Wire.BayStand) != 0) parts.Add("the stand");
            if ((what & Wire.BayCheck) != 0) parts.Add("a check");
            return parts.Count > 0 ? string.Join(" + ", parts.ToArray()) : "nothing";
        }

        Vector3 Island(float x, float y, float z)
        {
            var at = island.SkyrimToIsland(new Vec3(x, y, z));
            return new Vector3(at.x, at.y, at.z);
        }

        // Whether the island has its ground under the whole bay: the stand, every shelf and the
        // floor's corners (the bay spans more than one collision chunk).
        bool BayGround(GarageMessages.Request r)
        {
            if (!findGround(BayPoint(r, SatsumaBay.BodyX, SatsumaBay.BodyY, SatsumaBay.StandTop)).HasValue) return false;
            foreach (var shelf in SatsumaBay.Shelves)
                if (!findGround(BayPoint(r, shelf.X, shelf.Y, SatsumaBay.BoardHeights[SatsumaBay.BoardHeights.Length - 1])).HasValue) return false;
            float fx = SatsumaBay.FloorX[SatsumaBay.FloorX.Length - 1] + SatsumaBay.FloorHalf - 40f, fy = SatsumaBay.FloorHalf - 40f;
            foreach (var c in new[] { new Vector2(fx, fy), new Vector2(fx, -fy), new Vector2(-fx, fy), new Vector2(-fx, -fy) })
                if (!findGround(BayPoint(r, c.x, c.y, 0f)).HasValue) return false;
            return true;
        }

        Vector3 BayPoint(GarageMessages.Request r, float bx, float by, float bz)
        {
            float x, y, z;
            SatsumaBay.ToWorld(bx, by, bz, r.Position.x, r.Position.y, r.Position.z, r.Heading, out x, out y, out z);
            return Island(x, y, z);
        }

        // Unity yaw of a direction in the bay frame (radians clockwise from the bay's +X).
        static Quaternion Yaw(GarageMessages.Request r, float along) => Quaternion.Euler(0f, Coords.SkyrimAngleToUnity(r.Heading + along), 0f);

        // ---- The body ----

        bool SetBodyDown(VehicleLink link, GarageMessages.Request r)
        {
            if (link == null) return false;
            link.Find();
            var car = link.Car;
            if (car == null) { log("garage: no Satsuma in MSC"); return false; }
            var p = player();
            if (p != null && p.IsChildOf(car)) { log("garage: Kumpo sits in the Satsuma, left where it is"); return false; }
            var rotation = Yaw(r, 0f);
            var at = BayPoint(r, SatsumaBay.BodyX, SatsumaBay.BodyY, SatsumaBay.StandTop);
            // The stand's top under the body: the higher of its two crates.
            float top = float.MinValue;
            foreach (var x in SatsumaBay.StandX)
            {
                var g = findGround(BayPoint(r, SatsumaBay.BodyX + x, SatsumaBay.BodyY, SatsumaBay.StandTop));
                if (g.HasValue) top = Mathf.Max(top, g.Value.y);
            }
            if (top == float.MinValue) top = at.y;
            car.rotation = rotation;
            car.position = at;
            float bottom;
            if (Bottom(car, out bottom)) at.y += top + BodyDrop - bottom;
            link.Pinned = true;  // held still on its stand: on two crates it slid off
            link.SetDown(at, rotation);
            bodyPlaced = true;
            log($"garage: the Satsuma's body on its stand at ({at.x:0.00},{at.y:0.00},{at.z:0.00}), stand top {top:0.00}");
            return true;
        }

        bool OnStand(Transform car, GarageMessages.Request r)
        {
            var at = BayPoint(r, SatsumaBay.BodyX, SatsumaBay.BodyY, SatsumaBay.StandTop);
            var d = car.position - at;
            d.y = 0f;
            return d.magnitude < 0.5f;
        }

        // ---- The parts ----

        // MSC's home: its garage, else the yard.
        static Vector3? Home()
        {
            var garage = GameObject.Find("YARD/Building/Garage");
            if (garage != null) return garage.transform.position;
            var yard = GameObject.Find("YARD");
            return yard != null ? yard.transform.position : (Vector3?)null;
        }

        // The Satsuma's loose parts: MSC keeps them under CARPARTS (PartsCar, PartsMotor, PartsGT, ...)
        // tagged PART while off the car, named like a part (SatsumaModel.IsPart, as SatsumaParts has
        // them); at home (within HomeRadius of it, in MSC's own map) or, with island, on the island
        // too, and the lost ones (fallen below MSC's map, or waiting below for another space). Not the
        // ones in Kumpo's hands, nor the parts' own triggers.
        List<Rigidbody> LooseParts(bool island)
        {
            var list = new List<Rigidbody>();
            var home = Home();
            var p = player();
            var car = satsuma() != null ? satsuma().Car : null;
            foreach (Rigidbody body in UnityEngine.Object.FindObjectsOfType(typeof(Rigidbody)))
            {
                var t = body.transform;
                if (!body.gameObject.activeInHierarchy || !t.CompareTag("PART") || t.root.name != "CARPARTS" || !SatsumaModel.IsPart(t.name, HasRemoval(t))) continue;
                if ((p != null && t.IsChildOf(p)) || (car != null && t.IsChildOf(car))) continue;
                bool onIsland = t.position.y > IslandBelow || t.position.y < LostBelow;
                bool atHome = !onIsland && home.HasValue && Flat(t.position - home.Value) < HomeRadius;
                if (atHome || (island && onIsland)) list.Add(body);
            }
            // Same parts, same order: by name, then by MSC's instance id.
            list.Sort((a, b) =>
            {
                int byName = string.CompareOrdinal(a.name, b.name);
                return byName != 0 ? byName : a.GetInstanceID().CompareTo(b.GetInstanceID());
            });
            return list;
        }

        static bool HasRemoval(Transform t)
        {
            foreach (var fsm in t.GetComponents<PlayMakerFSM>())
                if (fsm.FsmName == "Removal") return true;
            return false;
        }

        static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        List<SatsumaBay.Part> Describe(List<Rigidbody> bodies)
        {
            var parts = new List<SatsumaBay.Part>();
            foreach (var b in bodies)
            {
                var size = Size(b.transform);
                parts.Add(new SatsumaBay.Part { Name = b.name, Longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z)), Height = Mathf.Min(size.x, Mathf.Min(size.y, size.z)) });
            }
            return parts;
        }

        void PutParts(GarageMessages.Request r, bool fromIsland, out int slotted, out int floor)
        {
            slotted = floor = 0;
            var bodies = LooseParts(fromIsland);
            var placed = SatsumaBay.Assign(Describe(bodies));
            for (int i = 0; i < bodies.Count; i++)
            {
                var t = bodies[i].transform;
                Vector3 spot;
                float along;
                if (placed[i].Slot >= 0)
                {
                    var s = SatsumaBay.Slots[placed[i].Slot];
                    spot = BayPoint(r, s.X, s.Y, s.Z);
                    along = s.Along;
                    if (SatsumaBay.SlotsFor(t.name).Contains(placed[i].Slot)) slotted++;  // its own (shelf or floor)
                    else floor++;
                }
                else
                {
                    float x, y, z;
                    SatsumaBay.PileSpot(placed[i].Pile, out x, out y, out z);
                    spot = BayPoint(r, x, y, 0f);
                    along = 0f;
                    floor++;
                    // On the floor's apron, then a layer up for each grid full.
                    var g = findGround(spot);
                    if (g.HasValue) spot = g.Value;
                    spot += Vector3.up * (z / 70f);
                }
                Place(bodies[i], spot, Yaw(r, along));
                Forget(bodies[i]);  // Keep notes its new place once it lies there
            }
            log($"garage: {bodies.Count} loose parts {(fromIsland ? "at home and on the island" : "at home")} put in the bay");
        }

        // The spanner set, at home or (fromIsland) on the island, onto its spot by the body's nose.
        void PutSpannerSet(GarageMessages.Request r, bool fromIsland)
        {
            var go = GameObject.Find(SpannerSetPath);
            var body = go != null ? go.GetComponent<Rigidbody>() : null;
            var p = player();
            if (body == null || (p != null && go.transform.IsChildOf(p))) { log("garage: no spanner set to put in the bay"); return; }
            var t = go.transform;
            var home = Home();
            bool onIsland = t.position.y > IslandBelow || t.position.y < LostBelow;
            bool atHome = !onIsland && home.HasValue && Flat(t.position - home.Value) < HomeRadius;
            if (!atHome && !(fromIsland && onIsland)) { log($"garage: the spanner set is elsewhere ({t.position}), left there"); return; }
            Place(body, BayPoint(r, SatsumaBay.ToolX, SatsumaBay.ToolY, 0f), Yaw(r, 0f));
            Forget(body);
            log("garage: the spanner set put by the Satsuma's nose");
        }

        // Sets the part on the surface at spot (a board, the floor): lying on its flattest side (round
        // parts on their edge rolled off the boards), its long side along the slot.
        static readonly Quaternion[] Sides = { Quaternion.identity, Quaternion.Euler(90f, 0f, 0f), Quaternion.Euler(0f, 0f, 90f) };

        void Place(Rigidbody body, Vector3 spot, Quaternion yaw)
        {
            var t = body.transform;
            t.position = spot;
            var along = yaw * Vector3.forward;
            Quaternion best = yaw;
            float bestHeight = float.MaxValue, bestAlong = 0f;
            foreach (var side in Sides)
                foreach (var turn in new[] { 0f, 90f })
                {
                    var rotation = yaw * Quaternion.Euler(0f, turn, 0f) * side;
                    t.rotation = rotation;
                    var size = Size(t);
                    float a = Mathf.Abs(size.x * along.x) + Mathf.Abs(size.z * along.z);
                    // Flattest first (a centimetre's difference doesn't count), then longest along the slot.
                    if (size.y < bestHeight - 0.01f || (Mathf.Abs(size.y - bestHeight) <= 0.01f && a > bestAlong))
                    {
                        best = rotation;
                        bestHeight = Mathf.Min(bestHeight, size.y);
                        bestAlong = a;
                    }
                }
            t.rotation = best;
            var ground = findGround(spot);
            if (ground.HasValue && spot.y - ground.Value.y > 0.1f) ground = spot;  // a pile's upper layer: on the parts below
            float bottom;
            if (Bottom(t, out bottom) && ground.HasValue) t.position += Vector3.up * (ground.Value.y + Clearance - bottom);
            body.position = t.position;
            body.rotation = t.rotation;
            if (!body.isKinematic)
            {
                body.velocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }
        }

        void Count(GarageMessages.Request r, out int on, out int off)
        {
            on = off = 0;
            // Every one on the island (and the lost ones): a part left in a dungeon is off its slot too.
            var bodies = LooseParts(true);
            var inBay = new List<Rigidbody>();
            foreach (var b in bodies)
                if (b.transform.position.y > IslandBelow || b.transform.position.y < LostBelow) inBay.Add(b);
            var placed = SatsumaBay.Assign(Describe(inBay));
            for (int i = 0; i < inBay.Count; i++)
            {
                if (placed[i].Slot < 0 || !SatsumaBay.Listed(inBay[i].name)) continue;
                var s = SatsumaBay.Slots[placed[i].Slot];
                var spot = BayPoint(r, s.X, s.Y, s.Z);
                float off2 = Flat(inBay[i].transform.position - spot);
                if (off2 < OnSlot) on++;
                else
                {
                    off++;
                    if (off <= 12) log($"garage: {inBay[i].name} is {off2:0.00} m off its slot {placed[i].Slot} (kind {s.Kind}, at {inBay[i].transform.position}, slot {spot})");
                }
            }
        }

        // An island point in the bay's frame (Skyrim units).
        Vector2 ToBay(GarageMessages.Request r, Vector3 unity)
        {
            var s = island.IslandToSkyrim(new Vec3(unity.x, unity.y, unity.z));
            float dx = s.x - r.Position.x, dy = s.y - r.Position.y;
            float sh = (float)Math.Sin(r.Heading), ch = (float)Math.Cos(r.Heading);
            return new Vector2(dx * sh + dy * ch, -dx * ch + dy * sh);
        }

        // ---- Keeping the parts in Skyrim ----

        // Every LateUpdate. Parts on the island keep their place in Skyrim (see the top).
        void Keep(bool playing)
        {
            var p = player();
            var car = satsuma() != null ? satsuma().Car : null;
            if (Time.realtimeSinceStartup >= nextKeepScan)
            {
                nextKeepScan = Time.realtimeSinceStartup + KeepScan;
                foreach (Rigidbody body in UnityEngine.Object.FindObjectsOfType(typeof(Rigidbody)))
                {
                    var t = body.transform;
                    if (kept.ContainsKey(body) || !body.gameObject.activeInHierarchy || t.position.y < IslandBelow) continue;  // MSC's own map (home, shops), or lost
                    bool part = t.CompareTag("PART") && t.root.name == "CARPARTS" && SatsumaModel.IsPart(t.name, HasRemoval(t));
                    if (!part && !IsSpannerSet(t)) continue;
                    kept[body] = new Kept { Body = body };
                }
            }
            var gone = new List<Rigidbody>();
            bool newAnchor = playing && island.HasAnchor && island.Generation != keptGeneration;
            if (newAnchor) keptGeneration = island.Generation;
            foreach (var k in kept.Values)
            {
                var body = k.Body;
                if (body == null || !body.gameObject.activeInHierarchy) { gone.Add(body); continue; }
                var t = body.transform;
                // In Kumpo's hands, or on the car: not ours until it lies loose again.
                if ((p != null && t.IsChildOf(p)) || (car != null && t.IsChildOf(car)) || (t.root.name != "CARPARTS" && !IsSpannerSet(t)))
                {
                    if (k.Held) { body.isKinematic = false; k.Held = false; }
                    gone.Add(body);
                    continue;
                }
                if (!playing || !island.HasAnchor)
                {
                    // MSC on its own, or no island yet (MSC just started): held where it is.
                    if (t.position.y > IslandBelow && !k.Held) Hold(k, true);
                    continue;
                }
                if (newAnchor && k.Known) { PutBack(k); continue; }
                if (k.Away && t.position.y > LostBelow)
                {
                    // Taken from its waiting spot (by MSC, or the spike's fetch): noted anew where it lies.
                    k.Away = false;
                    k.Known = false;
                    Hold(k, false);
                }
                if (k.Away) continue;
                var ground = findGround(t.position);
                if (ground.HasValue)
                {
                    if (k.Held) Hold(k, false);
                    if (!k.Known || !body.isKinematic)
                    {
                        // Where it lies in Skyrim, and in which space.
                        k.Position = island.IslandToSkyrim(new Vec3(t.position.x, t.position.y, t.position.z));
                        k.Rotation = t.rotation;
                        k.Worldspace = island.Worldspace;
                        k.Cell = island.Cell;
                        k.Known = true;
                    }
                }
                else if (!k.Held)
                {
                    if (k.Known) PutBack(k);  // it may have dropped a little: back where it lay
                    Hold(k, true);
                }
            }
            foreach (var b in gone) kept.Remove(b);
        }

        // Back to its noted pose on the current island, held until its ground is there; in another
        // space, put away far below.
        void PutBack(Kept k)
        {
            var t = k.Body.transform;
            if (k.Worldspace != island.Worldspace || k.Cell != island.Cell)
            {
                k.Away = true;
                t.position = new Vector3(t.position.x, AwayHeight, t.position.z);
            }
            else
            {
                k.Away = false;
                var at = island.SkyrimToIsland(k.Position);
                t.position = new Vector3(at.x, at.y, at.z);
                t.rotation = k.Rotation;
            }
            k.Body.position = t.position;
            k.Body.rotation = t.rotation;
            Hold(k, true);
        }

        static void Hold(Kept k, bool hold)
        {
            k.Held = hold;
            if (!hold)
            {
                k.Body.isKinematic = false;
                return;
            }
            if (!k.Body.isKinematic)
            {
                k.Body.velocity = Vector3.zero;
                k.Body.angularVelocity = Vector3.zero;
            }
            k.Body.isKinematic = true;
        }

        // A part GarageMover just put somewhere: released, and noted again once it lies there.
        void Forget(Rigidbody body)
        {
            Kept k;
            if (!kept.TryGetValue(body, out k)) return;
            if (k.Held) body.isKinematic = false;
            kept.Remove(body);
        }

        // ---- The vehicles ----

        // Where each goes, in the bay's frame (Skyrim units, heading from the bay's +X): the van onto
        // the garage's pit (the garage is 520 to the bay's right, its pit's top about 63 up), the rest
        // in a row on the open ground in front of both.
        static readonly string[] SpotNames = { "Hayosiko", "Jonnez", "Kekmet", "Gifu" };
        static readonly float[][] Spots =
        {
            new[] { 0f, -520f, 63f, 0f },
            new[] { 560f, -250f, 0f, 0f },
            new[] { 600f, 60f, 0f, 0f },
            new[] { 760f, -520f, 0f, 0f },
        };

        int BringVehicles(GarageMessages.Request r)
        {
            int n = 0;
            var p = player();
            foreach (var v in vehicles())
            {
                int i = Array.IndexOf(SpotNames, v.Name);
                if (i < 0 || v.Car == null || p == null || v.Carries(p)) continue;
                var s = Spots[i];
                float x, y, z;
                SatsumaBay.ToWorld(s[0], s[1], s[2], r.Position.x, r.Position.y, r.Position.z, r.Heading, out x, out y, out z);
                v.TowTo(p, new Vec3(x, y, z), r.Heading + s[3]);
                n++;
            }
            return n;
        }

        // ---- Shapes ----

        static Vector3 Size(Transform t)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var r in t.GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled) continue;
                if (!any) { b = r.bounds; any = true; }
                else b.Encapsulate(r.bounds);
            }
            return any ? b.size : new Vector3(0.3f, 0.3f, 0.3f);
        }

        // How far the part reaches along a flat direction (renderer bounds follow a turn at once).
        static float Extent(Transform t, Vector3 along)
        {
            var size = Size(t);
            return Mathf.Abs(size.x * along.x) + Mathf.Abs(size.z * along.z);
        }

        // The lowest point of what it draws (renderer bounds follow a move at once, collider bounds
        // only after the next physics step); for the body that's its attached parts too.
        static bool Bottom(Transform t, out float bottom)
        {
            bottom = float.MaxValue;
            foreach (var r in t.GetComponentsInChildren<Renderer>())
                if (r.enabled) bottom = Mathf.Min(bottom, r.bounds.min.y);
            return bottom != float.MaxValue;
        }
    }
}
